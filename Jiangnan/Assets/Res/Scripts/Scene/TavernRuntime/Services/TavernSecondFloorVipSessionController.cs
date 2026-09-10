using System.Collections;
using System.Collections.Generic;
using JN.Client;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.UI;
using QFramework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Video;

namespace JN.Client.Scene
{
    /// <summary>
    /// 二楼贵客包厢会话：入座先说话再点单；贵客菜单每批 2 道（一做一上），
    /// 两道一起吃完后播一次飞钱与评价气泡，再点下一批，共 6 道后结账；
    /// 中途切大众菜单点单则差评离店；开局即大众菜单仍吃一道后差评离店。
    /// 支持存档快照续跑。
    /// </summary>
    public sealed class TavernSecondFloorVipSessionController : MonoBehaviour
    {
        private const int TotalDishes = TavernSecondFloorVipService.ProductPlacementCount;
        /// <summary>贵客菜单每次点单对应的菜品数：厨师一次做、小二一次上。</summary>
        private const int VipMenuDishesPerOrder = 2;
        private const string CheckoutCoinIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/checkout.png";
        private const string PopularMenuOrderIconPath = "Assets/Res/Resources/Textures/UI/Icons/menu1.png";
        private const string VipMenuOrderIconPath = "Assets/Res/Resources/Textures/UI/Icons/menu2.png";
        private const string ColaServeIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/vip_Cola.png";
        private const string ColaServeIconFallbackPath = "Assets/Res/Resources/Textures/UI/Icons 1/酒.png";
        private const string ColaServeVideoPath = "Assets/Res/Resources/Videos/vipCola.mp4";
        private const string ColaServeCaption = "上可乐";
        /// <summary>上可乐相对上菜按钮的 UI 像素偏移（右侧）。</summary>
        private static readonly Vector2 ColaServeScreenOffset = new(260f, 16f);
        private const float VipSpeechBubbleSeconds = 2f;
        private const float FarewellBubbleSeconds = 3f;
        private const float VipMenuCookDurationScale = 0.45f;
        private const int TipCheckoutClickCount = 6;
        private const string VipSitOrderLine = "把最好的端上来";
        private const string VipTipThanksLine = "我很满意，给你小费";
        private const string VipMenuFarewellLine = "今日尽兴，改日再来";
        private const string PopularMenuFirstComplaintLine = "都是些粗茶淡饭";
        private const string PopularMenuSecondComplaintLine = "饭菜欠妥浅尝即可";
        private static readonly string[] VipMenuDishPraiseLines =
        {
            "嗯！这味儿地道！",
            "这鸭子香，来一只！",
            "好酒，再来一壶！",
            "对味儿，接着上！",
            "来碟点心，压压酒。"
        };
        /// <summary>入座贵客头顶文案气泡偏移（X 微调纠正坐姿网格相对根节点偏左）。</summary>
        private static readonly Vector3 VipSeatedHudOffset =
            new(0.2f, TavernWorldRuntimeHudLayout.CustomerWaitHeightOffset, 0f);
        /// <summary>点单/结账按钮相对文案再略向左，避免按钮看起来偏右。</summary>
        private static readonly Vector3 VipSeatedButtonOffset =
            new(0.05f, TavernWorldRuntimeHudLayout.CustomerWaitHeightOffset, 0f);

        private const string ChefCookState = "Cook";
        private const string ChefBaseLayerCookState = "Base Layer.Cook";
        private const string ChefCookTrigger = "TrCook";
        private const float ChefCookAnimPulseSeconds = 1.35f;

        [SerializeField] private float staffMoveArriveDistance = 0.35f;
        [SerializeField] private float staffMoveTimeoutSeconds = 12f;

        private readonly List<Transform> productPlacements = new();
        private readonly GameObject[] slotVisuals = new GameObject[TotalDishes];

        private Transform waiterPoint;
        private Transform foodPickupPoint;
        private Transform kitchenDishTable;
        private readonly Transform[] foodMakePoints = new Transform[TavernSecondFloorVipService.FoodMakePointCount];
        private Transform vipEndPoint;
        private GameObject waiterRoot;
        private GameObject chefRoot;
        private bool ownsSpawnedWaiter;
        private NavMeshAgent waiterAgent;
        private Animator waiterAnimator;
        private Animator chefAnimator;
        private bool waiterHasSpeed;

        private Coroutine sessionRoutine;
        private readonly Queue<int> pendingServeDishQueue = new();
        private readonly GameObject[] stagedDishesAtMakePoints =
            new GameObject[TavernSecondFloorVipService.FoodMakePointCount];
        private readonly bool[] servedFlags = new bool[TotalDishes];
        private GameObject checkoutBubbleRoot;
        private GameObject orderBubbleRoot;
        private GameObject colaServeBubbleRoot;
        private bool orderClicked;
        private bool colaServed;
        private bool colaServing;
        private bool sessionRunning;
        private bool vipSeated;
        private bool vipSessionEndedByPopularSwitch;
        private int checkoutDoneCount;
        private int tipCheckoutClickCount;
        private int eatenCount;
        private GameObject waiterCarryPlate;
        private GameObject stagedKitchenDish;
        private readonly List<GameObject> stagedKitchenBatchDishes = new();
        private static TavernSecondFloorVipSessionController activeInstance;

        public static TavernSecondFloorVipSessionController FindOrCreate()
        {
            var existing = FindFirstObjectByType<TavernSecondFloorVipSessionController>();
            if (existing != null)
            {
                return existing;
            }

            var host = new GameObject("TavernSecondFloorVipSession");
            return host.AddComponent<TavernSecondFloorVipSessionController>();
        }

        /// <summary>
        /// 若存档有二楼贵客则启动包厢会话（可重复调用，已在跑则忽略）。
        /// </summary>
        public void TryBeginSession()
        {
            if (sessionRunning)
            {
                return;
            }

            if (!TavernSecondFloorVipService.HasSecondFloorVipGuest())
            {
                return;
            }

            if (!TavernSecondFloorVipService.TrySpawnSeatedVipOnSecondFloor())
            {
                return;
            }

            if (!ResolveAnchorsAndStaff())
            {
                Debug.LogWarning("[SecondFloorVipSession] 挂点或员工缺失，无法启动包厢会话。");
                return;
            }

            sessionRunning = true;
            sessionRoutine = StartCoroutine(SessionRoutine());
        }

        public void StopSession()
        {
            // 切场景前落盘当前进度，回二楼可续（小费已结清则标记已清，不再落盘）。
            if (sessionRunning && TavernSecondFloorVipService.HasSecondFloorVipGuest())
            {
                PersistSnapshot();
            }

            StopAllSessionCoroutines();
            HideCheckoutBubble();
            HideOrderBubble();
            HideColaServeBubble();
            if (colaServing)
            {
                VideoWindowController.HideActiveWindow();
                colaServing = false;
            }
            ReleaseVipReviewTip();
            ClearWaiterCarryPlate();
            ClearAllStagedKitchenDishes();
            GameAudioManager.StopChefCook(chefRoot);
            ResetChefCookAnimation();
            // 小二招待结束后保留，下楼切场景前再销毁。
            sessionRunning = false;
        }

        /// <summary>下楼回一楼前调用：落盘快照、停会话并销毁二楼生成的小二。</summary>
        public static void CleanupBeforeLeaveFirstFloor()
        {
            var session = FindFirstObjectByType<TavernSecondFloorVipSessionController>();
            if (session == null)
            {
                return;
            }

            session.StopSession();
            session.DestroySpawnedWaiter();
        }

        /// <summary>把坐下/上菜/已吃/已结账写入存档。</summary>
        private void PersistSnapshot(bool saveImmediately = true)
        {
            TavernSecondFloorVipService.WriteSecondFloorVipSnapshot(
                vipSeated,
                CountServedDishes(),
                eatenCount,
                checkoutDoneCount,
                colaServed,
                saveImmediately);
        }

        private int CountServedDishes()
        {
            var count = 0;
            for (var i = 0; i < servedFlags.Length; i++)
            {
                if (servedFlags[i])
                {
                    count++;
                }
            }

            return count;
        }

        private void StopAllSessionCoroutines()
        {
            if (sessionRoutine != null)
            {
                StopCoroutine(sessionRoutine);
                sessionRoutine = null;
            }

            pendingServeDishQueue.Clear();
        }

        private void OnEnable()
        {
            activeInstance = this;
            Signals.Get<TavernRuntimeChangedSignal>().RemoveListener(HandleRuntimeChangedForOrderBubble);
            Signals.Get<TavernRuntimeChangedSignal>().AddListener(HandleRuntimeChangedForOrderBubble);
        }

        private void OnDisable()
        {
            Signals.Get<TavernRuntimeChangedSignal>().RemoveListener(HandleRuntimeChangedForOrderBubble);
            if (activeInstance == this)
            {
                activeInstance = null;
            }
        }

        private void HandleRuntimeChangedForOrderBubble()
        {
            ApplyOrderBubbleMenuVisuals();
        }

        private void OnDestroy()
        {
            Signals.Get<TavernRuntimeChangedSignal>().RemoveListener(HandleRuntimeChangedForOrderBubble);
            if (activeInstance == this)
            {
                activeInstance = null;
            }

            HideCheckoutBubble();
            HideOrderBubble();
            HideColaServeBubble();
            if (colaServing)
            {
                VideoWindowController.HideActiveWindow();
                colaServing = false;
            }
            ReleaseVipReviewTip();
            ClearWaiterCarryPlate();
            ClearAllStagedKitchenDishes();
            DestroySpawnedWaiter();
        }

        private bool ResolveAnchorsAndStaff()
        {
            productPlacements.Clear();
            productPlacements.AddRange(TavernSecondFloorVipService.CollectProductPlacements());
            if (productPlacements.Count < TotalDishes)
            {
                Debug.LogWarning(
                    $"[SecondFloorVipSession] ProductPlacement 不足：需要 {TotalDishes}，实际 {productPlacements.Count}。");
                return false;
            }

            waiterPoint = TavernSecondFloorVipService.ResolveNamedTransform(
                TavernSecondFloorVipService.WaiterPointName);
            // 小二取餐导航点：优先 foodPoint。
            foodPickupPoint = TavernSecondFloorVipService.ResolveFoodPickupPoint();
            // 成品出餐视觉：FoodMakePoint/Point1~N；小二取餐导航仍走 foodPoint。
            kitchenDishTable = TavernSecondFloorVipService.ResolveKitchenDishTable();
            var resolvedMakePoints = TavernSecondFloorVipService.CollectFoodMakePoints();
            for (var index = 0; index < foodMakePoints.Length && index < resolvedMakePoints.Length; index++)
            {
                foodMakePoints[index] = resolvedMakePoints[index];
            }

            vipEndPoint = TavernSecondFloorVipService.ResolveNamedTransform(
                TavernSecondFloorVipService.VipEndPointName);
            chefRoot = TavernSecondFloorVipService.ResolveNamedGameObject(
                TavernSecondFloorVipService.ChefCharacterName);

            if (waiterPoint == null || foodPickupPoint == null || vipEndPoint == null || chefRoot == null)
            {
                return false;
            }

            // 对齐一楼：从 SO Waiter03 生成，出生点 = waiterPoint。
            DestroySpawnedWaiter();
            waiterRoot = TavernSecondFloorVipService.SpawnWaiter03AtPoint(waiterPoint);
            if (waiterRoot == null)
            {
                Debug.LogWarning("[SecondFloorVipSession] SO Waiter03 生成失败。");
                return false;
            }

            ownsSpawnedWaiter = true;
            chefRoot.SetActive(true);

            waiterAgent = EnsureAgent(waiterRoot);
            waiterAnimator = waiterRoot.GetComponentInChildren<Animator>(true);
            chefAnimator = chefRoot.GetComponentInChildren<Animator>(true);
            waiterHasSpeed = HasFloatParam(waiterAnimator, "Speed");

            WarpToPoint(waiterAgent, waiterRoot.transform, waiterPoint.position, waiterPoint.rotation);
            return true;
        }

        private void DestroySpawnedWaiter()
        {
            ClearWaiterCarryPlate();
            ClearAllStagedKitchenDishes();
            if (!ownsSpawnedWaiter)
            {
                waiterRoot = null;
                waiterAgent = null;
                waiterAnimator = null;
                return;
            }

            if (waiterRoot != null)
            {
                Destroy(waiterRoot);
            }

            waiterRoot = null;
            waiterAgent = null;
            waiterAnimator = null;
            ownsSpawnedWaiter = false;
        }

        private IEnumerator SessionRoutine()
        {
            // 从存档恢复：已上菜 / 已吃 / 已结账 / 是否已坐下。
            TavernSecondFloorVipService.TryReadSecondFloorVipSnapshot(
                out vipSeated,
                out var servedCount,
                out eatenCount,
                out checkoutDoneCount);
            colaServed = DataManager.Instance?.SaveData?.tavern != null
                         && DataManager.Instance.SaveData.tavern.secondFloorVipColaServed;
            colaServing = false;

            for (var i = 0; i < servedFlags.Length; i++)
            {
                servedFlags[i] = i < servedCount;
            }

            RestoreTableVisualsFromSnapshot(servedCount, eatenCount);

            // 已坐：Bind 时已直坐，EnterAndSit 会立刻结束；未坐：从 VipEndPoint 走进来。
            yield return EnterVipAndSitRoutine();
            vipSeated = true;
            PersistSnapshot();

            // 第一道批：入座说话后出点单；贵客菜单按每批两道循环，大众菜单保持原点单后差评离店。
            var needsFirstOrder = !servedFlags[0] && eatenCount <= 0;
            if (needsFirstOrder)
            {
                GameAudioManager.PlayVipSatisfied();
                yield return ShowVipSpeechRoutine(VipSitOrderLine, VipSpeechBubbleSeconds);
                yield return AwaitPlayerOrderRoutine();
            }

            if (!IsVipMenuSelected())
            {
                yield return ServeFirstDishRoutine();
                yield return PopularMenuDissatisfiedLeaveRoutine();
                sessionRoutine = null;
                sessionRunning = false;
                yield break;
            }

            yield return VipMenuSerialDishesRoutine();
            if (!vipSessionEndedByPopularSwitch)
            {
                yield return FinishSessionRoutine();
            }

            sessionRoutine = null;
            sessionRunning = false;
        }

        /// <summary>大众菜单：点单后做第一道、按表用餐并差评离店（点单已在入座后完成）。</summary>
        private IEnumerator ServeFirstDishRoutine()
        {
            if (!servedFlags[0])
            {
                yield return CookDishRoutine(0, shortenSteps: 0);
                yield return ServeDishRoutine(0);
                servedFlags[0] = true;
                PersistSnapshot();
            }

            if (eatenCount < 1)
            {
                yield return DineRoutine(0);
            }
        }

        /// <summary>
        /// 贵客菜单：每次点单做/上两道；厨师一次做完，小二端一道实际摆两道，
        /// 本批两道一起吃完后播一次飞钱与评价，再出下一批点单，共三批到第六道。
        /// 第二批起若点单时已切大众菜单，则走大众反馈并离店。
        /// 首批点单已在入座后完成，此处不再重复等待。
        /// </summary>
        private IEnumerator VipMenuSerialDishesRoutine()
        {
            vipSessionEndedByPopularSwitch = false;
            for (var batchStart = 0; batchStart < TotalDishes; batchStart += VipMenuDishesPerOrder)
            {
                var batchEnd = Mathf.Min(batchStart + VipMenuDishesPerOrder, TotalDishes);
                if (eatenCount >= batchEnd)
                {
                    continue;
                }

                var needsCookAndServe = false;
                for (var dishIndex = batchStart; dishIndex < batchEnd; dishIndex++)
                {
                    if (!servedFlags[dishIndex])
                    {
                        needsCookAndServe = true;
                        break;
                    }
                }

                if (needsCookAndServe)
                {
                    // 首批点单在 SessionRoutine 已完成；后续批次吃完评价后再等玩家点单。
                    if (batchStart > 0)
                    {
                        yield return AwaitPlayerOrderRoutine();
                        if (!IsVipMenuSelected())
                        {
                            yield return ServePopularDishThenLeaveRoutine(batchStart);
                            vipSessionEndedByPopularSwitch = true;
                            yield break;
                        }
                    }

                    yield return CookDishBatchRoutine(batchStart, batchEnd);
                    yield return ServeDishBatchRoutine(batchStart, batchEnd);
                    for (var dishIndex = batchStart; dishIndex < batchEnd; dishIndex++)
                    {
                        servedFlags[dishIndex] = true;
                    }

                    PersistSnapshot();
                }

                // 本批未吃完的菜一起吃：一次用餐时长 → 一次飞钱入账 → 一次评价气泡。
                if (eatenCount < batchEnd)
                {
                    yield return DineBatchQuickRoutine(batchStart, batchEnd);
                }

                // 吃完本批后评价，再进入下一批点单；最后一批直接进小费结账。
                if (batchEnd < TotalDishes && eatenCount >= batchEnd)
                {
                    yield return ShowVipSpeechRoutine(ResolveRandomVipDishPraiseLine(), VipSpeechBubbleSeconds);
                }
            }
        }

        /// <summary>中途切大众菜单点单：按大众流程做这道、读表用餐后差评离店。</summary>
        private IEnumerator ServePopularDishThenLeaveRoutine(int dishIndex)
        {
            if (!servedFlags[dishIndex])
            {
                yield return CookDishRoutine(dishIndex, shortenSteps: 0);
                yield return ServeDishRoutine(dishIndex);
                servedFlags[dishIndex] = true;
                PersistSnapshot();
            }

            if (eatenCount <= dishIndex)
            {
                yield return DineRoutine(dishIndex);
            }

            yield return PopularMenuDissatisfiedLeaveRoutine();
        }

        private void ClearStagedDishAtMakePoint(int pointIndex)
        {
            if (pointIndex < 0 || pointIndex >= stagedDishesAtMakePoints.Length)
            {
                return;
            }

            if (stagedDishesAtMakePoints[pointIndex] != null)
            {
                Destroy(stagedDishesAtMakePoints[pointIndex]);
                stagedDishesAtMakePoints[pointIndex] = null;
            }
        }

        private void PopStagedDishForServe(int dishIndex)
        {
            var pointIndex = TavernSecondFloorVipService.ResolveFoodMakePointIndex(dishIndex);
            ClearStagedDishAtMakePoint(pointIndex);
        }

        private void ClearAllStagedKitchenDishes()
        {
            ClearStagedKitchenDish();
            ClearStagedKitchenBatchDishes();
            for (var index = 0; index < stagedDishesAtMakePoints.Length; index++)
            {
                ClearStagedDishAtMakePoint(index);
            }

            pendingServeDishQueue.Clear();
        }

        /// <summary>厨师一次做本批菜：单次做菜时长，出餐台上并排放两道。</summary>
        private IEnumerator CookDishBatchRoutine(int batchStart, int batchEndExclusive)
        {
            var cookSeconds = Mathf.Max(
                0.2f,
                TbConfigRuntime.GetSecondFloorVipCookDurationSeconds() * Mathf.Max(0.1f, VipMenuCookDurationScale));
            PlayChefCookAnimation();
            GameAudioManager.PlayChefCook(chefRoot);
            if (chefRoot != null)
            {
                HudOverlayService.ShowChefCookProgress(
                    chefRoot.transform,
                    cookSeconds,
                    new Vector3(0f, TavernWorldRuntimeHudLayout.ChefProgressHeightOffset, 0f));
            }

            var remaining = cookSeconds;
            var nextPulseAt = Time.time + ChefCookAnimPulseSeconds;
            while (remaining > 0f)
            {
                remaining -= Time.deltaTime;
                if (Time.time >= nextPulseAt)
                {
                    PlayChefCookAnimation();
                    nextPulseAt = Time.time + ChefCookAnimPulseSeconds;
                }

                yield return null;
            }

            GameAudioManager.StopChefCook(chefRoot);
            ResetChefCookAnimation();
            StageCookedDishBatchOnFoodMakePoints(batchStart, batchEndExclusive);
        }

        /// <summary>小二一次上本批未上桌的菜：端一道视觉，到桌后本批一起摆上。</summary>
        private IEnumerator ServeDishBatchRoutine(int batchStart, int batchEndExclusive)
        {
            if (batchStart < 0 || batchEndExclusive <= batchStart || batchStart >= productPlacements.Count)
            {
                yield break;
            }

            var safeEnd = Mathf.Min(batchEndExclusive, productPlacements.Count, TotalDishes);
            var firstUnserved = -1;
            for (var dishIndex = batchStart; dishIndex < safeEnd; dishIndex++)
            {
                if (!servedFlags[dishIndex])
                {
                    firstUnserved = dishIndex;
                    break;
                }
            }

            if (firstUnserved < 0)
            {
                yield break;
            }

            var carryPrefab = TavernSecondFloorVipService.GetDishPrefab(firstUnserved);
            if (waiterAgent == null || foodPickupPoint == null)
            {
                ClearStagedKitchenBatchDishes();
                ClearStagedKitchenDish();
                for (var dishIndex = batchStart; dishIndex < safeEnd; dishIndex++)
                {
                    if (!servedFlags[dishIndex])
                    {
                        PlaceDishVisual(dishIndex, withFood: true);
                    }
                }

                yield break;
            }

            yield return MoveStaffRoutine(
                waiterAgent,
                waiterRoot,
                waiterAnimator,
                waiterHasSpeed,
                foodPickupPoint.position);
            FaceToward(waiterRoot.transform, foodPickupPoint.position);
            yield return new WaitForSeconds(0.15f);
            ClearStagedKitchenBatchDishes();
            ClearStagedKitchenDish();
            AttachWaiterCarryPlate(carryPrefab);

            var firstPlacement = productPlacements[firstUnserved];
            var standAt = ResolveServeStandPosition(firstPlacement);
            yield return MoveStaffRoutine(
                waiterAgent,
                waiterRoot,
                waiterAnimator,
                waiterHasSpeed,
                standAt);
            if (firstPlacement != null)
            {
                FaceToward(waiterRoot.transform, firstPlacement.position);
            }

            ClearWaiterCarryPlate();
            for (var dishIndex = batchStart; dishIndex < safeEnd; dishIndex++)
            {
                if (!servedFlags[dishIndex])
                {
                    PlaceDishVisual(dishIndex, withFood: true);
                }
            }

            yield return new WaitForSeconds(0.15f);

            if (waiterPoint != null)
            {
                yield return MoveStaffRoutine(
                    waiterAgent,
                    waiterRoot,
                    waiterAnimator,
                    waiterHasSpeed,
                    waiterPoint.position);
                if (waiterRoot != null)
                {
                    waiterRoot.transform.rotation = waiterPoint.rotation;
                }
            }

            SetStaffSpeed(waiterAnimator, waiterHasSpeed, 0f);
        }

        /// <summary>
        /// 本批未上桌成品按 FoodMakePoint/Point1~N 依次摆放（不做人工偏移）。
        /// </summary>
        private void StageCookedDishBatchOnFoodMakePoints(int batchStart, int batchEndExclusive)
        {
            ClearStagedKitchenDish();
            ClearStagedKitchenBatchDishes();
            for (var index = 0; index < stagedDishesAtMakePoints.Length; index++)
            {
                ClearStagedDishAtMakePoint(index);
            }

            // 本批按顺序占 Point1、Point2…（一次最多两道，不会与下一批叠放）。
            var makeSlot = 0;
            for (var dishIndex = batchStart; dishIndex < batchEndExclusive && dishIndex < TotalDishes; dishIndex++)
            {
                if (servedFlags[dishIndex])
                {
                    continue;
                }

                var pointIndex = Mathf.Clamp(makeSlot, 0, foodMakePoints.Length - 1);
                var anchor = pointIndex >= 0 && pointIndex < foodMakePoints.Length
                    ? foodMakePoints[pointIndex]
                    : null;
                if (anchor == null)
                {
                    anchor = kitchenDishTable;
                }

                if (anchor == null)
                {
                    makeSlot++;
                    continue;
                }

                var dishPrefab = TavernSecondFloorVipService.GetDishPrefab(dishIndex);
                var staged = TavernSecondFloorVipService.CreatePlateVisualAt(anchor, dishPrefab);
                if (staged == null)
                {
                    makeSlot++;
                    continue;
                }

                // CreatePlateVisualAt 已挂到挂点原点；勿再叠厨房桌面偏移。
                if (pointIndex >= 0 && pointIndex < stagedDishesAtMakePoints.Length)
                {
                    ClearStagedDishAtMakePoint(pointIndex);
                    stagedDishesAtMakePoints[pointIndex] = staged;
                }

                stagedKitchenBatchDishes.Add(staged);
                makeSlot++;
            }
        }

        private void ClearStagedKitchenBatchDishes()
        {
            for (var index = 0; index < stagedKitchenBatchDishes.Count; index++)
            {
                var staged = stagedKitchenBatchDishes[index];
                if (staged != null)
                {
                    Destroy(staged);
                }
            }

            stagedKitchenBatchDishes.Clear();
            // 批量子对象可能同时记在 makePoint 槽，销毁后只清引用，避免二次 Destroy。
            for (var index = 0; index < stagedDishesAtMakePoints.Length; index++)
            {
                stagedDishesAtMakePoints[index] = null;
            }
        }

        /// <summary>大众菜单：第一道吃完后两句差评，再起身离店（不结账）。</summary>
        private IEnumerator PopularMenuDissatisfiedLeaveRoutine()
        {
            yield return ShowVipSpeechRoutine(PopularMenuFirstComplaintLine, VipSpeechBubbleSeconds);
            yield return ShowVipSpeechRoutine(PopularMenuSecondComplaintLine, VipSpeechBubbleSeconds);
            yield return LeaveAndCleanupRoutine();
        }

        private static bool IsVipMenuSelected()
        {
            return DataManager.Instance != null && DataManager.Instance.IsVipMenuSelected();
        }

        /// <summary>按快照摆桌：已吃→空盘；已上未吃→有菜。</summary>
        private void RestoreTableVisualsFromSnapshot(int servedCount, int eatenCountSnapshot)
        {
            for (var dishIndex = 0; dishIndex < TotalDishes; dishIndex++)
            {
                if (dishIndex < eatenCountSnapshot)
                {
                    PlaceDishVisual(dishIndex, withFood: false);
                }
                else if (dishIndex < servedCount)
                {
                    PlaceDishVisual(dishIndex, withFood: true);
                }
            }
        }

        /// <summary>入座后立刻显示点餐按钮，点击后厨师才开始做第一道菜。</summary>
        private IEnumerator AwaitPlayerOrderRoutine()
        {
            orderClicked = false;
            var vip = TavernSecondFloorVipService.SpawnedVipRoot;
            if (vip == null)
            {
                orderClicked = true;
                yield break;
            }

            ShowOrderBubble();
            while (!orderClicked)
            {
                yield return null;
            }

            HideOrderBubble();
        }

        private IEnumerator ShowVipSpeechRoutine(string line, float seconds)
        {
            var vip = TavernSecondFloorVipService.SpawnedVipRoot;
            var target = vip != null ? vip.transform : null;
            var duration = Mathf.Max(0.1f, seconds);
            if (target == null)
            {
                yield return new WaitForSeconds(duration);
                yield break;
            }

            HudOverlayService.ShowCustomerReviewTip(
                target,
                line,
                durationSeconds: duration,
                worldOffset: VipSeatedHudOffset);
            yield return new WaitForSeconds(duration);
            ReleaseVipReviewTip();
        }

        private static string ResolveRandomVipDishPraiseLine()
        {
            if (VipMenuDishPraiseLines == null || VipMenuDishPraiseLines.Length == 0)
            {
                return "对味儿，接着上！";
            }

            return VipMenuDishPraiseLines[UnityEngine.Random.Range(0, VipMenuDishPraiseLines.Length)];
        }

        private void ReleaseVipReviewTip()
        {
            var vip = TavernSecondFloorVipService.SpawnedVipRoot;
            if (vip != null)
            {
                HudOverlayService.ReleaseCustomerReviewTip(vip.transform);
            }
        }

        private IEnumerator EnterVipAndSitRoutine()
        {
            var driver = TavernSecondFloorVipService.GetVipSeatDriver();
            if (driver == null)
            {
                yield break;
            }

            yield return driver.EnterAndSitRoutine();
        }

        private IEnumerator CookDishRoutine(
            int dishIndex,
            int shortenSteps,
            bool stageOnKitchenTable = true,
            float durationScale = 1f)
        {
            // 厨师默认站位已是做菜点：不移动，只播做菜动画、进度条与音效。
            var cookSeconds = Mathf.Max(
                0.2f,
                (TbConfigRuntime.GetSecondFloorVipCookDurationSeconds()
                 - 0.2f * Mathf.Max(0, shortenSteps)) * Mathf.Max(0.1f, durationScale));
            PlayChefCookAnimation();
            GameAudioManager.PlayChefCook(chefRoot);
            if (chefRoot != null)
            {
                HudOverlayService.ShowChefCookProgress(
                    chefRoot.transform,
                    cookSeconds,
                    new Vector3(0f, TavernWorldRuntimeHudLayout.ChefProgressHeightOffset, 0f));
            }

            var remaining = cookSeconds;
            var nextPulseAt = Time.time + ChefCookAnimPulseSeconds;
            while (remaining > 0f)
            {
                remaining -= Time.deltaTime;
                if (Time.time >= nextPulseAt)
                {
                    PlayChefCookAnimation();
                    nextPulseAt = Time.time + ChefCookAnimPulseSeconds;
                }

                yield return null;
            }

            GameAudioManager.StopChefCook(chefRoot);
            ResetChefCookAnimation();
            if (stageOnKitchenTable)
            {
                StageCookedDishOnKitchenTable(dishIndex);
            }
        }

        private IEnumerator ServeDishRoutine(int dishIndex, bool fromKitchenQueue = false)
        {
            var placement = productPlacements[dishIndex];
            var dishPrefab = TavernSecondFloorVipService.GetDishPrefab(dishIndex);
            if (waiterAgent == null || foodPickupPoint == null || placement == null)
            {
                ClearWaiterCarryPlate();
                if (fromKitchenQueue)
                {
                    PopStagedDishForServe(dishIndex);
                }
                else
                {
                    ClearStagedKitchenDish();
                }

                PlaceDishVisual(dishIndex, withFood: true);
                yield break;
            }

            // 取餐导航始终走 foodPoint；连做菜品视觉仍在 FoodMakePoint/PointN。
            yield return MoveStaffRoutine(
                waiterAgent,
                waiterRoot,
                waiterAnimator,
                waiterHasSpeed,
                foodPickupPoint.position);
            FaceToward(waiterRoot.transform, foodPickupPoint.position);
            yield return new WaitForSeconds(0.15f);
            if (fromKitchenQueue)
            {
                PopStagedDishForServe(dishIndex);
            }
            else
            {
                ClearStagedKitchenDish();
            }

            AttachWaiterCarryPlate(dishPrefab);

            // 站在桌子旁上菜（不要寻路到桌面 ProductPlacement，否则会穿模上台面）
            var standAt = ResolveServeStandPosition(placement);
            yield return MoveStaffRoutine(
                waiterAgent,
                waiterRoot,
                waiterAnimator,
                waiterHasSpeed,
                standAt);
            FaceToward(waiterRoot.transform, placement.position);
            ClearWaiterCarryPlate();
            PlaceDishVisual(dishIndex, withFood: true);
            yield return new WaitForSeconds(0.15f);

            if (waiterPoint != null)
            {
                yield return MoveStaffRoutine(
                    waiterAgent,
                    waiterRoot,
                    waiterAnimator,
                    waiterHasSpeed,
                    waiterPoint.position);
                if (waiterRoot != null)
                {
                    waiterRoot.transform.rotation = waiterPoint.rotation;
                }
            }

            SetStaffSpeed(waiterAnimator, waiterHasSpeed, 0f);
        }

        /// <summary>
        /// 上菜站位：从桌心/挂点朝 waiterPoint 外侧偏移，再吸附 NavMesh，避免走到桌面上。
        /// </summary>
        private Vector3 ResolveServeStandPosition(Transform placement)
        {
            var fallback = waiterPoint != null ? waiterPoint.position : (waiterRoot != null ? waiterRoot.transform.position : Vector3.zero);
            if (placement == null)
            {
                return fallback;
            }

            var table = FindFirstObjectByType<TableArea>();
            var tableCenter = table != null ? table.transform.position : placement.position;
            var outward = fallback - tableCenter;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f)
            {
                outward = placement.position - tableCenter;
                outward.y = 0f;
            }

            if (outward.sqrMagnitude < 0.0001f)
            {
                outward = Vector3.forward;
            }

            outward.Normalize();
            // 站在桌缘外侧，不要踩到 ProductPlacement（桌面）。
            var candidate = tableCenter + outward * 1.15f;
            candidate.y = fallback.y;

            if (NavMesh.SamplePosition(candidate, out var hit, 1.75f, NavMesh.AllAreas))
            {
                return hit.position;
            }

            if (NavMesh.SamplePosition(fallback, out var homeHit, 2f, NavMesh.AllAreas))
            {
                return homeHit.position;
            }

            return fallback;
        }

        private void AttachWaiterCarryPlate(GameObject dishPrefab)
        {
            ClearWaiterCarryPlate();
            if (waiterRoot == null || dishPrefab == null)
            {
                return;
            }

            waiterCarryPlate = TavernSecondFloorVipService.CreateWaiterCarryPlate(waiterRoot, dishPrefab);
        }

        private void ClearWaiterCarryPlate()
        {
            if (waiterCarryPlate == null)
            {
                return;
            }

            Destroy(waiterCarryPlate);
            waiterCarryPlate = null;
        }

        /// <summary>
        /// 单道成品摆到 FoodMakePoint 对应子节点；缺挂点时回退厨房桌。
        /// </summary>
        private void StageCookedDishOnKitchenTable(int dishIndex)
        {
            ClearStagedKitchenDish();
            var pointIndex = TavernSecondFloorVipService.ResolveFoodMakePointIndex(dishIndex);
            var anchor = pointIndex >= 0 && pointIndex < foodMakePoints.Length
                ? foodMakePoints[pointIndex]
                : null;
            if (anchor == null)
            {
                anchor = kitchenDishTable;
            }

            if (anchor == null)
            {
                return;
            }

            var dishPrefab = TavernSecondFloorVipService.GetDishPrefab(dishIndex);
            stagedKitchenDish = TavernSecondFloorVipService.CreatePlateVisualAt(anchor, dishPrefab);
            if (stagedKitchenDish == null)
            {
                return;
            }

            if (pointIndex >= 0 && pointIndex < stagedDishesAtMakePoints.Length)
            {
                ClearStagedDishAtMakePoint(pointIndex);
                stagedDishesAtMakePoints[pointIndex] = stagedKitchenDish;
            }
        }

        private void ClearStagedKitchenDish()
        {
            if (stagedKitchenDish == null)
            {
                return;
            }

            Destroy(stagedKitchenDish);
            stagedKitchenDish = null;
        }

        private static Vector3 ResolveKitchenTableSurfaceLocalOffset(Transform table)
        {
            if (table == null)
            {
                return Vector3.zero;
            }

            var renderers = table.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                // foodPoint 等空挂点：直接摆在挂点原点。
                return Vector3.zero;
            }

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
            {
                if (renderers[index] != null)
                {
                    bounds.Encapsulate(renderers[index].bounds);
                }
            }

            var topWorld = new Vector3(bounds.center.x, bounds.max.y + 0.02f, bounds.center.z);
            return table.InverseTransformPoint(topWorld);
        }

        private IEnumerator DineRoutine(int dishIndex)
        {
            var dineSeconds = TbConfigRuntime.GetSecondFloorVipDineDurationSeconds();
            var vip = TavernSecondFloorVipService.SpawnedVipRoot;
            var driver = TavernSecondFloorVipService.GetVipSeatDriver();
            if (vip != null)
            {
                // 用餐进度条：挂在贵客头顶（入座坐姿偏移）。
                HudOverlayService.ShowChefCookProgress(
                    vip.transform,
                    dineSeconds,
                    VipSeatedHudOffset);
            }

            driver?.StartEatingAnimation();
            const float eatLoopRetriggerInterval = 1.1f;
            var elapsed = 0f;
            var nextRetriggerTime = eatLoopRetriggerInterval;
            while (elapsed < dineSeconds)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (elapsed >= nextRetriggerTime)
                {
                    driver?.RetriggerEatingAnimation();
                    nextRetriggerTime += eatLoopRetriggerInterval;
                }
            }

            driver?.StopEatingAnimation();
            PlaceDishVisual(dishIndex, withFood: false);
            eatenCount++;
            PersistSnapshot();
            if (eatenCount >= TotalDishes)
            {
                ClearAllSlotVisuals();
            }
        }

        /// <summary>贵客菜单用餐：时长读 Config，不挂进度条，吃完后按普通菜价自动结账。</summary>
        private IEnumerator DineQuickRoutine(int dishIndex)
        {
            var dineSeconds = TbConfigRuntime.GetSecondFloorVipDineDurationSeconds();
            var driver = TavernSecondFloorVipService.GetVipSeatDriver();
            driver?.StartEatingAnimation();
            yield return new WaitForSeconds(dineSeconds);
            driver?.StopEatingAnimation();
            PlaceDishVisual(dishIndex, withFood: false);
            eatenCount++;
            PersistSnapshot();
            if (checkoutDoneCount <= dishIndex)
            {
                yield return AutoCheckoutAfterDishRoutine(dishIndex);
            }

            if (eatenCount >= TotalDishes)
            {
                ClearAllSlotVisuals();
            }
        }

        /// <summary>
        /// 本批多道一起吃：一次用餐动画与时长，吃完后一次飞钱并按道数入账。
        /// </summary>
        private IEnumerator DineBatchQuickRoutine(int batchStart, int batchEndExclusive)
        {
            var safeStart = Mathf.Max(batchStart, eatenCount);
            var safeEnd = Mathf.Clamp(batchEndExclusive, safeStart, TotalDishes);
            if (safeStart >= safeEnd)
            {
                yield break;
            }

            var dineSeconds = TbConfigRuntime.GetSecondFloorVipDineDurationSeconds();
            var driver = TavernSecondFloorVipService.GetVipSeatDriver();
            driver?.StartEatingAnimation();
            const float eatLoopRetriggerInterval = 1.1f;
            var elapsed = 0f;
            var nextRetriggerTime = eatLoopRetriggerInterval;
            while (elapsed < dineSeconds)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (elapsed >= nextRetriggerTime)
                {
                    driver?.RetriggerEatingAnimation();
                    nextRetriggerTime += eatLoopRetriggerInterval;
                }
            }

            driver?.StopEatingAnimation();
            for (var dishIndex = safeStart; dishIndex < safeEnd; dishIndex++)
            {
                PlaceDishVisual(dishIndex, withFood: false);
            }

            eatenCount = safeEnd;
            PersistSnapshot();

            if (checkoutDoneCount < safeEnd)
            {
                yield return AutoCheckoutAfterBatchRoutine(safeStart, safeEnd);
            }

            if (eatenCount >= TotalDishes)
            {
                ClearAllSlotVisuals();
            }
        }

        /// <summary>每道吃完：飞钱/音效并按普通菜单价入账（不含小费结账）。</summary>
        private IEnumerator AutoCheckoutAfterDishRoutine(int dishIndex)
        {
            yield return SecondFloorVipCoinCollectionPresenter.PlayRoutine(
                SecondFloorVipCoinCollectionPresenter.ResolveDefaultFlySource(),
                SecondFloorVipCoinCollectionPresenter.Profile.PerDish);
            AwardPerDishCheckout(dishIndex);
        }

        /// <summary>本批吃完：只播一次飞钱，再按未结道数逐道入账。</summary>
        private IEnumerator AutoCheckoutAfterBatchRoutine(int batchStart, int batchEndExclusive)
        {
            var awardStart = Mathf.Max(batchStart, checkoutDoneCount);
            var awardEnd = Mathf.Clamp(batchEndExclusive, awardStart, TotalDishes);
            if (awardStart >= awardEnd)
            {
                yield break;
            }

            yield return SecondFloorVipCoinCollectionPresenter.PlayRoutine(
                SecondFloorVipCoinCollectionPresenter.ResolveDefaultFlySource(),
                SecondFloorVipCoinCollectionPresenter.Profile.PerDish);
            for (var dishIndex = awardStart; dishIndex < awardEnd; dishIndex++)
            {
                AwardPerDishCheckout(dishIndex);
            }
        }

        private void AwardPerDishCheckout(int dishIndex)
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null)
            {
                checkoutDoneCount = Mathf.Max(checkoutDoneCount, dishIndex + 1);
                PersistSnapshot();
                return;
            }

            var income = ResolvePopularDishCheckoutIncome();
            dataManager.ChangeCoinNum(income);
            dataManager.RecordVipCheckout(income);
            checkoutDoneCount = Mathf.Max(checkoutDoneCount, dishIndex + 1);
            PersistSnapshot();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        /// <summary>普通菜单价：按酒楼等级取桌位基础价，不加贵客菜单/贵客倍率。</summary>
        private static int ResolvePopularDishCheckoutIncome()
        {
            var tavernLevel = DataManager.Instance != null ? DataManager.Instance.GetTavernLevel() : 1;
            return Mathf.Max(1, TbConfigRuntime.GetTableCheckoutIncomeForLevel(tavernLevel, 120));
        }

        private IEnumerator FinishSessionRoutine()
        {
            HideOrderBubble();
            if (tipCheckoutClickCount < TipCheckoutClickCount)
            {
                if (tipCheckoutClickCount <= 0)
                {
                    yield return ShowVipSpeechRoutine(VipTipThanksLine, VipSpeechBubbleSeconds);
                }

                yield return AwaitTipCheckoutRoutine();
            }

            yield return ShowVipSpeechRoutine(VipMenuFarewellLine, FarewellBubbleSeconds);
            yield return LeaveAndCleanupRoutine();
        }

        /// <summary>六道吃完后出结账按钮；需点 6 次，每次飞 40 金币，结束后入账并标记贵客已离。</summary>
        private IEnumerator AwaitTipCheckoutRoutine()
        {
            ShowCheckoutBubble();
            while (tipCheckoutClickCount < TipCheckoutClickCount)
            {
                yield return null;
            }

            HideCheckoutBubble();
            yield return new WaitForSeconds(0.75f);
            AwardFinalVipCheckout();
            // 小费点完即视为该贵客标记消失，下楼再上楼不再刷出（不等离店动画）。
            TavernSecondFloorVipService.SetSecondFloorVipGuest(false);
        }

        private void HandleTipCheckoutClick()
        {
            if (tipCheckoutClickCount >= TipCheckoutClickCount)
            {
                return;
            }

            tipCheckoutClickCount++;
            SecondFloorVipCoinCollectionPresenter.PlayTipCheckoutClick(
                SecondFloorVipCoinCollectionPresenter.ResolveDefaultFlySource());
        }

        private IEnumerator LeaveAndCleanupRoutine()
        {
            HideCheckoutBubble();
            HideOrderBubble();
            HideColaServeBubble();
            if (colaServing)
            {
                VideoWindowController.HideActiveWindow();
                colaServing = false;
            }
            ReleaseVipReviewTip();
            ClearWaiterCarryPlate();
            ClearAllStagedKitchenDishes();
            ClearAllSlotVisuals();

            TavernSecondFloorVipService.SetSecondFloorVipGuest(false);

            var driver = TavernSecondFloorVipService.GetVipSeatDriver();
            var vipLeft = false;
            if (driver != null)
            {
                yield return driver.LeaveToPointRoutine(vipEndPoint, () => vipLeft = true);
            }
            else
            {
                vipLeft = true;
            }

            while (!vipLeft)
            {
                yield return null;
            }

            TavernSecondFloorVipService.ClearSpawnedVipRuntime();

            // 小二回默认位并保留，下楼切场景前再销毁。
            if (waiterPoint != null && waiterRoot != null)
            {
                WarpToPoint(waiterAgent, waiterRoot.transform, waiterPoint.position, waiterPoint.rotation);
            }

            GameAudioManager.StopChefCook(chefRoot);
            ResetChefCookAnimation();
        }

        private void PlaceDishVisual(int dishIndex, bool withFood)
        {
            if (dishIndex < 0 || dishIndex >= productPlacements.Count)
            {
                return;
            }

            var placement = productPlacements[dishIndex];
            if (slotVisuals[dishIndex] != null)
            {
                Destroy(slotVisuals[dishIndex]);
                slotVisuals[dishIndex] = null;
            }

            var dishPrefab = withFood ? TavernSecondFloorVipService.GetDishPrefab(dishIndex) : null;
            slotVisuals[dishIndex] = TavernSecondFloorVipService.CreatePlateVisualAt(placement, dishPrefab);
        }

        private void ClearAllSlotVisuals()
        {
            for (var index = 0; index < slotVisuals.Length; index++)
            {
                if (slotVisuals[index] == null)
                {
                    continue;
                }

                Destroy(slotVisuals[index]);
                slotVisuals[index] = null;
            }
        }

        private void ShowCheckoutBubble()
        {
            HideCheckoutBubble();
            var vip = TavernSecondFloorVipService.SpawnedVipRoot;
            var target = vip != null ? vip.transform : (productPlacements.Count > 0 ? productPlacements[0] : null);
            if (target == null)
            {
                tipCheckoutClickCount = TipCheckoutClickCount;
                return;
            }

            var icon = GameplayResourceStore.LoadAsset<Sprite>(CheckoutCoinIconPath);
            checkoutBubbleRoot = HudOverlayService.ShowFoodTableServeBubble(
                target,
                icon,
                HandleTipCheckoutClick,
                VipSeatedButtonOffset);
            if (checkoutBubbleRoot == null)
            {
                tipCheckoutClickCount = TipCheckoutClickCount;
                return;
            }

            var orderButton = checkoutBubbleRoot.GetComponentInChildren<TableOrderButtonUI>(true);
            orderButton?.SetDishCaption("结账");
        }

        private void HideCheckoutBubble()
        {
            if (checkoutBubbleRoot == null)
            {
                return;
            }

            HudOverlayService.ReleaseWorldHudItem(checkoutBubbleRoot);
            checkoutBubbleRoot = null;
        }

        /// <summary>点单按钮仍在时，按当前菜单刷新图标和文案。</summary>
        public static void RefreshVisibleOrderBubble()
        {
            var session = activeInstance != null
                ? activeInstance
                : FindFirstObjectByType<TavernSecondFloorVipSessionController>();
            session?.ApplyOrderBubbleMenuVisuals();
        }

        private void ShowOrderBubble()
        {
            HideOrderBubble();
            var vip = TavernSecondFloorVipService.SpawnedVipRoot;
            var target = vip != null ? vip.transform : (productPlacements.Count > 0 ? productPlacements[0] : null);
            if (target == null)
            {
                orderClicked = true;
                return;
            }

            var icon = LoadOrderBubbleIcon(IsVipMenuSelected());
            orderBubbleRoot = HudOverlayService.ShowFoodTableServeBubble(
                target,
                icon,
                () => orderClicked = true,
                VipSeatedButtonOffset);
            if (orderBubbleRoot == null)
            {
                orderClicked = true;
                return;
            }

            ApplyOrderBubbleMenuVisuals();
            ShowColaServeBubble();
        }

        private void ApplyOrderBubbleMenuVisuals()
        {
            if (orderBubbleRoot == null)
            {
                return;
            }

            var vipMenu = IsVipMenuSelected();
            var icon = LoadOrderBubbleIcon(vipMenu);
            var caption = vipMenu ? "上招牌菜" : "上大众菜";
            var followView = orderBubbleRoot.GetComponent<WorldFollowOrderButtonView>()
                             ?? orderBubbleRoot.GetComponentInChildren<WorldFollowOrderButtonView>(true);
            if (followView != null)
            {
                followView.RefreshServeVisual(icon, caption);
                followView.SetBreathingEnabled(vipMenu);
                return;
            }

            var orderButton = orderBubbleRoot.GetComponentInChildren<TableOrderButtonUI>(true);
            orderButton?.ApplyMenuOrderVisual(icon, caption);
            orderButton?.SetBreathingEnabled(vipMenu);
        }

        private static Sprite LoadOrderBubbleIcon(bool vipMenu)
        {
            var iconPath = vipMenu ? VipMenuOrderIconPath : PopularMenuOrderIconPath;
            return GameplayResourceStore.LoadAsset<Sprite>(iconPath);
        }

        private void HideOrderBubble()
        {
            HideColaServeBubble();
            if (orderBubbleRoot != null)
            {
                HudOverlayService.ReleaseWorldHudItem(orderBubbleRoot);
                orderBubbleRoot = null;
            }
        }

        private void ShowColaServeBubble()
        {
            HideColaServeBubble();
            if (colaServed || colaServing)
            {
                return;
            }

            var dataManager = DataManager.Instance;
            if (dataManager == null || dataManager.IsVisitingOtherTavern)
            {
                return;
            }

            var vip = TavernSecondFloorVipService.SpawnedVipRoot;
            var target = vip != null ? vip.transform : (productPlacements.Count > 0 ? productPlacements[0] : null);
            if (target == null)
            {
                return;
            }

            var icon = LoadColaServeIcon();
            colaServeBubbleRoot = HudOverlayService.ShowFoodTableServeBubble(
                target,
                icon,
                OnColaServeClicked,
                VipSeatedButtonOffset);
            if (colaServeBubbleRoot == null)
            {
                return;
            }

            colaServeBubbleRoot.transform.SetAsLastSibling();
            var followView = colaServeBubbleRoot.GetComponent<WorldFollowOrderButtonView>()
                             ?? colaServeBubbleRoot.GetComponentInChildren<WorldFollowOrderButtonView>(true);
            if (followView != null)
            {
                followView.SetScreenOffset(ColaServeScreenOffset);
                followView.RefreshServeVisual(icon, ColaServeCaption);
                followView.SetBreathingEnabled(false);
                return;
            }

            var orderButton = colaServeBubbleRoot.GetComponentInChildren<TableOrderButtonUI>(true);
            orderButton?.ApplyMenuOrderVisual(icon, ColaServeCaption);
        }

        private static Sprite LoadColaServeIcon()
        {
            return GameplayResourceStore.LoadAsset<Sprite>(ColaServeIconPath)
                   ?? GameplayResourceStore.LoadAsset<Sprite>(ColaServeIconFallbackPath)
                   ?? LoadOrderBubbleIcon(false);
        }

        private void HideColaServeBubble()
        {
            if (colaServeBubbleRoot == null)
            {
                return;
            }

            HudOverlayService.ReleaseWorldHudItem(colaServeBubbleRoot);
            colaServeBubbleRoot = null;
        }

        private void OnColaServeClicked()
        {
            if (colaServed || colaServing)
            {
                return;
            }

            var dataManager = DataManager.Instance;
            if (dataManager == null || !dataManager.TryConsumeCola())
            {
                HudOverlayService.ShowFloatingWarning("可乐不足");
                return;
            }

            colaServing = true;
            HideColaServeBubble();
            var clip = GameplayResourceStore.LoadAsset<VideoClip>(ColaServeVideoPath);
            if (clip == null)
            {
                Debug.LogWarning($"[SecondFloorVipSession] 缺少可乐视频：{ColaServeVideoPath}");
                FinishColaServeAfterVideo();
                return;
            }

            VideoWindowController.Show(clip, FinishColaServeAfterVideo, pauseOnLastFrame: false);
        }

        private void FinishColaServeAfterVideo()
        {
            if (colaServed)
            {
                return;
            }

            colaServing = false;
            colaServed = true;
            PersistSnapshot();
            AwardColaServeReward();
            if (isActiveAndEnabled)
            {
                StartCoroutine(ColaServeThanksRoutine());
            }
        }

        private IEnumerator ColaServeThanksRoutine()
        {
            yield return ShowVipSpeechRoutine(VipTipThanksLine, VipSpeechBubbleSeconds);
        }

        private static void AwardColaServeReward()
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null)
            {
                return;
            }

            var source = SecondFloorVipCoinCollectionPresenter.ResolveDefaultFlySource();
            SecondFloorVipCoinCollectionPresenter.PlayInstant(
                source,
                SecondFloorVipCoinCollectionPresenter.Profile.TipCheckoutClick);
            dataManager.ChangeCoinNum(DataManager.VipColaServeReward);
        }

        private void AwardFinalVipCheckout()
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null)
            {
                checkoutDoneCount = TotalDishes;
                PersistSnapshot();
                return;
            }

            var income = TbConfigRuntime.GetSecondFloorVipFinalCheckoutIncome();
            dataManager.ChangeCoinNum(income);
            dataManager.RecordVipCheckout(income);
            dataManager.AddPrestigeForCompletedTable(hasVipCustomer: true);
            dataManager.RecordTakeMoneyCheckout();

            checkoutDoneCount = TotalDishes;
            PersistSnapshot();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        private IEnumerator MoveStaffRoutine(
            NavMeshAgent agent,
            GameObject root,
            Animator animator,
            bool hasSpeed,
            Vector3 destination)
        {
            if (agent == null || root == null)
            {
                yield break;
            }

            if (!TavernSecondFloorVipService.TryEnableAgentOnNavMesh(agent, root.transform.position))
            {
                root.transform.position = destination;
                yield break;
            }

            agent.isStopped = false;
            agent.SetDestination(destination);
            var timeout = Mathf.Max(1f, staffMoveTimeoutSeconds);
            while (timeout > 0f)
            {
                timeout -= Time.deltaTime;
                if (hasSpeed && animator != null && agent.isOnNavMesh)
                {
                    animator.SetFloat("Speed", agent.velocity.magnitude);
                }

                if (!agent.pathPending
                    && agent.hasPath
                    && agent.remainingDistance <= Mathf.Max(0.05f, staffMoveArriveDistance))
                {
                    break;
                }

                // 无路径时也允许超时结束，避免永久卡住。
                if (!agent.pathPending && !agent.hasPath)
                {
                    agent.SetDestination(destination);
                }

                yield return null;
            }

            SetStaffSpeed(animator, hasSpeed, 0f);
            agent.isStopped = true;
        }

        private static void WarpToPoint(
            NavMeshAgent agent,
            Transform root,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            if (root == null)
            {
                return;
            }

            root.SetPositionAndRotation(worldPosition, worldRotation);
            if (agent != null)
            {
                TavernSecondFloorVipService.TryEnableAgentOnNavMesh(agent, worldPosition);
                agent.isStopped = true;
                if (agent.isOnNavMesh)
                {
                    agent.ResetPath();
                }
            }
        }

        private static NavMeshAgent EnsureAgent(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            var agent = root.GetComponent<NavMeshAgent>() ?? root.GetComponentInChildren<NavMeshAgent>(true);
            if (agent != null)
            {
                return agent;
            }

            agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.15f;
            agent.speed = 1.1f;
            agent.acceleration = 4f;
            agent.angularSpeed = 720f;
            agent.height = 1.6f;
            return agent;
        }

        private static void FaceToward(Transform self, Vector3 worldTarget)
        {
            if (self == null)
            {
                return;
            }

            var delta = worldTarget - self.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.0001f)
            {
                return;
            }

            self.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        }

        private void PlayChefCookAnimation()
        {
            if (chefAnimator == null || !chefAnimator.isActiveAndEnabled)
            {
                return;
            }

            SetStaffSpeed(chefAnimator, HasFloatParam(chefAnimator, "Speed"), 0f);
            if (IsChefInCookState(chefAnimator))
            {
                return;
            }

            if (HasChefCookState(chefAnimator))
            {
                if (chefAnimator.HasState(0, Animator.StringToHash(ChefBaseLayerCookState)))
                {
                    chefAnimator.CrossFadeInFixedTime(ChefBaseLayerCookState, 0.08f, 0, 0f);
                }
                else
                {
                    chefAnimator.CrossFadeInFixedTime(ChefCookState, 0.08f, 0, 0f);
                }

                return;
            }

            if (HasTriggerParam(chefAnimator, ChefCookTrigger))
            {
                chefAnimator.ResetTrigger(ChefCookTrigger);
                chefAnimator.SetTrigger(ChefCookTrigger);
            }
        }

        private void ResetChefCookAnimation()
        {
            if (chefAnimator == null || !chefAnimator.isActiveAndEnabled)
            {
                return;
            }

            SetStaffSpeed(chefAnimator, HasFloatParam(chefAnimator, "Speed"), 0f);
            if (HasTriggerParam(chefAnimator, ChefCookTrigger))
            {
                chefAnimator.ResetTrigger(ChefCookTrigger);
            }

            // 回到站立待机：优先 Idle，否则清速度即可。
            if (chefAnimator.HasState(0, Animator.StringToHash("Idle")))
            {
                chefAnimator.CrossFadeInFixedTime("Idle", 0.1f, 0, 0f);
            }
            else if (chefAnimator.HasState(0, Animator.StringToHash("Base Layer.Idle")))
            {
                chefAnimator.CrossFadeInFixedTime("Base Layer.Idle", 0.1f, 0, 0f);
            }
        }

        private static bool IsChefInCookState(Animator animator)
        {
            var currentState = animator.GetCurrentAnimatorStateInfo(0);
            return currentState.IsName(ChefBaseLayerCookState) || currentState.IsName(ChefCookState);
        }

        private static bool HasChefCookState(Animator animator)
        {
            return animator.HasState(0, Animator.StringToHash(ChefBaseLayerCookState))
                   || animator.HasState(0, Animator.StringToHash(ChefCookState));
        }

        private static bool HasTriggerParam(Animator animator, string triggerName)
        {
            if (animator == null || string.IsNullOrEmpty(triggerName))
            {
                return false;
            }

            foreach (var parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == triggerName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetStaffSpeed(Animator animator, bool hasSpeed, float speed)
        {
            if (animator != null && hasSpeed)
            {
                animator.SetFloat("Speed", speed);
            }
        }

        private static bool HasFloatParam(Animator animator, string paramName)
        {
            if (animator == null)
            {
                return false;
            }

            foreach (var parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Float && parameter.name == paramName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
