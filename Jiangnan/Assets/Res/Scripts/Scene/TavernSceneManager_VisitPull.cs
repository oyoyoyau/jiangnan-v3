using System.Collections;
using System.Collections.Generic;
using JN.Client;
using JN.Client.Manager;
using JN.Client.Model;
using JN.Client.UI;
using QFramework;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.Scene
{
    /// <summary>
    /// 拜访他人酒楼：刷客/拉客/饺子节点/回店进客。
    /// </summary>
    public partial class TavernSceneManager
    {
        private const string VisitJiaoziNodeName = "jiaozi";
        private const string JiaoziBearerNamePrefix = "jiaofu";
        private const string JiaoziEndPointName = "JiaoziEndPoint";
        private const string JiaoziCustomerPointName = "JiaoziCustomerPoint";
        private const string PeopleStartPointName = "PeopleStartPoint";
        /// <summary>回店卸客：轿子从 PeopleStartPoint 走到 JiaoziEndPoint 的时长。</summary>
        private const float HomeUnloadArriveSeconds = 0.5f;
        /// <summary>拜访满载：轿子从 JiaoziEndPoint 走到 PeopleStartPoint 的时长。</summary>
        private const float VisitJiaoziDepartSeconds = 0.5f;
        /// <summary>回店卸客：每位客人生成间隔。</summary>
        private const float HomeUnloadSpawnIntervalSeconds = 0.3f;
        /// <summary>拉客/卸客时轿子世界 Y。</summary>
        private const float JiaoziServiceWorldY = 0.85f;
        private const float JiaoziServiceScale = 1.5f;
        /// <summary>自家拉客冷却中：轿子本地 Y。</summary>
        private const float HomeJiaoziCooldownLocalY = 0.5f;
        /// <summary>拜访拉客兜底门口坐标（找不到 JiaoziEndPoint 时用）。</summary>
        private static readonly Vector3 VisitJiaoziWorldPosition = new(-0.9f, JiaoziServiceWorldY, -0.24f);
        private static readonly Vector3 DrumUpWorldOffset = new(0f, 1.15f, 0f);
        /// <summary>DrumUpBtn 不能拉客时图标色（#646464，alpha=1）。</summary>
        private static readonly Color DrumUpBtnInsufficientIconColor = new(0x64 / 255f, 0x64 / 255f, 0x64 / 255f, 1f);

        private enum HomeUnloadPhase
        {
            None = 0,
            Arriving = 1,
            Unloading = 2
        }

        private enum VisitJiaoziPhase
        {
            /// <summary>未展示或已离场隐藏。</summary>
            Hidden = 0,
            /// <summary>停在 JiaoziEndPoint 接客。</summary>
            Stationed = 1,
            /// <summary>满载驶向 PeopleStartPoint。</summary>
            Departing = 2
        }

        private bool visitSimulationActive;
        private float visitSpawnRemaining = -1f;
        private Transform visitJiaoziRoot;
        private bool visitJiaoziHomePoseCached;
        private Vector3 visitJiaoziHomeLocalPosition;
        private Vector3 visitJiaoziHomeLocalScale;
        /// <summary>拜访/移动轿子时锁定的世界 Y（取场景默认高度，不跟点位起伏）。</summary>
        private float visitJiaoziLockedWorldY;
        private bool visitJiaoziLockedWorldYCached;
        /// <summary>自家回店卸客时序（进场 → 卸客 → 归位）。</summary>
        private HomeUnloadPhase homeUnloadPhase = HomeUnloadPhase.None;
        private float homeUnloadArriveElapsed;
        private Vector3 homeUnloadArriveStartWorld;
        private Vector3 homeUnloadArriveEndWorld;
        /// <summary>卸客进场全程锁定的世界 Y，避免跟点位高低起伏。</summary>
        private float homeUnloadLockedWorldY;
        private readonly List<int> homeUnloadSpawnKinds = new();
        private int homeUnloadSpawnIndex;
        private float homeUnloadSpawnTimer;
        private bool homeUnloadBearersWalking;
        /// <summary>拜访拉客轿子：停靠接客 / 满载离场。</summary>
        private VisitJiaoziPhase visitJiaoziPhase = VisitJiaoziPhase.Hidden;
        private float visitJiaoziDepartElapsed;
        private Vector3 visitJiaoziDepartStartWorld;
        private Vector3 visitJiaoziDepartEndWorld;
        private float visitJiaoziDepartLockedWorldY;
        private bool visitJiaoziBearersWalking;
        /// <summary>满载后等待在途拉客客人全部消失，再启动驶离。</summary>
        private bool visitJiaoziDepartPending;
        /// <summary>缓存自家「可拉客」状态，冷却变化时再刷轿夫。</summary>
        private bool cachedHomePullReadyForBearer;
        private bool hasCachedHomePullReadyForBearer;
        private readonly Dictionary<TavernCustomerRuntimeController, GameObject> visitDrumUpButtons = new();
        private readonly HashSet<TavernCustomerRuntimeController> visitPullingCustomers = new();

        private bool IsVisitSimulationRunning =>
            DataManager.Instance != null
            && DataManager.Instance.IsVisitingOtherTavern
            && visitSimulationActive;

        /// <summary>
        /// 场景启动后：若正在拜访他人酒楼，开启拜访模拟。
        /// </summary>
        private void TryBeginVisitPullSimulation()
        {
            if (DataManager.Instance == null || !DataManager.Instance.IsVisitingOtherTavern)
            {
                visitSimulationActive = false;
                RefreshVisitJiaoziVisibility();
                return;
            }

            visitSimulationActive = true;
            customerSpawnLoopActive = true;
            if (!TryRestoreOtherTavernVisitSnapshot())
            {
                SeedVisitInitialSeatedCustomers();
                // 拜访开场：每桌独立概率挂「被拉客」提示（触发桌清空客人，不含贵客桌）。
                TryRollVisitTavernPulledTips();
            }

            EnsureVisitHotTavernHasSeatedVip();

            // 满座后再按固定间隔进客（不立刻再刷一波）。
            visitSpawnRemaining = GetEffectiveCustomerSpawnInterval();
            // 拜访开场：容量未满则轿子停在 JiaoziEndPoint。
            SyncVisitJiaoziPhaseOnEnter();
            RefreshVisitJiaoziVisibility();
            RefreshVisitDrumUpButtons();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        private void EndVisitPullSimulation()
        {
            visitSimulationActive = false;
            CancelVisitJiaoziDepart();
            ClearAllVisitDrumUpButtons();
            RefreshVisitJiaoziVisibility();
        }

        /// <summary>
        /// 拜访模式逐帧：满座等菜后的固定间隔进客、自动结账、拉客 HUD、轿子显隐。
        /// </summary>
        private void TickVisitPullSimulation(float deltaTime)
        {
            if (!IsVisitSimulationRunning)
            {
                return;
            }

            if (!hasNavMesh)
            {
                hasNavMesh = TryGetNavMeshPosition(
                    customerEntryPoint != null ? customerEntryPoint.position : Vector3.zero,
                    out _);
            }

            if (visitSpawnRemaining < 0f)
            {
                visitSpawnRemaining = GetEffectiveCustomerSpawnInterval();
            }

            visitSpawnRemaining -= deltaTime;
            if (visitSpawnRemaining <= 0f)
            {
                // 爆满店无贵客时先补刷；其余按对方等级概率刷贵客/稀客。
                SpawnCustomerIfPossible(allowVipSpawn: true);
                visitSpawnRemaining = GetEffectiveCustomerSpawnInterval();
            }

            TickVisitAutoCheckout();
            RefreshVisitDrumUpButtons();
            TryStartVisitJiaoziDepartIfReady();
            TickVisitJiaoziDepart(deltaTime);
            RefreshVisitJiaoziVisibility();
        }

        /// <summary>
        /// 自家酒楼：回店卸客时序（轿子进场 → 间隔卸客 → 归位）；冷却变化时同步轿夫。
        /// </summary>
        private void TickPendingPulledCustomerEnter(float deltaTime)
        {
            if (DataManager.Instance == null || DataManager.Instance.IsVisitingOtherTavern)
            {
                CancelHomeUnloadSequence();
                hasCachedHomePullReadyForBearer = false;
                return;
            }

            TickHomeUnloadSequence(deltaTime);
            TickHomeJiaoziBearerByCooldown();
        }

        /// <summary>
        /// 自家常驻轿子：冷却结束/开始时刷新轿夫显隐。
        /// </summary>
        private void TickHomeJiaoziBearerByCooldown()
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null || !dataManager.IsJiaoziUnlocked())
            {
                return;
            }

            // 卸客时序进行中由卸客逻辑接管轿夫，避免冷却刷显隐打断行走动画。
            if (homeUnloadPhase != HomeUnloadPhase.None)
            {
                return;
            }

            var ready = dataManager.IsPullCustomerCooldownReady();
            if (hasCachedHomePullReadyForBearer && ready == cachedHomePullReadyForBearer)
            {
                return;
            }

            cachedHomePullReadyForBearer = ready;
            hasCachedHomePullReadyForBearer = true;
            RefreshVisitJiaoziVisibility();
        }

        /// <summary>
        /// 进自家店时若有待卸客：轿子从 PeopleStartPoint 进场到 JiaoziEndPoint，再按间隔卸客，最后归位。
        /// </summary>
        private void ScheduleHomeUnloadFinalizeIfNeeded()
        {
            if (DataManager.Instance == null || DataManager.Instance.IsVisitingOtherTavern)
            {
                CancelHomeUnloadSequence();
                return;
            }

            if (DataManager.Instance.GetPendingPulledCustomerCount() <= 0)
            {
                CancelHomeUnloadSequence();
                RestoreVisitJiaoziToHomePose();
                RefreshVisitJiaoziVisibility();
                RefreshMyDrumUpButton();
                return;
            }

            if (homeUnloadPhase != HomeUnloadPhase.None)
            {
                return;
            }

            BeginHomeUnloadSequence();
        }

        /// <summary>
        /// 开始回店卸客表现：轿子出现在入口，0.5 秒走到终点后卸客。
        /// </summary>
        private void BeginHomeUnloadSequence()
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null)
            {
                return;
            }

            GameAudioManager.PlayPeakTimeWaiterShout();
            EnsureVisitJiaoziRoot();
            CacheVisitJiaoziHomePoseIfNeeded();
            if (visitJiaoziRoot == null)
            {
                // 无轿子节点时仍立刻刷客，避免丢客。
                FlushPendingPulledCustomersKeepCapacity();
                FinalizeHomeJiaoziUnload();
                return;
            }

            homeUnloadSpawnKinds.Clear();
            homeUnloadSpawnKinds.AddRange(dataManager.GetPendingPulledCustomerKindsCopy());
            homeUnloadSpawnIndex = 0;
            homeUnloadSpawnTimer = 0f;
            homeUnloadArriveElapsed = 0f;

            // 卸客全程保持轿子当前 Y，只在 XZ 上从入口走到终点。
            homeUnloadLockedWorldY = JiaoziServiceWorldY;

            var startPoint = customerEntryPoint != null
                ? customerEntryPoint
                : FindSceneTransformByName(PeopleStartPointName);
            var endPoint = FindSceneTransformByName(JiaoziEndPointName);
            var startWorld = startPoint != null
                ? startPoint.position
                : VisitJiaoziWorldPosition;
            var endWorld = endPoint != null
                ? endPoint.position
                : VisitJiaoziWorldPosition;
            homeUnloadArriveStartWorld = WithLockedJiaoziY(startWorld);
            homeUnloadArriveEndWorld = WithLockedJiaoziY(endWorld);

            visitJiaoziRoot.gameObject.SetActive(true);
            FacilityBuildVisualUtility.ApplyBuiltState(visitJiaoziRoot.gameObject, includeChildren: false);
            ApplyJiaoziServiceVisual(true);
            visitJiaoziRoot.position = homeUnloadArriveStartWorld;
            FaceJiaoziToward(homeUnloadArriveEndWorld);
            homeUnloadBearersWalking = true;
            SetJiaoziBearerVisible(visitJiaoziRoot, true, playWalk: true);

            homeUnloadPhase = HomeUnloadPhase.Arriving;
            // 卸客期间隐藏场景拉客按钮。
            RefreshMyDrumUpButton();
            RefreshVisitJiaoziVisibility();
        }

        private void TickHomeUnloadSequence(float deltaTime)
        {
            if (homeUnloadPhase == HomeUnloadPhase.None)
            {
                return;
            }

            if (homeUnloadPhase == HomeUnloadPhase.Arriving)
            {
                TickHomeUnloadArriving(deltaTime);
                return;
            }

            if (homeUnloadPhase == HomeUnloadPhase.Unloading)
            {
                TickHomeUnloadSpawning(deltaTime);
            }
        }

        /// <summary>
        /// 轿子从入口插值移动到终点；到位后停行走动画并开始卸客。
        /// </summary>
        private void TickHomeUnloadArriving(float deltaTime)
        {
            EnsureVisitJiaoziRoot();
            if (visitJiaoziRoot == null)
            {
                homeUnloadPhase = HomeUnloadPhase.Unloading;
                homeUnloadSpawnTimer = 0f;
                return;
            }

            homeUnloadArriveElapsed += Mathf.Max(0f, deltaTime);
            var duration = Mathf.Max(0.01f, HomeUnloadArriveSeconds);
            var t = Mathf.Clamp01(homeUnloadArriveElapsed / duration);
            visitJiaoziRoot.position = Vector3.Lerp(homeUnloadArriveStartWorld, homeUnloadArriveEndWorld, t);
            FaceJiaoziToward(homeUnloadArriveEndWorld);

            if (t < 1f)
            {
                return;
            }

            visitJiaoziRoot.position = homeUnloadArriveEndWorld;
            homeUnloadBearersWalking = false;
            SetJiaoziBearerVisible(visitJiaoziRoot, true, playWalk: false);
            homeUnloadPhase = HomeUnloadPhase.Unloading;
            homeUnloadSpawnTimer = 0f;
            RefreshVisitJiaoziVisibility();
        }

        /// <summary>
        /// 按 0.3 秒间隔依次卸客；不因排队/容量失败阻塞，到点就卸下一位，卸完即归位。
        /// </summary>
        private void TickHomeUnloadSpawning(float deltaTime)
        {
            if (homeUnloadSpawnIndex >= homeUnloadSpawnKinds.Count)
            {
                FinalizeHomeJiaoziUnload();
                return;
            }

            homeUnloadSpawnTimer -= deltaTime;
            if (homeUnloadSpawnTimer > 0f)
            {
                return;
            }

            var kind = homeUnloadSpawnKinds[homeUnloadSpawnIndex];
            // 尝试刷入排队；失败也继续推进，避免等队列空位卡住轿子。
            TrySpawnPulledCustomerOfKind(kind, forceDuringFinalize: true);
            homeUnloadSpawnIndex++;
            homeUnloadSpawnTimer = HomeUnloadSpawnIntervalSeconds;

            if (homeUnloadSpawnIndex >= homeUnloadSpawnKinds.Count)
            {
                FinalizeHomeJiaoziUnload();
            }
        }

        private void CancelHomeUnloadSequence()
        {
            homeUnloadPhase = HomeUnloadPhase.None;
            homeUnloadArriveElapsed = 0f;
            homeUnloadSpawnIndex = 0;
            homeUnloadSpawnTimer = 0f;
            homeUnloadBearersWalking = false;
            homeUnloadSpawnKinds.Clear();
        }

        /// <summary>
        /// 卸客收尾：容量清零 + 轿子回默认位置，并开启拉客真实时间冷却。
        /// </summary>
        private void FinalizeHomeJiaoziUnload()
        {
            CancelHomeUnloadSequence();
            DataManager.Instance?.ClearAllPendingPulledCustomers();
            DataManager.Instance?.StartPullCustomerCooldown();
            DataManager.ClearAllOtherTavernVisitSnapshots();
            RestoreVisitJiaoziToHomePose();
            RefreshVisitJiaoziVisibility();
            // 卸客结束：重新显示拉客按钮（冷却置灰由 MyDrumUpBtnView 刷新）。
            RefreshMyDrumUpButton();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        /// <summary>
        /// 轿子水平朝向目标点（仅绕 Y）。
        /// </summary>
        private void FaceJiaoziToward(Vector3 worldTarget)
        {
            if (visitJiaoziRoot == null)
            {
                return;
            }

            var direction = worldTarget - visitJiaoziRoot.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            visitJiaoziRoot.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        /// <summary>
        /// 卸客移动时沿用锁定世界 Y，只改 XZ。
        /// </summary>
        private Vector3 WithLockedJiaoziY(Vector3 worldPosition)
        {
            return new Vector3(worldPosition.x, homeUnloadLockedWorldY, worldPosition.z);
        }

        /// <summary>
        /// 拉客/卸客：scale=1.5、世界 Y=0.85；
        /// 自家常驻：可拉客用场景默认位，冷却中本地 Y=0.5。
        /// </summary>
        private void ApplyJiaoziServiceVisual(bool servicePose)
        {
            EnsureVisitJiaoziRoot();
            if (visitJiaoziRoot == null)
            {
                return;
            }

            if (servicePose)
            {
                visitJiaoziRoot.localScale = Vector3.one * JiaoziServiceScale;
                var world = visitJiaoziRoot.position;
                world.y = JiaoziServiceWorldY;
                visitJiaoziRoot.position = world;
                return;
            }

            CacheVisitJiaoziHomePoseIfNeeded();
            if (!visitJiaoziHomePoseCached)
            {
                return;
            }

            visitJiaoziRoot.localScale = visitJiaoziHomeLocalScale;
            var local = visitJiaoziHomeLocalPosition;
            if (ShouldLowerHomeJiaoziForPullCooldown())
            {
                local.y = HomeJiaoziCooldownLocalY;
            }

            visitJiaoziRoot.localPosition = local;
        }

        /// <summary>自家已解锁轿子且拉客冷却中（非卸客时序）时压低 Y。</summary>
        private bool ShouldLowerHomeJiaoziForPullCooldown()
        {
            var dataManager = DataManager.Instance;
            return dataManager != null
                   && !dataManager.IsVisitingOtherTavern
                   && dataManager.IsJiaoziUnlocked()
                   && homeUnloadPhase == HomeUnloadPhase.None
                   && !dataManager.IsPullCustomerCooldownReady();
        }

        private void EnsureVisitJiaoziRoot()
        {
            visitJiaoziRoot ??= FindSceneTransformByName(VisitJiaoziNodeName);
        }

        /// <summary>
        /// 强制轿子回到场景默认本地坐标并隐藏轿夫。
        /// </summary>
        private void RestoreVisitJiaoziToHomePose()
        {
            EnsureVisitJiaoziRoot();
            CacheVisitJiaoziHomePoseIfNeeded();
            if (visitJiaoziRoot == null)
            {
                return;
            }

            if (visitJiaoziHomePoseCached)
            {
                visitJiaoziRoot.localPosition = visitJiaoziHomeLocalPosition;
            }

            visitJiaoziRoot.localEulerAngles = Vector3.zero;
            ApplyJiaoziServiceVisual(false);
            homeUnloadBearersWalking = false;
            SetJiaoziBearerVisible(visitJiaoziRoot, false);
        }

        /// <summary>
        /// 按待卸类型立刻刷客进队，但不消费队列（容量仍占用，直到归位收尾）。
        /// </summary>
        private void FlushPendingPulledCustomersKeepCapacity()
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null)
            {
                return;
            }

            var kinds = dataManager.GetPendingPulledCustomerKindsCopy();
            for (var index = 0; index < kinds.Count; index++)
            {
                if (!TrySpawnPulledCustomerOfKind(kinds[index], forceDuringFinalize: true))
                {
                    // 队满等失败：后续种类仍尽量尝试，避免一人卡住整队。
                    continue;
                }
            }
        }

        /// <summary>
        /// 按拉客时记录的类型消费队列并刷入排队（兼容旧路径）。
        /// </summary>
        private void FlushPendingPulledCustomers(bool forceDuringFinalize = false)
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null)
            {
                return;
            }

            var guard = Mathf.Max(1, dataManager.GetPendingPulledCustomerCount() + 2);
            while (dataManager.GetPendingPulledCustomerCount() > 0 && guard-- > 0)
            {
                if (!TrySpawnOnePendingPulledCustomer(forceDuringFinalize))
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 卸客：消费队首并生成对应客人。
        /// </summary>
        private bool TrySpawnOnePendingPulledCustomer(bool forceDuringFinalize = false)
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null || dataManager.GetPendingPulledCustomerCount() <= 0)
            {
                return false;
            }

            if (!dataManager.TryConsumePendingPulledCustomer(out var kind))
            {
                return false;
            }

            if (TrySpawnPulledCustomerOfKind(kind, forceDuringFinalize))
            {
                return true;
            }

            dataManager.RequeuePulledCustomerAtFront(kind);
            return false;
        }

        /// <summary>
        /// 按类型在门口生成客人并进队（不改待卸容量队列）。
        /// </summary>
        private bool TrySpawnPulledCustomerOfKind(int kind, bool forceDuringFinalize = false)
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null)
            {
                return false;
            }

            if (!forceDuringFinalize && !IsBusinessActive)
            {
                return false;
            }

            if (forceDuringFinalize
                && (dataManager.TavernData == null || !dataManager.TavernData.isOpen || isClosingBusiness))
            {
                return false;
            }

            // 卸客强制刷入：不受排队上限/场上人数限制，保证拉客数量与卸客数量一致。
            if (!forceDuringFinalize)
            {
                if (activeCustomers.Count >= GetDynamicMaxActiveCustomers())
                {
                    return false;
                }

                if (dataManager.GetUnlockedTableCount() == 0
                    || queuedCustomers.Count >= GetEffectiveMaxQueueSize())
                {
                    return false;
                }
            }
            else if (dataManager.GetUnlockedTableCount() == 0)
            {
                return false;
            }

            if (!TryGetUnloadEnterSpawnPosition(out var spawnPosition))
            {
                return false;
            }

            var asVip = kind == DataManager.PulledCustomerKindVip;
            var asRare = kind == DataManager.PulledCustomerKindRare;
            var customer = SpawnCustomerRuntime(spawnPosition, asVip: asVip, asRare: asRare);
            if (customer == null && (asVip || asRare))
            {
                customer = SpawnCustomerRuntime(spawnPosition, asVip: false, asRare: false);
                if (customer != null)
                {
                    if (asVip)
                    {
                        customer.MarkAsVip();
                    }
                    else
                    {
                        customer.MarkAsRare();
                    }
                }
            }

            if (customer == null)
            {
                return false;
            }

            // 卸客贵客与常规刷贵客一致：先挂大堂/包厢气泡，确认后再入队或上二楼。
            if (customer.IsVip)
            {
                customer.SetAwaitingVipFloorChoice(true);
                customer.MoveToQueue(GetQueueTarget(Mathf.Max(0, queuedCustomers.Count)));
                ShowVipGuestActionBubble(customer);
            }
            else
            {
                EnqueueSpawnedCustomer(customer);
            }

            PlayGuideBuildingSuccessEffect(spawnPosition, playAudio: false);
            // 拉客任务 Solicit：回店卸客进队成功计 1 次。
            dataManager.RecordSolicitSuccess();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
            return true;
        }

        /// <summary>
        /// 卸客出生点：优先 JiaoziCustomerPoint，再回退 EnterStartPoint / Door / 常规刷客点。
        /// </summary>
        private bool TryGetUnloadEnterSpawnPosition(out Vector3 spawnPosition)
        {
            var spawnRoot = FindSceneTransformByName(JiaoziCustomerPointName)
                            ?? customerSpawnPoint
                            ?? FindSceneTransformByName("EnterStartPoint")
                            ?? FindSceneTransformByName("Door")
                            ?? customerEntryPoint;

            if (spawnRoot == null)
            {
                spawnPosition = Vector3.zero;
                return false;
            }

            // 贴卸客点中心，轻微侧向错开避免多人叠模。
            var right = spawnRoot.right.sqrMagnitude > 0.1f ? spawnRoot.right.normalized : Vector3.right;
            var pendingIndex = Mathf.Max(0, homeUnloadSpawnIndex);
            var laneIndex = pendingIndex % 3 - 1;
            var candidate = spawnRoot.position + right * (laneIndex * 0.35f);
            if (TryGetNavMeshPosition(candidate, out spawnPosition))
            {
                return true;
            }

            return TryGetNavMeshPosition(spawnRoot.position, out spawnPosition);
        }

        /// <summary>
        /// 退出他人酒楼拜访：Halt 后按 VisitingTileId 采集每桌客人与被拉客提示。
        /// </summary>
        public void CaptureOtherTavernVisitSnapshot()
        {
            if (!DataManager.IsInOtherTavernVisitSession || DataManager.Instance == null)
            {
                return;
            }

            var tileId = DataManager.Instance.VisitingTileId;
            if (tileId <= 0)
            {
                return;
            }

            var snapshot = new OtherTavernVisitSnapshot
            {
                tileId = tileId,
                tables = new List<OtherTavernVisitTableSnapshot>()
            };

            var tableIds = new List<int>(AllTables.Keys);
            tableIds.Sort();
            for (var index = 0; index < tableIds.Count; index++)
            {
                var tableId = tableIds[index];
                var tableData = DataManager.Instance.GetTableData(tableId);
                if (tableData == null || !tableData.isUnlocked)
                {
                    continue;
                }

                var tableSnap = new OtherTavernVisitTableSnapshot
                {
                    tableId = tableId,
                    runtimeState = tableData.runtimeState,
                    guestKinds = CaptureVisitTableGuestKinds(tableId),
                    hasPulledTip = tablesWithPulledTip.Contains(tableId)
                };

                if (tableSnap.hasPulledTip
                    && TryGetPulledTipMeta(tableId, out var isSelf, out var pullerName, out var headIconId))
                {
                    tableSnap.pulledTipIsSelf = isSelf;
                    tableSnap.pullerName = pullerName;
                    tableSnap.headIconId = headIconId;
                }

                snapshot.tables.Add(tableSnap);
            }

            DataManager.Instance.SaveOtherTavernVisitSnapshot(snapshot);
        }

        private List<int> CaptureVisitTableGuestKinds(int tableId)
        {
            var kinds = new List<int>();
            if (!TryGetTableCustomerGroup(tableId, out var customers) || customers == null || customers.Count <= 0)
            {
                return kinds;
            }

            var ordered = new List<TavernCustomerRuntimeController>(customers.Count);
            for (var index = 0; index < customers.Count; index++)
            {
                if (customers[index] != null)
                {
                    ordered.Add(customers[index]);
                }
            }

            ordered.Sort((left, right) => left.SeatIndex.CompareTo(right.SeatIndex));
            for (var index = 0; index < ordered.Count; index++)
            {
                kinds.Add(ResolveVisitGuestKind(ordered[index]));
            }

            return kinds;
        }

        private static int ResolveVisitGuestKind(TavernCustomerRuntimeController customer)
        {
            if (customer == null)
            {
                return DataManager.PulledCustomerKindNormal;
            }

            if (customer.IsVip)
            {
                return DataManager.PulledCustomerKindVip;
            }

            if (customer.IsRare)
            {
                return DataManager.PulledCustomerKindRare;
            }

            return DataManager.PulledCustomerKindNormal;
        }

        /// <summary>
        /// 再进同一他人店：按行程快照还原入座与被拉客提示，跳过播种/掷骰。
        /// </summary>
        private bool TryRestoreOtherTavernVisitSnapshot()
        {
            if (DataManager.Instance == null)
            {
                return false;
            }

            var tileId = DataManager.Instance.VisitingTileId;
            if (!DataManager.Instance.TryGetOtherTavernVisitSnapshot(tileId, out var snapshot)
                || snapshot == null
                || snapshot.tables == null)
            {
                return false;
            }

            RestoreOtherTavernVisitSnapshot(snapshot);
            return true;
        }

        private void RestoreOtherTavernVisitSnapshot(OtherTavernVisitSnapshot snapshot)
        {
            for (var index = 0; index < snapshot.tables.Count; index++)
            {
                RestoreOtherTavernVisitTableSnapshot(snapshot.tables[index]);
            }
        }

        private void RestoreOtherTavernVisitTableSnapshot(OtherTavernVisitTableSnapshot tableSnap)
        {
            if (tableSnap == null || tableSnap.tableId <= 0)
            {
                return;
            }

            if (!AllTables.TryGetValue(tableSnap.tableId, out var table) || table == null)
            {
                return;
            }

            var tableData = DataManager.Instance != null
                ? DataManager.Instance.GetTableData(tableSnap.tableId)
                : null;
            if (tableData == null || !tableData.isUnlocked)
            {
                return;
            }

            var seated = SpawnVisitSnapshotSeatedCustomers(tableSnap.tableId, table, tableSnap.guestKinds);
            if (seated.Count > 0)
            {
                customerFlowService.RegisterTableGroup(
                    tableCustomers,
                    tableCustomerGroups,
                    tableSnap.tableId,
                    seated);
                ApplyVisitSnapshotOccupiedState(tableSnap.tableId, table);
            }
            else
            {
                tableStateService.SetIdle(tableSnap.tableId, table, clearDishVisual: true, dispatchRuntimeChanged: false);
            }

            if (!tableSnap.hasPulledTip)
            {
                return;
            }

            // 贵客桌不还原「被拉走」标记。
            if (TableHasVipCustomer(tableSnap.tableId))
            {
                return;
            }

            ShowPulledTipOnTableFromSnapshot(
                tableSnap.tableId,
                table,
                tableSnap.pulledTipIsSelf,
                tableSnap.pullerName,
                tableSnap.headIconId);
        }

        /// <summary>
        /// 拜访快照有客：写回占用态。厨工/前台不进快照，统一落到待上菜。
        /// </summary>
        private void ApplyVisitSnapshotOccupiedState(int tableId, TableArea table)
        {
            tableStateService.SetWaitingServe(tableId, table, "待上菜", dispatchRuntimeChanged: false);
        }

        private List<TavernCustomerRuntimeController> SpawnVisitSnapshotSeatedCustomers(
            int tableId,
            TableArea table,
            List<int> guestKinds)
        {
            var result = new List<TavernCustomerRuntimeController>();
            if (table == null || guestKinds == null || guestKinds.Count <= 0 || customerTemplates.Count == 0)
            {
                return result;
            }

            var seatCap = Mathf.Max(0, table.GetSeatCapacity());
            var seatedCount = seatCap > 0 ? Mathf.Min(guestKinds.Count, seatCap) : guestKinds.Count;
            for (var seatIndex = 0; seatIndex < seatedCount; seatIndex++)
            {
                var spawnPosition = table.TryGetSeatPoseByIndex(seatIndex, out var seatPosition, out _)
                    ? seatPosition
                    : table.GetCustomerTargetPosition();
                var customer = SpawnVisitSnapshotCustomer(spawnPosition, guestKinds[seatIndex]);
                if (customer == null)
                {
                    continue;
                }

                customer.InstantSeatAtTable(tableId, seatIndex);
                result.Add(customer);
            }

            return result;
        }

        private TavernCustomerRuntimeController SpawnVisitSnapshotCustomer(Vector3 spawnPosition, int kind)
        {
            var asVip = kind == DataManager.PulledCustomerKindVip;
            var asRare = kind == DataManager.PulledCustomerKindRare;
            var customer = SpawnCustomerRuntime(spawnPosition, asVip: asVip, asRare: asRare);
            if (customer != null || (!asVip && !asRare))
            {
                return customer;
            }

            customer = SpawnCustomerRuntime(spawnPosition, asVip: false, asRare: false);
            if (customer == null)
            {
                return null;
            }

            if (asVip)
            {
                customer.MarkAsVip();
            }
            else
            {
                customer.MarkAsRare();
            }

            return customer;
        }

        /// <summary>
        /// 拜访开场：所有已解锁桌位满座，并直接处于「等上菜」。
        /// 爆满店必定 1 贵客，其余座位按对方等级稀客概率刷稀客/普客；普通店全为普客。
        /// 不依赖存档 runtimeState（ApplyUnlockedTablesOnly 已强制 Idle），拜访中改桌态不落盘。
        /// </summary>
        private void SeedVisitInitialSeatedCustomers()
        {
            if (DataManager.Instance == null || customerTemplates.Count == 0)
            {
                return;
            }

            var slots = new List<(int TableId, int SeatIndex, TableArea Table)>();
            var tableIds = new List<int>(AllTables.Keys);
            tableIds.Sort();
            for (var index = 0; index < tableIds.Count; index++)
            {
                var tableId = tableIds[index];
                if (!AllTables.TryGetValue(tableId, out var table) || table == null)
                {
                    continue;
                }

                var tableData = DataManager.Instance.GetTableData(tableId);
                if (tableData == null || !tableData.isUnlocked)
                {
                    continue;
                }

                if ((TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.Idle)
                {
                    continue;
                }

                var seatCount = Mathf.Max(0, table.GetSeatCapacity());
                for (var seatIndex = 0; seatIndex < seatCount; seatIndex++)
                {
                    slots.Add((tableId, seatIndex, table));
                }
            }

            if (slots.Count <= 0)
            {
                return;
            }

            var roleFlags = ResolveVisitSeedCustomerRoles(slots.Count);
            var seatedByTable = new Dictionary<int, List<TavernCustomerRuntimeController>>();

            for (var index = 0; index < slots.Count; index++)
            {
                var slot = slots[index];
                var spawnPosition = slot.Table.TryGetSeatPoseByIndex(slot.SeatIndex, out var seatPosition, out _)
                    ? seatPosition
                    : slot.Table.GetCustomerTargetPosition();
                var asVip = roleFlags[index] == VisitSeedRole.Vip;
                var asRare = roleFlags[index] == VisitSeedRole.Rare;
                var customer = SpawnCustomerRuntime(spawnPosition, asVip: asVip, asRare: asRare);
                if (customer == null)
                {
                    continue;
                }

                customer.InstantSeatAtTable(slot.TableId, slot.SeatIndex);
                if (!seatedByTable.TryGetValue(slot.TableId, out var seated))
                {
                    seated = new List<TavernCustomerRuntimeController>();
                    seatedByTable[slot.TableId] = seated;
                }

                seated.Add(customer);
            }

            foreach (var pair in seatedByTable)
            {
                if (pair.Value == null || pair.Value.Count <= 0)
                {
                    continue;
                }

                if (!AllTables.TryGetValue(pair.Key, out var table) || table == null)
                {
                    continue;
                }

                customerFlowService.RegisterTableGroup(
                    tableCustomers,
                    tableCustomerGroups,
                    pair.Key,
                    pair.Value);
                tableStateService.SetWaitingServe(pair.Key, table, "待上菜", dispatchRuntimeChanged: false);
            }
        }

        /// <summary>
        /// 爆满店开场保底：若播种/快照后仍无入座贵客，替换一张未挂被拉客提示桌上的普通客。
        /// </summary>
        private void EnsureVisitHotTavernHasSeatedVip()
        {
            if (DataManager.Instance == null
                || !DataManager.Instance.IsVisitingHotTavern
                || vipCustomerTemplates.Count == 0
                || CountShopVipCustomers() > 0)
            {
                return;
            }

            if (!TryFindVisitHotVipReplacementSeat(out var tableId, out var table, out var seatIndex, out var replaceCustomer))
            {
                return;
            }

            var spawnPosition = table.TryGetSeatPoseByIndex(seatIndex, out var seatPosition, out _)
                ? seatPosition
                : table.GetCustomerTargetPosition();

            if (replaceCustomer != null)
            {
                customerFlowService.HandleCustomerExited(
                    replaceCustomer,
                    activeCustomers: null,
                    queuedCustomers: null,
                    tableCustomers,
                    tableCustomerGroups);
                ClearVipGuestActionBubble(replaceCustomer);
                customerWaitHudService.ReleaseCustomer(replaceCustomer);
                activeCustomers.Remove(replaceCustomer);
                queuedCustomers.Remove(replaceCustomer);
                ReleaseCustomerContext(replaceCustomer);
                if (replaceCustomer.gameObject != null)
                {
                    Destroy(replaceCustomer.gameObject);
                }
            }

            var vip = SpawnCustomerRuntime(spawnPosition, asVip: true);
            if (vip == null)
            {
                return;
            }

            vip.InstantSeatAtTable(tableId, seatIndex);
            if (TryGetTableCustomerGroup(tableId, out var remaining) && remaining != null && remaining.Count > 0)
            {
                remaining.Add(vip);
                if (!tableCustomers.ContainsKey(tableId))
                {
                    tableCustomers[tableId] = vip;
                }

                return;
            }

            customerFlowService.RegisterTableGroup(
                tableCustomers,
                tableCustomerGroups,
                tableId,
                new List<TavernCustomerRuntimeController> { vip });
            tableStateService.SetWaitingServe(tableId, table, "待上菜", dispatchRuntimeChanged: false);
        }

        private bool TryFindVisitHotVipReplacementSeat(
            out int tableId,
            out TableArea table,
            out int seatIndex,
            out TavernCustomerRuntimeController replaceCustomer)
        {
            tableId = 0;
            table = null;
            seatIndex = 0;
            replaceCustomer = null;

            var tableIds = new List<int>(AllTables.Keys);
            tableIds.Sort();
            for (var index = 0; index < tableIds.Count; index++)
            {
                var candidateId = tableIds[index];
                if (tablesWithPulledTip.Contains(candidateId) || TableHasVipCustomer(candidateId))
                {
                    continue;
                }

                if (!AllTables.TryGetValue(candidateId, out var candidateTable) || candidateTable == null)
                {
                    continue;
                }

                if (!TryGetTableCustomerGroup(candidateId, out var customers) || customers == null)
                {
                    continue;
                }

                for (var seat = 0; seat < customers.Count; seat++)
                {
                    var customer = customers[seat];
                    if (customer == null || customer.IsVip || customer.IsLeavingTavern)
                    {
                        continue;
                    }

                    tableId = candidateId;
                    table = candidateTable;
                    seatIndex = customer.SeatIndex;
                    replaceCustomer = customer;
                    return true;
                }
            }

            return false;
        }

        private enum VisitSeedRole
        {
            Normal = 0,
            Vip = 1,
            Rare = 2
        }

        /// <summary>
        /// 拜访开场座位角色：爆满店必定 1 贵客，其余只按稀客概率；非爆满全普客。
        /// </summary>
        private VisitSeedRole[] ResolveVisitSeedCustomerRoles(int seatCount)
        {
            var roles = new VisitSeedRole[seatCount];
            for (var index = 0; index < seatCount; index++)
            {
                roles[index] = VisitSeedRole.Normal;
            }

            var isHot = DataManager.Instance != null && DataManager.Instance.IsVisitingHotTavern;
            var canVip = vipCustomerTemplates.Count > 0;
            var canRare = rareCustomerTemplates.Count > 0;

            if (isHot)
            {
                // 打乱座位，贵客随机落一座；其余座位不再刷贵客。
                var order = new List<int>(seatCount);
                for (var index = 0; index < seatCount; index++)
                {
                    order.Add(index);
                }

                for (var index = order.Count - 1; index > 0; index--)
                {
                    var swap = Random.Range(0, index + 1);
                    (order[index], order[swap]) = (order[swap], order[index]);
                }

                var cursor = 0;
                if (canVip && cursor < order.Count)
                {
                    // 爆满店开场保底 1 名贵客。
                    roles[order[cursor++]] = VisitSeedRole.Vip;
                }

                for (; cursor < order.Count; cursor++)
                {
                    roles[order[cursor]] = RollVisitSeedRole(canVip: false, canRare: canRare);
                }

                return roles;
            }

            for (var index = 0; index < seatCount; index++)
            {
                roles[index] = RollVisitSeedRole(canVip: false, canRare: false);
            }

            return roles;
        }

        private VisitSeedRole RollVisitSeedRole(bool canVip, bool canRare)
        {
            if (canVip && VipCustomerService.TrySpawnVip(true, GetEffectiveVipSpawnChance()))
            {
                return VisitSeedRole.Vip;
            }

            if (canRare && RareCustomerService.TrySpawnRare(true, GetEffectiveRareSpawnChance()))
            {
                return VisitSeedRole.Rare;
            }

            return VisitSeedRole.Normal;
        }

        private void TickVisitAutoCheckout()
        {
            var tableIds = new List<int>(AllTables.Keys);
            for (var index = 0; index < tableIds.Count; index++)
            {
                var tableId = tableIds[index];
                if (!IsTableInState(tableId, TavernTableRuntimeState.Checkout))
                {
                    continue;
                }

                // 他人店：不派小二，直接无收入结账。
                CompleteCheckoutWithoutIncome(tableId);
            }
        }

        private void RefreshVisitJiaoziVisibility()
        {
            visitJiaoziRoot ??= FindSceneTransformByName(VisitJiaoziNodeName);
            if (visitJiaoziRoot == null)
            {
                return;
            }

            CacheVisitJiaoziHomePoseIfNeeded();
            ApplyVisitJiaoziPose();

            var dataManager = DataManager.Instance;
            var purchased = dataManager != null && dataManager.IsJiaoziUnlocked();
            var unloadingAtHome = IsUnloadingPulledCustomersAtHome();
            var pullingAtVisit = dataManager != null
                && dataManager.IsVisitingOtherTavern
                && visitSimulationActive
                && (visitJiaoziPhase == VisitJiaoziPhase.Stationed
                    || visitJiaoziPhase == VisitJiaoziPhase.Departing);
            // 拉客 / 自家卸客：门口展示。
            var doorServiceActive = pullingAtVisit || unloadingAtHome;

            var show = false;
            if (doorServiceActive)
            {
                show = true;
            }
            else if (dataManager != null && dataManager.IsVisitingOtherTavern)
            {
                show = false;
            }
            else if (dataManager != null)
            {
                // 已购常驻（HireStaff_enter 结束后自动解锁）；对话前不显示。
                dataManager.TryGrantJiaoziUnlockedByProgress(dispatchSignals: false);
                purchased = dataManager.IsJiaoziUnlocked();
                show = purchased;
            }

            if (visitJiaoziRoot.gameObject.activeSelf != show)
            {
                visitJiaoziRoot.gameObject.SetActive(show);
            }

            if (show)
            {
                // 仅改 jiaozi 根节点材质，轿夫子节点保持原样。
                if (purchased || doorServiceActive)
                {
                    FacilityBuildVisualUtility.ApplyBuiltState(visitJiaoziRoot.gameObject, includeChildren: false);
                }
                else
                {
                    FacilityBuildVisualUtility.ApplyPreviewState(visitJiaoziRoot.gameObject, includeChildren: false);
                }
            }

            // 轿夫：拜访拉客/自家卸客时出现；自家常驻时仅可拉客（非 CD）出现。
            var canPullAtHome = purchased
                                && dataManager != null
                                && !dataManager.IsVisitingOtherTavern
                                && dataManager.IsPullCustomerCooldownReady()
                                && homeUnloadPhase == HomeUnloadPhase.None;
            var showBearers = show && (doorServiceActive || canPullAtHome);
            var bearersWalking = (showBearers && homeUnloadBearersWalking)
                                 || (visitJiaoziPhase == VisitJiaoziPhase.Departing && visitJiaoziBearersWalking);
            SetJiaoziBearerVisible(visitJiaoziRoot, showBearers, playWalk: bearersWalking);
            // 拉客/卸客放大抬高；常驻与归位恢复默认。
            ApplyJiaoziServiceVisual(doorServiceActive);
        }

        /// <summary>
        /// 自家酒楼卸客展示中（进场 / 卸客阶段）。
        /// </summary>
        private bool IsUnloadingPulledCustomersAtHome()
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null || dataManager.IsVisitingOtherTavern)
            {
                return false;
            }

            if (homeUnloadPhase != HomeUnloadPhase.None)
            {
                return true;
            }

            return dataManager.GetPendingPulledCustomerCount() > 0
                   && dataManager.TavernData != null
                   && dataManager.TavernData.isOpen
                   && !isClosingBusiness;
        }

        /// <summary>
        /// 缓存自家场景默认本地坐标/缩放与世界 Y，供拉客/卸客移动时只改 XZ。
        /// </summary>
        private void CacheVisitJiaoziHomePoseIfNeeded()
        {
            if (visitJiaoziRoot == null)
            {
                return;
            }

            if (!visitJiaoziHomePoseCached)
            {
                // 完整记录场景摆放（含 Y），归位时原样还原。
                visitJiaoziHomeLocalPosition = visitJiaoziRoot.localPosition;
                visitJiaoziHomeLocalScale = visitJiaoziRoot.localScale;
                visitJiaoziHomePoseCached = true;
            }

            if (!visitJiaoziLockedWorldYCached)
            {
                visitJiaoziLockedWorldY = JiaoziServiceWorldY;
                visitJiaoziLockedWorldYCached = true;
            }
        }

        /// <summary>
        /// 拜访轿子移动：只改 XZ，Y 固定拉客/卸客高度 0.85。
        /// </summary>
        private Vector3 WithVisitJiaoziLockedY(Vector3 worldPosition)
        {
            return new Vector3(worldPosition.x, JiaoziServiceWorldY, worldPosition.z);
        }

        /// <summary>
        /// 拜访拉客：停靠 JiaoziEndPoint；满载离场由 Tick 驱动；自家卸客由时序驱动；其余还原默认位。
        /// </summary>
        private void ApplyVisitJiaoziPose()
        {
            if (visitJiaoziRoot == null)
            {
                return;
            }

            // 自家卸客进场/停留、拜访满载驶离期间不覆盖位置与朝向。
            if (homeUnloadPhase != HomeUnloadPhase.None
                || visitJiaoziPhase == VisitJiaoziPhase.Departing)
            {
                return;
            }

            if (ShouldPlaceJiaoziAtVisitEndPoint())
            {
                PlaceVisitJiaoziAtEndPoint();
                return;
            }

            if (visitJiaoziHomePoseCached)
            {
                visitJiaoziRoot.localPosition = visitJiaoziHomeLocalPosition;
            }

            visitJiaoziRoot.localEulerAngles = Vector3.zero;
            ApplyJiaoziServiceVisual(false);
        }

        /// <summary>
        /// 拜访且容量未满：轿子停在场景 JiaoziEndPoint。
        /// </summary>
        private bool ShouldPlaceJiaoziAtVisitEndPoint()
        {
            var dataManager = DataManager.Instance;
            return dataManager != null
                   && dataManager.IsVisitingOtherTavern
                   && visitSimulationActive
                   && visitJiaoziPhase == VisitJiaoziPhase.Stationed;
        }

        /// <summary>
        /// 把轿子放到 JiaoziEndPoint（仅 XZ），Y 保持场景默认高度；朝向 JiaoziCustomerPoint。
        /// </summary>
        private void PlaceVisitJiaoziAtEndPoint()
        {
            EnsureVisitJiaoziRoot();
            if (visitJiaoziRoot == null)
            {
                return;
            }

            CacheVisitJiaoziHomePoseIfNeeded();
            var endPoint = FindSceneTransformByName(JiaoziEndPointName);
            var target = endPoint != null ? endPoint.position : VisitJiaoziWorldPosition;
            ApplyJiaoziServiceVisual(true);
            visitJiaoziRoot.position = WithVisitJiaoziLockedY(target);

            var customerPoint = FindSceneTransformByName(JiaoziCustomerPointName);
            if (customerPoint != null)
            {
                FaceJiaoziToward(customerPoint.position);
            }
            else
            {
                visitJiaoziRoot.localEulerAngles = new Vector3(0f, 90f, 0f);
            }
        }

        /// <summary>
        /// 进入拜访时：未满载则停靠接客，已满则保持隐藏。
        /// </summary>
        private void SyncVisitJiaoziPhaseOnEnter()
        {
            CancelVisitJiaoziDepart();
            var dataManager = DataManager.Instance;
            if (dataManager == null || dataManager.IsJiaoziCapacityFull())
            {
                visitJiaoziPhase = VisitJiaoziPhase.Hidden;
                return;
            }

            visitJiaoziPhase = VisitJiaoziPhase.Stationed;
            PlaceVisitJiaoziAtEndPoint();
        }

        /// <summary>
        /// 满载后：从当前停靠点驶向 PeopleStartPoint，到位后隐藏。
        /// </summary>
        private void BeginVisitJiaoziDepart()
        {
            if (visitJiaoziPhase == VisitJiaoziPhase.Departing
                || visitJiaoziPhase == VisitJiaoziPhase.Hidden)
            {
                return;
            }

            EnsureVisitJiaoziRoot();
            CacheVisitJiaoziHomePoseIfNeeded();
            var startPoint = FindSceneTransformByName(JiaoziEndPointName);
            var endPoint = FindSceneTransformByName(PeopleStartPointName);
            var startRaw = visitJiaoziRoot != null
                ? visitJiaoziRoot.position
                : (startPoint != null ? startPoint.position : VisitJiaoziWorldPosition);
            var endRaw = endPoint != null ? endPoint.position : startRaw;
            // 满载驶离同样锁 Y=0.85，并保持卸客/拉客缩放。
            visitJiaoziDepartLockedWorldY = JiaoziServiceWorldY;
            visitJiaoziDepartStartWorld = new Vector3(startRaw.x, visitJiaoziDepartLockedWorldY, startRaw.z);
            visitJiaoziDepartEndWorld = new Vector3(endRaw.x, visitJiaoziDepartLockedWorldY, endRaw.z);
            visitJiaoziDepartElapsed = 0f;
            visitJiaoziBearersWalking = true;
            visitJiaoziPhase = VisitJiaoziPhase.Departing;

            if (visitJiaoziRoot != null)
            {
                ApplyJiaoziServiceVisual(true);
                visitJiaoziRoot.position = visitJiaoziDepartStartWorld;
                FaceJiaoziToward(visitJiaoziDepartEndWorld);
                if (!visitJiaoziRoot.gameObject.activeSelf)
                {
                    visitJiaoziRoot.gameObject.SetActive(true);
                }
            }

            RefreshVisitJiaoziVisibility();
        }

        private void TickVisitJiaoziDepart(float deltaTime)
        {
            if (visitJiaoziPhase != VisitJiaoziPhase.Departing)
            {
                return;
            }

            EnsureVisitJiaoziRoot();
            if (visitJiaoziRoot == null)
            {
                FinalizeVisitJiaoziDepart();
                return;
            }

            visitJiaoziDepartElapsed += Mathf.Max(0f, deltaTime);
            var duration = Mathf.Max(0.01f, VisitJiaoziDepartSeconds);
            var t = Mathf.Clamp01(visitJiaoziDepartElapsed / duration);
            var pos = Vector3.Lerp(visitJiaoziDepartStartWorld, visitJiaoziDepartEndWorld, t);
            pos.y = visitJiaoziDepartLockedWorldY;
            visitJiaoziRoot.position = pos;
            FaceJiaoziToward(visitJiaoziDepartEndWorld);

            if (t < 1f)
            {
                return;
            }

            visitJiaoziRoot.position = new Vector3(
                visitJiaoziDepartEndWorld.x,
                visitJiaoziDepartLockedWorldY,
                visitJiaoziDepartEndWorld.z);
            FinalizeVisitJiaoziDepart();
        }

        private void FinalizeVisitJiaoziDepart()
        {
            visitJiaoziBearersWalking = false;
            visitJiaoziPhase = VisitJiaoziPhase.Hidden;
            visitJiaoziDepartElapsed = 0f;
            if (visitJiaoziRoot != null && visitJiaoziRoot.gameObject.activeSelf)
            {
                visitJiaoziRoot.gameObject.SetActive(false);
            }

            RefreshVisitJiaoziVisibility();
        }

        private void CancelVisitJiaoziDepart()
        {
            visitJiaoziPhase = VisitJiaoziPhase.Hidden;
            visitJiaoziDepartElapsed = 0f;
            visitJiaoziBearersWalking = false;
            visitJiaoziDepartPending = false;
        }

        /// <summary>
        /// 满载后预约驶离：等 visitPullingCustomers 全部走到点并销毁后再动。
        /// </summary>
        private void RequestVisitJiaoziDepartAfterPullersGone()
        {
            visitJiaoziDepartPending = true;
            TryStartVisitJiaoziDepartIfReady();
        }

        private void TryStartVisitJiaoziDepartIfReady()
        {
            if (!visitJiaoziDepartPending)
            {
                return;
            }

            if (visitJiaoziPhase == VisitJiaoziPhase.Departing
                || visitJiaoziPhase == VisitJiaoziPhase.Hidden)
            {
                visitJiaoziDepartPending = false;
                return;
            }

            PruneVisitPullingCustomers();
            if (visitPullingCustomers.Count > 0)
            {
                return;
            }

            visitJiaoziDepartPending = false;
            BeginVisitJiaoziDepart();
        }

        private void PruneVisitPullingCustomers()
        {
            if (visitPullingCustomers.Count == 0)
            {
                return;
            }

            visitPullingCustomers.RemoveWhere(customer => customer == null);
        }

        /// <summary>
        /// 拉客离场目标：优先 JiaoziCustomerPoint。
        /// </summary>
        private bool TryGetVisitPullDepartPosition(out Vector3 worldPosition)
        {
            var point = FindSceneTransformByName(JiaoziCustomerPointName);
            if (point != null)
            {
                worldPosition = point.position;
                return true;
            }

            worldPosition = default;
            return false;
        }

        /// <summary>
        /// 控制 jiaozi 下轿夫（jiaofu*）显隐；行走时开 Animator，停下则关（静态站姿）。
        /// </summary>
        private static void SetJiaoziBearerVisible(Transform jiaoziRoot, bool visible, bool playWalk = false)
        {
            if (jiaoziRoot == null)
            {
                return;
            }

            for (var index = 0; index < jiaoziRoot.childCount; index++)
            {
                var child = jiaoziRoot.GetChild(index);
                if (child == null
                    || child.name.IndexOf(JiaoziBearerNamePrefix, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (child.gameObject.activeSelf != visible)
                {
                    child.gameObject.SetActive(visible);
                }

                if (!visible)
                {
                    continue;
                }

                var animators = child.GetComponentsInChildren<Animator>(true);
                for (var animatorIndex = 0; animatorIndex < animators.Length; animatorIndex++)
                {
                    var animator = animators[animatorIndex];
                    if (animator == null)
                    {
                        continue;
                    }

                    if (playWalk)
                    {
                        if (!animator.enabled)
                        {
                            animator.enabled = true;
                        }

                        PrepareAnimatorForMovement(animator);
                        SetAnimatorSpeed(animator, WalkAnimationSpeed);
                        continue;
                    }

                    // 停下：关掉 Animator，保持静态站姿。
                    if (animator.enabled)
                    {
                        animator.enabled = false;
                    }
                }
            }
        }

        private void RefreshVisitDrumUpButtons()
        {
            if (!IsVisitSimulationRunning)
            {
                ClearAllVisitDrumUpButtons();
                return;
            }

            var eligible = new HashSet<TavernCustomerRuntimeController>();
            CollectVisitPullEligibleCustomers(eligible);

            var stale = new List<TavernCustomerRuntimeController>();
            foreach (var pair in visitDrumUpButtons)
            {
                if (pair.Key == null || !eligible.Contains(pair.Key) || visitPullingCustomers.Contains(pair.Key))
                {
                    stale.Add(pair.Key);
                }
            }

            for (var index = 0; index < stale.Count; index++)
            {
                ReleaseVisitDrumUpButton(stale[index]);
            }

            var dataManager = DataManager.Instance;
            foreach (var customer in eligible)
            {
                if (customer == null || visitPullingCustomers.Contains(customer))
                {
                    continue;
                }

                var capacityInsufficient = !CanAffordVisitPull(customer, dataManager);
                if (visitDrumUpButtons.TryGetValue(customer, out var existing) && existing != null)
                {
                    ApplyVisitDrumUpCapacityVisual(existing, capacityInsufficient);
                    continue;
                }

                CreateVisitDrumUpButton(customer, capacityInsufficient);
            }
        }

        private void CollectVisitPullEligibleCustomers(HashSet<TavernCustomerRuntimeController> eligible)
        {
            for (var index = 0; index < queuedCustomers.Count; index++)
            {
                var customer = queuedCustomers[index];
                if (IsCustomerEligibleForVisitPull(customer))
                {
                    eligible.Add(customer);
                }
            }

            for (var index = 0; index < activeCustomers.Count; index++)
            {
                var customer = activeCustomers[index];
                if (IsCustomerEligibleForVisitPull(customer))
                {
                    eligible.Add(customer);
                }
            }
        }

        /// <summary>
        /// 拜访拉客：未离店的客人显示拉客按钮（容量不足时置灰，仍可点出 tips）。
        /// </summary>
        private static bool IsCustomerEligibleForVisitPull(TavernCustomerRuntimeController customer)
        {
            return customer != null && !customer.IsLeavingTavern;
        }

        /// <summary>
        /// 当前轿子剩余容量是否够装下该客人。
        /// </summary>
        private static bool CanAffordVisitPull(
            TavernCustomerRuntimeController customer,
            DataManager dataManager)
        {
            if (customer == null || dataManager == null)
            {
                return false;
            }

            var kind = ResolvePulledKindFromCustomer(customer);
            var cost = DataManager.ResolvePullCapacityCostByKind(kind);
            return dataManager.CanPullWithCapacityCost(cost);
        }

        /// <summary>
        /// 解析拉客类型：优先 IsVip/IsRare，再用 M5/M6 表现名兜底。
        /// </summary>
        private static int ResolvePulledKindFromCustomer(TavernCustomerRuntimeController customer)
        {
            if (customer == null)
            {
                return DataManager.PulledCustomerKindNormal;
            }

            if (customer.IsVip)
            {
                return DataManager.PulledCustomerKindVip;
            }

            if (customer.IsRare)
            {
                return DataManager.PulledCustomerKindRare;
            }

            if (IsCustomerModelM5(customer.gameObject))
            {
                return DataManager.PulledCustomerKindVip;
            }

            if (IsCustomerModelM6(customer.gameObject))
            {
                return DataManager.PulledCustomerKindRare;
            }

            return DataManager.PulledCustomerKindNormal;
        }

        private void CreateVisitDrumUpButton(TavernCustomerRuntimeController customer, bool capacityInsufficient)
        {
            var hud = HudOverlayService.EnsureWorldRuntimeHudPanelForWaitHud();
            if (hud == null || customer == null)
            {
                return;
            }

            var captured = customer;
            var root = hud.ShowDrumUpButton(
                customer.transform,
                DrumUpWorldOffset,
                () => OnClickVisitDrumUp(captured),
                capacityInsufficient);
            if (root != null)
            {
                ApplyVisitDrumUpBtnCapacityVisual(root, capacityInsufficient);
                visitDrumUpButtons[customer] = root;
            }
        }

        private static void ApplyVisitDrumUpCapacityVisual(GameObject root, bool capacityInsufficient)
        {
            ApplyVisitDrumUpBtnCapacityVisual(root, capacityInsufficient);
        }

        /// <summary>
        /// 仅处理 DrumUpBtn：容量不足时 Icon/底板着色 #646464，透明度保持 1。
        /// </summary>
        private static void ApplyVisitDrumUpBtnCapacityVisual(GameObject root, bool capacityInsufficient)
        {
            if (root == null)
            {
                return;
            }

            var drumUpBtn = root.transform.Find("DrumUpBtn");
            if (drumUpBtn == null)
            {
                for (var index = 0; index < root.transform.childCount; index++)
                {
                    var child = root.transform.GetChild(index);
                    if (child != null && child.name == "DrumUpBtn")
                    {
                        drumUpBtn = child;
                        break;
                    }
                }
            }

            if (drumUpBtn == null)
            {
                return;
            }

            var tint = capacityInsufficient ? DrumUpBtnInsufficientIconColor : Color.white;
            var rootImage = drumUpBtn.GetComponent<Image>();
            if (rootImage != null)
            {
                rootImage.color = tint;
            }

            var icon = drumUpBtn.Find("ContentParent/Icon")
                       ?? drumUpBtn.Find("Icon");
            if (icon == null)
            {
                for (var index = 0; index < drumUpBtn.childCount; index++)
                {
                    var child = drumUpBtn.GetChild(index);
                    if (child == null)
                    {
                        continue;
                    }

                    if (child.name == "Icon")
                    {
                        icon = child;
                        break;
                    }

                    var nested = child.Find("Icon");
                    if (nested != null)
                    {
                        icon = nested;
                        break;
                    }
                }
            }

            var iconImage = icon != null ? icon.GetComponent<Image>() : null;
            if (iconImage != null)
            {
                iconImage.color = tint;
            }

            var canvasGroup = drumUpBtn.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = drumUpBtn.gameObject.AddComponent<CanvasGroup>();
            }

            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        private void ReleaseVisitDrumUpButton(TavernCustomerRuntimeController customer)
        {
            if (customer == null)
            {
                return;
            }

            if (visitDrumUpButtons.TryGetValue(customer, out var root) && root != null)
            {
                Destroy(root);
            }

            visitDrumUpButtons.Remove(customer);
        }

        private void ClearAllVisitDrumUpButtons()
        {
            foreach (var pair in visitDrumUpButtons)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value);
                }
            }

            visitDrumUpButtons.Clear();
        }

        /// <summary>
        /// 点击拉客：容量不足出 tips；够则立刻按类型写入待卸队列。
        /// </summary>
        private void OnClickVisitDrumUp(TavernCustomerRuntimeController customer)
        {
            if (!IsVisitSimulationRunning || customer == null || visitPullingCustomers.Contains(customer))
            {
                return;
            }

            var dataManager = DataManager.Instance;
            if (!CanAffordVisitPull(customer, dataManager))
            {
                HudOverlayService.ShowFloatingWarning("容量不足");
                return;
            }

            TryPullVisitCustomer(customer);
        }

        /// <summary>
        /// 点击拉客：立刻按类型写入待卸队列；走向门口仅表现，与卸客数据无关。
        /// </summary>
        private void TryPullVisitCustomer(TavernCustomerRuntimeController customer)
        {
            if (!IsVisitSimulationRunning || customer == null || visitPullingCustomers.Contains(customer))
            {
                return;
            }

            var dataManager = DataManager.Instance;
            if (!CanAffordVisitPull(customer, dataManager) || dataManager == null)
            {
                return;
            }

            // 点击当帧锁定类型并入队，后续离桌走路不得再改队列。
            var kind = ResolvePulledKindFromCustomer(customer);
            if (!dataManager.TryPullCustomerOntoJiaozi(kind))
            {
                HudOverlayService.ShowFloatingWarning("容量不足");
                RefreshVisitJiaoziVisibility();
                RefreshVisitDrumUpButtons();
                return;
            }

            visitPullingCustomers.Add(customer);
            ReleaseVisitDrumUpButton(customer);
            AbortVisitCustomerBindings(customer);
            // 先记下桌号：离桌后仍用于挂「客人已被我拉走」提示。
            var pulledFromTableId = customer.TableId;
            DetachVisitPulledCustomerFromTable(customer);
            TryShowSelfVisitPullTipOnTable(pulledFromTableId);
            // Solicit 在回店卸客成功后再累计，不在拜访点击时记。
            RefreshVisitJiaoziVisibility();
            RefreshVisitDrumUpButtons();

            // 纯表现：加速+拖尾走向 JiaoziCustomerPoint 后销毁，不读写待卸队列。
            if (TryGetVisitPullDepartPosition(out var departPos))
            {
                customer.SetExitPosition(departPos);
            }

            ApplyVisitPullDepartBoost(customer);
            HudOverlayService.ShowPulledAwayReviewTip(customer.transform);
            customer.LeaveTavern();
            StartCoroutine(ClearVisitPullFlagWhenGone(customer));

            // 满载：等所有在途拉客客人走到 JiaoziCustomerPoint 消失后，轿子再驶离。
            if (dataManager.IsJiaoziCapacityFull())
            {
                RequestVisitJiaoziDepartAfterPullersGone();
            }
        }

        /// <summary>
        /// 拉客离桌：挂叫醒同款拖尾，并按 weakSpeed 倍率加速走向门口。
        /// </summary>
        private void ApplyVisitPullDepartBoost(TavernCustomerRuntimeController customer)
        {
            if (customer == null)
            {
                return;
            }

            var speedMul = Mathf.Max(1f, waiterWakeSpeedMultiplier);
            customer.ApplyMoveSpeedMultiplier(speedMul);

            var prefab = LoadVisitPullTrailEffectPrefab();
            if (prefab == null)
            {
                return;
            }

            var effect = Instantiate(prefab, customer.transform, false);
            if (effect == null)
            {
                return;
            }

            effect.name = "Effect_Tuowei_VisitPull";
            effect.transform.localPosition = WaiterWakeBoostEffectLocalOffset;
            effect.transform.localRotation = Quaternion.identity;
            effect.transform.localScale = Vector3.one * WaiterWakeBoostEffectScale;
            effect.SetActive(true);
            foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particle.main;
                main.loop = true;
                particle.gameObject.SetActive(true);
                particle.Clear(true);
                particle.Play(true);
            }
            // 不设定时关闭：客人上轿销毁时特效一并销毁。
        }

        /// <summary>
        /// 拉走入座客人时立刻脱离桌组；桌空则恢复 Idle，便于继续进客占座观感。
        /// </summary>
        private void DetachVisitPulledCustomerFromTable(TavernCustomerRuntimeController customer)
        {
            if (customer == null || customer.TableId <= 0)
            {
                return;
            }

            var tableId = customer.TableId;
            customerFlowService.HandleCustomerExited(
                customer,
                activeCustomers: null,
                queuedCustomers: null,
                tableCustomers,
                tableCustomerGroups);

            if (TryGetTableCustomerGroup(tableId, out var remaining) && remaining != null && remaining.Count > 0)
            {
                return;
            }

            if (!AllTables.TryGetValue(tableId, out var table) || table == null)
            {
                return;
            }

            waitSatisfactionTracker.ClearTable(tableId);
            tableStateService.SetIdle(tableId, table, dispatchRuntimeChanged: false);
        }

        private IEnumerator ClearVisitPullFlagWhenGone(TavernCustomerRuntimeController customer)
        {
            while (customer != null)
            {
                yield return null;
            }

            visitPullingCustomers.Remove(customer);
            TryStartVisitJiaoziDepartIfReady();
        }

        private void AbortVisitCustomerBindings(TavernCustomerRuntimeController customer)
        {
            if (customer == null)
            {
                return;
            }

            // 仅解绑该客人，避免同桌其他人被整桌 Abort。
            RemoveCustomerFromFrontCounterBinding(customer);
        }
    }
}
