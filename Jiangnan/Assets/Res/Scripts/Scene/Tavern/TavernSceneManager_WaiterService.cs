using System.Collections;
using System.Collections.Generic;
using cfg;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.Model;
using JN.Client.UI;
using QFramework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace JN.Client.Scene
{
    public partial class TavernSceneManager : IWaiterRuntimeHost
    {
        #region Waiter Service

        private const float WaiterMoveSpeed = 1.15f;
        private const float WaiterReachDistance = 0.12f;
        private const float TableServiceOutwardPadding = 0.07f;
        private const float TableServiceSeatClearance = 0.24f;
        private const float TableServiceFallbackSeatClearance = 0.20f;
        private const float TableServiceFallbackExtraPadding = 0.05f;
        private static readonly float[] TableServiceCornerScales = { 0.88f, 0.94f, 0.80f, 1f };
        private const float WaiterTurnSpeed = 360f;
        private const float WaiterLookAheadDistance = 0.35f;
        private const float WalkAnimationSpeed = 0.65f;
        private const float CleanSmokeScale = 0.18f;
        private const float WaiterWakeBoostEffectScale = 1f;
        private const float StaffWakeBoostEffectScale = 0.5f;
        /// <summary>拉客拖尾：挂在客人本地空间，脚后略抬高。</summary>
        private static readonly Vector3 WaiterWakeBoostEffectLocalOffset = new(0f, 0.05f, -0.22f);
        /// <summary>叫醒加速光效：挂在小二/厨师身上跟随。</summary>
        private static readonly Vector3 StaffWakeBoostEffectLocalOffset = Vector3.zero;
        /// <summary>小二/厨师被叫醒后头顶问候气泡四选一。</summary>
        private static readonly string[] StaffWakeGreetingTips =
        {
            "老...老板好！",
            "得嘞！这就去！",
            "哎哟喂，来啦来啦！",
            "掌柜的，您就瞧好吧！"
        };
        private const float StaffWakeGreetingTipSeconds = 3f;
        private const float WaiterVisualScaleMultiplier = 0.76f;
        private const float WaiterMoveTotalTimeout = 8f;
        private const float WaiterMoveStuckCheckInterval = 0.4f;
        private const float WaiterMoveStuckProgressThreshold = 0.02f;
        private const float WaiterTaskProgressHeadOffset = TavernWorldRuntimeHudLayout.WaiterProgressHeightOffset;
        private const float WaiterCarryDishOnPlateYOffset = 0.025f;
        private const float WaiterCarryPlateScale = 2.5f;
        private static readonly Vector3 WaiterCarryPlateLocalPosition = new(-0.09f, 1f, 0.4f);
        private static readonly Vector3 WaiterCarryPlateLocalEuler = Vector3.zero;
        private static readonly Vector3 WaiterCarryPlateFallbackLocalPosition = new(0.15f, 1.05f, 0.35f);
        private static readonly Vector3 WaiterCarryPlateFallbackLocalEuler = new(-15f, 0f, 0f);
        private static readonly string[] WaiterCarryAttachBoneNames = { "Prop_R", "Hand_R", "Prop_L", "Hand_L" };
        private const int PreferredWaiterStaffId = 5;
        private const string CleanSmokeEffectPath = "Assets/Res/Resources/Effect/Effect_Smoke.prefab";
        /// <summary>叫醒小二/厨师加速期间挂载的光效。</summary>
        private const string StaffWakeBoostEffectPath = "Assets/Res/Resources/Effect/jiangnan_chushi.prefab";
        /// <summary>拜访拉客离桌拖尾（与叫醒光效分离）。</summary>
        private const string VisitPullTrailEffectPath = "Assets/Res/Resources/Effect/jiangnan_tuowei.prefab";

        private static int ResolvePreferredFloorWaiterStaffId()
        {
            var ids = DataManager.Instance != null
                ? DataManager.Instance.GetOwnedStaffIdsByPosition(StaffPosition.Waiter, includeTemporary: true)
                : null;
            if (ids != null && ids.Count > 0)
            {
                return ids[0];
            }

            return PreferredWaiterStaffId;
        }
        private const string DefaultOrderIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/dingDan.png";
        private const string CheckoutCoinIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/coin.png";
        private const string CheckoutVipIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/checkout.png";
        private const string WaiterCleanTrigger = "TrClean";
        private const string WaiterSpeedParam = "Speed";
        private const string AnimatorMovementState = "Movement";
        private const string AnimatorBaseLayerMovementState = "Base Layer.Movement";
        private const string ChefCookState = "Cook";
        private const string ChefBaseLayerCookState = "Base Layer.Cook";
        private const string AnimatorIsEatingParam = "IsEating";
        private const string ChefCookTrigger = "TrCook";
        private const float WaiterMinMaxStamina = 0.1f;
        private static GameObject cleanSmokeEffectPrefab;
        private static GameObject staffWakeBoostEffectPrefab;
        private static GameObject visitPullTrailEffectPrefab;

        [Header("Waiter Stamina")]
        [SerializeField, Min(0.1f)] private float waiterMaxStamina = 5f;
        [SerializeField, Min(0f)] private float waiterOrderStaminaCost = 0f;
        [SerializeField, Min(0f)] private float waiterNotifyChefStaminaCost = 0f;
        [SerializeField, Min(0f)] private float waiterServeStaminaCost = 0f;
        [SerializeField, Min(0f)] private float waiterCheckoutStaminaCost = 0f;
        [SerializeField, Min(0f)] private float waiterCleanStaminaCost = 1f;
        [SerializeField, Min(0.1f)] private float waiterStaminaRecoverInterval = 30f;
        [SerializeField, Min(1f)] private float waiterWakeSpeedMultiplier = 2f;
        [SerializeField, Min(0f)] private float waiterWakeStaminaRecover = 3f;
        [SerializeField, Min(0f), FormerlySerializedAs("waiterNapChance")] private float waiterNapStaminaThreshold = 0f;

        [Header("Chef Stamina")]
        [SerializeField, Min(0.1f)] private float chefMaxStamina = 2f;
        [SerializeField, Min(0f)] private float chefCookStaminaCost = 1f;
        [SerializeField, Min(0.1f)] private float chefStaminaRecoverInterval = 30f;
        [SerializeField, Min(1f)] private float chefWakeSpeedMultiplier = 3f;
        [SerializeField, Min(0f)] private float chefWakeStaminaRecover = 5f;
        [SerializeField, Min(0.1f)] private float chefWeakSpeedUpDuration = 8f;

        [Header("Waiter Nap")]
        [SerializeField] private float waiterNapCooldown = 10f;
        [SerializeField] private float waiterWakeStateDelay = 0.65f;//用于等待人物动画时长
        [SerializeField] private float waiterWakePostHudDelay = 0f;//用于HUD动画时长
        [SerializeField] private string waiterNapTrigger = "Sleep";
        [SerializeField] private string waiterWakeAnimationTrigger = "WakeUp";
        [SerializeField] private string waiterWakeHudTrigger = "shan";
        [SerializeField] private string waiterSleepHudTrigger = "sleeping";

        [Header("Waiter Checkout")]
        [SerializeField, Range(0f, 1f)] private float waiterStealChance = 0.25f;
        [Header("Waiter Cook Steal")]
        [SerializeField, Range(0f, 1f)] private float cookPhaseWaiterStealChance = 0.25f;
        [SerializeField] private float cookPhaseWaiterStealCooldown = 10f;
        [SerializeField] private Sprite cookPhaseWaiterStealingIcon;

        [Header("Waiter State Icons")]
        [SerializeField] private Sprite waiterIdleIcon;
        [SerializeField] private Sprite waiterOrderingIcon;
        [SerializeField] private Sprite waiterNotifyChefIcon;
        [SerializeField] private Sprite waiterServingIcon;
        [SerializeField] private Sprite waiterCheckoutIcon;
        [SerializeField] private Sprite waiterStealingIcon;
        [SerializeField] private Sprite waiterCleaningIcon;
        [SerializeField] private Sprite waiterNappingIcon;

        private enum WaiterServiceState
        {
            Idle,
            Ordering,
            NotifyChef,
            CookStealing,
            Serving,
            Checkout,
            Stealing,
            Cleaning,
            Napping,
            Attracting
        }

        private enum WaiterWakeFlowKind
        {
            Napping,
            Stealing,
            CookStealing
        }

        // 小二与桌位的派发关系：避免多个小二被分配到同一张桌处理同一件事
        private readonly HashSet<int> assignedOrderTableIds = new();
        private readonly HashSet<int> assignedServeTableIds = new();
        private readonly HashSet<int> assignedCheckoutTableIds = new();
        private readonly HashSet<int> assignedCleanTableIds = new();
        private readonly Dictionary<GameObject, int> waiterOrderAssignments = new();
        private readonly Dictionary<GameObject, int> waiterServeAssignments = new();
        private readonly Dictionary<GameObject, int> waiterCheckoutAssignments = new();
        private readonly Dictionary<GameObject, int> waiterCleanAssignments = new();
        /// <summary>玩家点结账中止后：禁止立刻遣回默认站位，原地待命。</summary>
        private readonly HashSet<GameObject> waitersSuppressHomeReturn = new();
        private readonly Dictionary<GameObject, WaiterServiceState> waiterServiceStates = new();
        private readonly Dictionary<GameObject, float> nextWaiterNapRollTimes = new();
        private readonly Dictionary<GameObject, float> nextWaiterStealRollTimes = new();
        private readonly Dictionary<GameObject, float> nextCookPhaseWaiterStealRollTimes = new();
        private readonly Dictionary<GameObject, float> waiterCurrentStamina = new();
        private readonly Dictionary<GameObject, float> waiterPassiveStaminaRecoverTimers = new();
        private readonly Dictionary<GameObject, float> chefCurrentStamina = new();
        private readonly Dictionary<GameObject, float> chefPassiveStaminaRecoverTimers = new();
        private readonly HashSet<GameObject> nappingChefs = new();
        private readonly Dictionary<GameObject, GameObject> activeChefStateIcons = new();
        private readonly Dictionary<GameObject, Coroutine> chefWakeRoutines = new();
        private float chefCookSpeedBoostMultiplier = 1f;
        private float chefCookSpeedBoostEndsAt;
        private readonly Dictionary<GameObject, GameObject> activeWaiterStateIcons = new();
        private readonly Dictionary<GameObject, GameObject> activeWaiterStealProgress = new();
        private readonly Dictionary<GameObject, Coroutine> waiterWakeRoutines = new();
        private readonly Dictionary<GameObject, GameObject> activeWaiterWakeBoostSmokeEffects = new();
        private readonly Dictionary<GameObject, Coroutine> waiterWakeBoostSmokeRoutines = new();
        private readonly Dictionary<GameObject, WaiterCharacter> waiterContexts = new();
        private readonly Dictionary<GameObject, int> waiterNapTableAssignments = new();
        private readonly Dictionary<int, GameObject> tableNapWaiters = new();
        private readonly HashSet<GameObject> stoppedWaiterSteals = new();
        private readonly HashSet<GameObject> stoppedCookPhaseWaiterSteals = new();
        private readonly Dictionary<GameObject, int> waiterCookStealAssignments = new();
        private readonly TavernTaskDispatchService waiterTaskDispatchService = new();

        /// <summary>
        /// 尝试派发小二上菜任务。
        /// </summary>
        /// <param name="tableId">需要上菜的桌位编号。</param>
        /// <param name="playerDirected">玩家点击派工时忽略技能门槛。</param>
        /// <returns>成功派发时返回 true，否则返回 false。</returns>
        private bool TryStartWaiterServeTask(int tableId, bool playerDirected = false)
        {
            if (assignedServeTableIds.Contains(tableId))
            {
                return false;
            }

            if (DataManager.Instance.GetHiredGuideWaiterCount() <= 0
                || !HasAvailablePreparedDishForServe(tableId))
            {
                return false;
            }

            var dispatched = waiterTaskDispatchService.TryDispatchWaiterTask(this, new WaiterServeTask(tableId), playerDirected);
            if (dispatched && playerDirected)
            {
                DataManager.Instance?.RecordManualServiceDispatch(true);
            }

            return dispatched;
        }

        /// <summary>
        /// 尝试派发小二点单任务。
        /// </summary>
        /// <param name="tableId">需要点单的桌位编号。</param>
        /// <param name="playerDirected">玩家点击派工时忽略技能门槛。</param>
        /// <returns>成功派发时返回 true，否则返回 false。</returns>
        private bool TryStartWaiterOrderTask(int tableId, bool playerDirected = false)
        {
            // 点单改由前台管理，小二不再接点单任务；仅负责后续上菜等。
            return false;
        }

        /// <summary>
        /// 尝试派发小二结账任务。
        /// </summary>
        /// <param name="playerDirected">玩家点击派工时忽略技能门槛。</param>
        private bool TryStartWaiterCheckoutTask(int tableId, bool playerDirected = false)
        {
            if (!playerDirected && !CanAutoDispatchWaiterCheckout())
            {
                return false;
            }

            if (assignedCheckoutTableIds.Contains(tableId))
            {
                return false;
            }

            if (DataManager.Instance.GetHiredGuideWaiterCount() <= 0)
            {
                return false;
            }

            ClearCheckoutCoinFlyPreplayed(tableId);
            var dispatched = waiterTaskDispatchService.TryDispatchWaiterTask(this, new WaiterCheckoutTask(tableId), playerDirected);
            if (dispatched && playerDirected)
            {
                DataManager.Instance?.RecordManualServiceDispatch(true);
            }

            return dispatched;
        }

        /// <summary>
        /// 尝试派发小二清扫桌位任务。
        /// </summary>
        /// <param name="tableId">需要清扫的桌位编号。</param>
        /// <returns>成功派发时返回 true，否则返回 false。</returns>
        private bool TryStartWaiterCleanTask(int tableId)
        {
            if (assignedCleanTableIds.Contains(tableId) || IsTableUpgrading(tableId))
            {
                return false;
            }

            if (DataManager.Instance.GetHiredGuideWaiterCount() <= 0)
            {
                return false;
            }

            return waiterTaskDispatchService.TryDispatchWaiterTask(this, new WaiterCleanTask(tableId));
        }

        /// <summary>
        /// 取消指定桌位正在排队或执行中的清扫任务，
        /// 让待升级桌在顾客离场后可以直接进入搬桌流程。
        /// </summary>
        /// <param name="tableId">桌位编号。</param>
        private void CancelWaiterCleanTask(int tableId)
        {
            assignedCleanTableIds.Remove(tableId);
            GameAudioManager.StopWiping(tableId);
            StopCleanSmokeEffect(tableId, null);

            GameObject targetWaiter = null;
            foreach (var pair in waiterCleanAssignments)
            {
                if (pair.Value == tableId)
                {
                    targetWaiter = pair.Key;
                    break;
                }
            }

            if (targetWaiter == null)
            {
                return;
            }

            SoftStopWaiterTaskRoutine(targetWaiter);
            ResetWaiterServiceAnimation(targetWaiter.GetComponentInChildren<Animator>(true));
            ReleaseWaiterAssignments(targetWaiter);
            busyWaiters.Remove(targetWaiter);
            SetWaiterServiceState(targetWaiter, WaiterServiceState.Idle);
        }

        /// <summary>
        /// 让空闲小二回到招聘默认站位；回位过程可被新任务打断。
        /// </summary>
        private void EnsureAllWaitersReturnedHome()
        {
            if (HasUnassignedCleaningTables())
            {
                return;
            }

            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            for (var index = 0; index < waiters.Length; index++)
            {
                var waiter = waiters[index];
                if (waiter == null
                    || busyWaiters.Contains(waiter)
                    || waiterTaskRoutines.ContainsKey(waiter)
                    || staffVisualsBeingAnimated.Contains(waiter)
                    || waitersSuppressHomeReturn.Contains(waiter))
                {
                    continue;
                }

                if (IsWaiterNapping(waiter))
                {
                    continue;
                }

                if (waiterContexts.TryGetValue(waiter, out var context)
                    && context != null
                    && context.CurrentStateKey == WaiterStateKeys.ReturningHome)
                {
                    continue;
                }

                var homeIndex = ResolveWaiterHomeIndex(waiter);
                if (IsWaiterNearHome(waiter, homeIndex))
                {
                    continue;
                }

                EnsureWaiterAnimationReceiver(waiter);
                waiterTaskDispatchService.StartReturnHome(this, waiter);
            }
        }

        /// <summary>
        /// 小二是否已在默认站位附近（避免空闲时反复触发回位）。
        /// </summary>
        private bool IsWaiterNearHome(GameObject waiter, int homeIndex)
        {
            if (waiter == null || !TryGetWaiterHomePose(Mathf.Max(0, homeIndex), out var homePosition, out _, out _))
            {
                return false;
            }

            var delta = waiter.transform.position - homePosition;
            delta.y = 0f;
            var nearDistance = Mathf.Max(0.25f, WaiterReachDistance * 3f);
            return delta.sqrMagnitude <= nearDistance * nearDistance;
        }

        private void EvaluateWaiterNapTransitions()
        {
            // 打盹现在只在收钱后判定，营业循环中不再对空闲小二做随机触发。
        }

        /// <summary>
        /// 快照恢复后：所有在场小二体力清零并立刻在桌位打盹（离线回来必打盹）。
        /// </summary>
        private void ForceAllWaitersNapAfterSnapshotRestore()
        {
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            if (waiters == null || waiters.Length == 0)
            {
                // 员工视觉可能晚一帧就绪，延后补一次。
                StartCoroutine(DeferredForceAllWaitersNapAfterSnapshotRestore());
                return;
            }

            ForceAllWaitersNapAfterSnapshotRestoreImmediate(waiters);
        }

        private IEnumerator DeferredForceAllWaitersNapAfterSnapshotRestore()
        {
            yield return null;
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            if (waiters == null || waiters.Length == 0)
            {
                yield break;
            }

            ForceAllWaitersNapAfterSnapshotRestoreImmediate(waiters);
        }

        private void ForceAllWaitersNapAfterSnapshotRestoreImmediate(GameObject[] waiters)
        {
            if (waiters == null || waiters.Length == 0)
            {
                return;
            }

            var napTables = CollectUnlockedTablesForForcedNap();
            var tableCursor = 0;
            for (var index = 0; index < waiters.Length; index++)
            {
                var waiter = waiters[index];
                if (waiter == null || !waiter.activeInHierarchy)
                {
                    continue;
                }

                EnsureWaiterStaminaInitialized(waiter, resetToFull: false);
                SetWaiterCurrentStamina(waiter, 0f, refreshHud: false);

                // 软中断：递增 epoch，勿 StopCoroutine（离线恢复时易撞 WaitForSeconds continue failure）。
                SoftStopWaiterTaskRoutine(waiter);
                StopWaiterHomeReturn(waiter);
                ReleaseWaiterAssignments(waiter);

                var tableId = -1;
                TableArea table = null;
                if (napTables.Count > 0)
                {
                    var pick = napTables[tableCursor % napTables.Count];
                    tableId = pick.tableId;
                    table = pick.table;
                    tableCursor++;
                }

                if (table != null)
                {
                    BindWaiterNapTable(waiter, tableId);
                    SnapWaiterToTableSeat(waiter, table);
                    EnterWaiterNap(waiter, tableId, resetServiceAnimation: false);
                }
                else
                {
                    EnterWaiterNap(waiter, -1, resetServiceAnimation: false);
                }

                // 打盹已由 IsWaiterNapping 表达，勿再塞进 busyWaiters，
                // 否则服务循环会认为全员忙碌，出餐后永远派不出上菜。
                busyWaiters.Remove(waiter);
                RefreshWaiterStateHud(waiter, true);
            }
        }

        private List<(int tableId, TableArea table)> CollectUnlockedTablesForForcedNap()
        {
            var result = new List<(int tableId, TableArea table)>();
            if (DataManager.Instance == null)
            {
                return result;
            }

            foreach (var pair in AllTables)
            {
                var tableData = DataManager.Instance.GetTableData(pair.Key);
                if (tableData == null || !tableData.isUnlocked || pair.Value == null)
                {
                    continue;
                }

                result.Add((pair.Key, pair.Value));
            }

            result.Sort((left, right) => left.tableId.CompareTo(right.tableId));
            return result;
        }

        private void EnsureWaiterNapCooldownInitialized(GameObject waiter)
        {
            if (waiter == null || nextWaiterNapRollTimes.ContainsKey(waiter))
            {
                return;
            }

            nextWaiterNapRollTimes[waiter] = 0f;
        }

        private void EnsureWaiterStealCooldownInitialized(GameObject waiter)
        {
            if (waiter == null || nextWaiterStealRollTimes.ContainsKey(waiter))
            {
                return;
            }

            nextWaiterStealRollTimes[waiter] = 0f;
        }

        private void EnsureCookPhaseWaiterStealCooldownInitialized(GameObject waiter)
        {
            if (waiter == null || nextCookPhaseWaiterStealRollTimes.ContainsKey(waiter))
            {
                return;
            }

            nextCookPhaseWaiterStealRollTimes[waiter] = 0f;
        }

        private void ApplyWaiterStaminaConfigFromTable()
        {
            var tavernLevel = DataManager.Instance != null ? DataManager.Instance.GetTavernLevel() : 1;
            waiterMaxStamina = TbConfigRuntime.GetWaiterMaxStaminaForLevel(tavernLevel, waiterMaxStamina);
            // 营业中清桌完成后扣体力；结账不扣。
            waiterCleanStaminaCost = TbConfigRuntime.GetWaiterTableStaminaCost(waiterCleanStaminaCost);
            waiterOrderStaminaCost = 0f;
            waiterNotifyChefStaminaCost = 0f;
            waiterServeStaminaCost = 0f;
            waiterCheckoutStaminaCost = 0f;
            waiterStaminaRecoverInterval = TbConfigRuntime.GetWaiterStaminaRecoverInterval(waiterStaminaRecoverInterval);
            waiterWakeSpeedMultiplier = TbConfigRuntime.GetWaiterWakeSpeedMultiplier(waiterWakeSpeedMultiplier);
            waiterWakeStaminaRecover = TbConfigRuntime.GetWaiterWakeStaminaRecover(waiterWakeStaminaRecover);
            waiterNapStaminaThreshold = 0f;

            chefMaxStamina = TbConfigRuntime.GetChefMaxStaminaForLevel(tavernLevel, chefMaxStamina);
            chefCookStaminaCost = TbConfigRuntime.GetChefCookStaminaCost(chefCookStaminaCost);
            chefStaminaRecoverInterval = TbConfigRuntime.GetChefStaminaRecoverInterval(chefStaminaRecoverInterval);
            chefWakeSpeedMultiplier = TbConfigRuntime.GetChefWakeSpeedMultiplier(chefWakeSpeedMultiplier);
            chefWakeStaminaRecover = TbConfigRuntime.GetChefWakeStaminaRecover(chefWakeStaminaRecover);
            chefWeakSpeedUpDuration = TbConfigRuntime.GetChefWeakSpeedUpTime(chefWeakSpeedUpDuration);
        }

        private float GetConfiguredWaiterMaxStamina()
        {
            var tavernLevel = DataManager.Instance != null ? DataManager.Instance.GetTavernLevel() : 1;
            return Mathf.Max(
                WaiterMinMaxStamina,
                TbConfigRuntime.GetWaiterMaxStaminaForLevel(tavernLevel, waiterMaxStamina));
        }

        private void EnsureWaiterStaminaInitialized(GameObject waiter, bool resetToFull = false)
        {
            if (waiter == null)
            {
                return;
            }

            var maxStamina = GetConfiguredWaiterMaxStamina();
            if (resetToFull || !waiterCurrentStamina.ContainsKey(waiter))
            {
                waiterCurrentStamina[waiter] = maxStamina;
                waiterPassiveStaminaRecoverTimers[waiter] = 0f;
            }
        }

        private float GetWaiterCurrentStamina(GameObject waiter)
        {
            if (waiter == null)
            {
                return 0f;
            }

            EnsureWaiterStaminaInitialized(waiter);
            return waiterCurrentStamina.TryGetValue(waiter, out var stamina)
                ? Mathf.Clamp(stamina, 0f, GetConfiguredWaiterMaxStamina())
                : 0f;
        }

        private void SetWaiterCurrentStamina(GameObject waiter, float stamina, bool refreshHud = true)
        {
            if (waiter == null)
            {
                return;
            }

            waiterCurrentStamina[waiter] = Mathf.Clamp(stamina, 0f, GetConfiguredWaiterMaxStamina());
            if (refreshHud)
            {
                // 体力条始终隐藏，仅在打盹时刷新可点击状态图标。
                RefreshWaiterStateHud(waiter);
            }
        }

        private float ResolveWaiterStaminaCost(WaiterStaminaAction action)
        {
            return action switch
            {
                WaiterStaminaAction.Ordering => Mathf.Max(0f, waiterOrderStaminaCost),
                WaiterStaminaAction.NotifyChef => Mathf.Max(0f, waiterNotifyChefStaminaCost),
                WaiterStaminaAction.Serving => Mathf.Max(0f, waiterServeStaminaCost),
                WaiterStaminaAction.Checkout => Mathf.Max(0f, waiterCheckoutStaminaCost),
                WaiterStaminaAction.Cleaning => Mathf.Max(0f, waiterCleanStaminaCost),
                _ => 0f
            };
        }

        private void ConsumeWaiterStamina(GameObject waiter, WaiterStaminaAction action)
        {
            if (waiter == null)
            {
                return;
            }

            EnsureWaiterStaminaInitialized(waiter);
            var cost = ResolveWaiterStaminaCost(action);
            if (cost <= 0f)
            {
                return;
            }

            var profile = ResolveStaffRuntimeProfile(waiter);
            if (profile != null)
            {
                cost *= profile.StaminaDrainMul;
            }

            var nextStamina = GetWaiterCurrentStamina(waiter) - cost;
            SetWaiterCurrentStamina(waiter, nextStamina);
        }

        private bool IsWaiterOutOfStamina(GameObject waiter)
        {
            return GetWaiterCurrentStamina(waiter) <= GetConfiguredWaiterNapStaminaThreshold() + 0.0001f;
        }

        private float GetConfiguredWaiterNapStaminaThreshold()
        {
            return Mathf.Clamp(waiterNapStaminaThreshold, 0f, GetConfiguredWaiterMaxStamina());
        }

        private bool IsWaiterRecoveringStamina(GameObject waiter)
        {
            // 体力条隐藏，打盹也不显示恢复中表现。
            return false;
        }

        /// <summary>
        /// 非打盹小二按配置间隔恢复 1 点体力。
        /// </summary>
        private void UpdateWaiterPassiveStaminaRecovery(float deltaTime)
        {
            if (deltaTime <= 0f
                || DataManager.Instance == null
                || DataManager.Instance.TavernData == null
                || !DataManager.Instance.TavernData.isOpen)
            {
                return;
            }

            var interval = Mathf.Max(0.1f, waiterStaminaRecoverInterval);
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            for (var index = 0; index < waiters.Length; index++)
            {
                var waiter = waiters[index];
                if (waiter == null || IsWaiterNapping(waiter))
                {
                    continue;
                }

                EnsureWaiterStaminaInitialized(waiter);
                var maxStamina = GetConfiguredWaiterMaxStamina();
                var current = GetWaiterCurrentStamina(waiter);
                if (current >= maxStamina - 0.0001f)
                {
                    waiterPassiveStaminaRecoverTimers[waiter] = 0f;
                    continue;
                }

                var timer = waiterPassiveStaminaRecoverTimers.TryGetValue(waiter, out var existing)
                    ? existing
                    : 0f;
                timer += deltaTime;
                while (timer >= interval && GetWaiterCurrentStamina(waiter) < maxStamina - 0.0001f)
                {
                    timer -= interval;
                    SetWaiterCurrentStamina(waiter, GetWaiterCurrentStamina(waiter) + 1f, refreshHud: false);
                }

                waiterPassiveStaminaRecoverTimers[waiter] = timer;
            }
        }

        private void ResetAllWaiterStamina()
        {
            waiterCurrentStamina.Clear();
            waiterPassiveStaminaRecoverTimers.Clear();
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            for (var index = 0; index < waiters.Length; index++)
            {
                var waiter = waiters[index];
                if (waiter == null)
                {
                    continue;
                }

                EnsureWaiterStaminaInitialized(waiter, true);
            }

            ResetAllChefStamina();
        }

        public void RestoreAllWaiterStaminaToFull()
        {
            ResetAllWaiterStamina();
            RefreshAllWaiterStateHuds();
            RefreshAllChefNapHuds();
        }

        /// <summary>
        /// 按酒楼等级取厨师体力上限。
        /// </summary>
        private float GetConfiguredChefMaxStamina()
        {
            var tavernLevel = DataManager.Instance != null ? DataManager.Instance.GetTavernLevel() : 1;
            return Mathf.Max(
                WaiterMinMaxStamina,
                TbConfigRuntime.GetChefMaxStaminaForLevel(tavernLevel, chefMaxStamina));
        }

        private void EnsureChefStaminaInitialized(GameObject chef, bool resetToFull = false)
        {
            if (chef == null)
            {
                return;
            }

            var maxStamina = GetConfiguredChefMaxStamina();
            if (resetToFull || !chefCurrentStamina.ContainsKey(chef))
            {
                chefCurrentStamina[chef] = maxStamina;
                chefPassiveStaminaRecoverTimers[chef] = 0f;
            }
        }

        private float GetChefCurrentStamina(GameObject chef)
        {
            if (chef == null)
            {
                return 0f;
            }

            EnsureChefStaminaInitialized(chef);
            return chefCurrentStamina.TryGetValue(chef, out var stamina)
                ? Mathf.Clamp(stamina, 0f, GetConfiguredChefMaxStamina())
                : 0f;
        }

        private void SetChefCurrentStamina(GameObject chef, float stamina, bool refreshHud = true)
        {
            if (chef == null)
            {
                return;
            }

            chefCurrentStamina[chef] = Mathf.Clamp(stamina, 0f, GetConfiguredChefMaxStamina());
            if (refreshHud && IsChefNapping(chef))
            {
                ShowChefNapHud(chef);
            }
        }

        private void ConsumeChefCookStamina(GameObject chef)
        {
            if (chef == null)
            {
                return;
            }

            EnsureChefStaminaInitialized(chef);
            var cost = Mathf.Max(0f, chefCookStaminaCost);
            if (cost <= 0f)
            {
                return;
            }

            var profile = ResolveStaffRuntimeProfile(chef);
            if (profile != null)
            {
                cost *= profile.StaminaDrainMul;
            }

            SetChefCurrentStamina(chef, GetChefCurrentStamina(chef) - cost, refreshHud: false);
        }

        private bool IsChefOutOfStamina(GameObject chef)
        {
            return GetChefCurrentStamina(chef) <= 0.0001f;
        }

        private bool IsChefNapping(GameObject chef)
        {
            return chef != null && nappingChefs.Contains(chef);
        }

        private void ResetAllChefStamina()
        {
            ClearAllChefNaps();
            chefCurrentStamina.Clear();
            chefPassiveStaminaRecoverTimers.Clear();
            chefCookSpeedBoostMultiplier = 1f;
            chefCookSpeedBoostEndsAt = 0f;
            var chefs = GetGuideStaffVisuals(GuideChefVisualKey);
            for (var index = 0; index < chefs.Length; index++)
            {
                var chef = chefs[index];
                if (chef == null)
                {
                    continue;
                }

                EnsureChefStaminaInitialized(chef, true);
            }
        }

        /// <summary>
        /// 非打盹厨师按配置间隔恢复 1 点体力。
        /// </summary>
        private void UpdateChefPassiveStaminaRecovery(float deltaTime)
        {
            if (deltaTime <= 0f
                || DataManager.Instance == null
                || DataManager.Instance.TavernData == null
                || !DataManager.Instance.TavernData.isOpen)
            {
                return;
            }

            var interval = Mathf.Max(0.1f, chefStaminaRecoverInterval);
            var chefs = GetGuideStaffVisuals(GuideChefVisualKey);
            for (var index = 0; index < chefs.Length; index++)
            {
                var chef = chefs[index];
                if (chef == null || IsChefNapping(chef))
                {
                    continue;
                }

                EnsureChefStaminaInitialized(chef);
                var maxStamina = GetConfiguredChefMaxStamina();
                if (GetChefCurrentStamina(chef) >= maxStamina - 0.0001f)
                {
                    chefPassiveStaminaRecoverTimers[chef] = 0f;
                    continue;
                }

                var timer = chefPassiveStaminaRecoverTimers.TryGetValue(chef, out var existing)
                    ? existing
                    : 0f;
                timer += deltaTime;
                while (timer >= interval && GetChefCurrentStamina(chef) < maxStamina - 0.0001f)
                {
                    timer -= interval;
                    SetChefCurrentStamina(chef, GetChefCurrentStamina(chef) + 1f, refreshHud: false);
                }

                chefPassiveStaminaRecoverTimers[chef] = timer;
            }
        }

        private float GetEffectiveChefCookSpeedMultiplier()
        {
            return Time.time < chefCookSpeedBoostEndsAt
                ? Mathf.Max(1f, chefCookSpeedBoostMultiplier)
                : 1f;
        }

        private void ApplyChefCookSpeedBoost(float multiplier, float durationSeconds)
        {
            var mul = Mathf.Max(1f, multiplier);
            var duration = Mathf.Max(0.1f, durationSeconds);
            chefCookSpeedBoostMultiplier = mul;
            chefCookSpeedBoostEndsAt = Time.time + duration;

            // 已在做菜的会话：按倍率压缩剩余时间。
            foreach (var pair in chefCookSessions)
            {
                var session = pair.Value;
                if (session == null)
                {
                    continue;
                }

                var remaining = session.EndsAt - Time.time;
                if (remaining > 0f)
                {
                    session.EndsAt = Time.time + remaining / mul;
                }
            }
        }

        /// <summary>
        /// 做菜完成后若体力耗尽则进入打盹（优先播 Sleep；控制器无状态时回退默认站姿 + UI）。
        /// </summary>
        private void TryEnterChefNapAfterCook(GameObject chef)
        {
            if (chef == null || IsChefNapping(chef) || !IsChefOutOfStamina(chef))
            {
                return;
            }

            EnterChefNap(chef);
        }

        private void EnterChefNap(GameObject chef)
        {
            if (chef == null)
            {
                return;
            }

            StopChefWakeRoutine(chef);
            nappingChefs.Add(chef);
            busyChefs.Remove(chef);
            GameAudioManager.StopChefCook(chef);
            // 先挂打盹 HUD，再播 Sleep，才能触发 HudAnim 的 zzz（sleeping）。
            ShowChefNapHud(chef);
            PlayChefNapAnimation(chef, chef.GetComponentInChildren<Animator>(true));
            TrackChefState(chef, ChefStateKeys.Napping);
        }

        private void PlayChefNapAnimation(GameObject chef, Animator animator)
        {
            if (animator == null || !animator.isActiveAndEnabled)
            {
                PlayChefSleepingHudAnimation(chef);
                return;
            }

            // 与小二一致：Sleep 需正常速度，否则易卡在过渡帧。
            SetAnimatorSpeed(animator, 1f);
            if (HasAnimatorParameter(animator, AnimatorIsEatingParam, AnimatorControllerParameterType.Bool))
            {
                animator.SetBool(AnimatorIsEatingParam, false);
            }

            if (HasAnimatorParameter(animator, "IsSitting", AnimatorControllerParameterType.Bool))
            {
                animator.SetBool("IsSitting", true);
            }

            if (HasAnimatorParameter(animator, "WakeUp", AnimatorControllerParameterType.Trigger))
            {
                animator.ResetTrigger("WakeUp");
            }

            if (HasAnimatorParameter(animator, "Sleep", AnimatorControllerParameterType.Trigger))
            {
                CrossFadeStateImmediate(animator, "Base Layer.Sleep", "Sleep");
                TriggerAnimator(animator, "Sleep");
            }
            else
            {
                // 控制器尚未接 Sleep 时保持默认站姿。
                ResetChefCookAnimationInternal(animator);
            }

            PlayChefSleepingHudAnimation(chef);
        }

        private void PlayChefSleepingHudAnimation(GameObject chef)
        {
            if (chef == null || !activeChefStateIcons.TryGetValue(chef, out var root) || root == null)
            {
                return;
            }

            var hudAnimTransform = HudBindingUtility.FindChildRecursive(root.transform, "HudAnim");
            var hudAnimator = hudAnimTransform != null ? hudAnimTransform.GetComponent<Animator>() : null;
            if (hudAnimator == null)
            {
                return;
            }

            hudAnimator.ResetTrigger(waiterWakeHudTrigger);
            hudAnimator.ResetTrigger(waiterSleepHudTrigger);
            TriggerAnimator(hudAnimator, waiterSleepHudTrigger);
        }

        private void ResetChefNapAnimation(GameObject chef)
        {
            if (chef == null)
            {
                return;
            }

            var animator = chef.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isActiveAndEnabled)
            {
                return;
            }

            if (HasAnimatorParameter(animator, "Sleep", AnimatorControllerParameterType.Trigger))
            {
                animator.ResetTrigger("Sleep");
            }

            if (HasAnimatorParameter(animator, "IsSitting", AnimatorControllerParameterType.Bool))
            {
                animator.SetBool("IsSitting", false);
            }

            ResetChefCookAnimationInternal(animator);
        }

        private void WakeChefFromNap(GameObject chef)
        {
            if (chef == null || !IsChefNapping(chef) || chefWakeRoutines.ContainsKey(chef))
            {
                return;
            }

            // 与小二叫醒一致：只播巴掌，不惨叫、不打爆金币。
            GameAudioManager.PlayInterruptCombo(playCoinBurst: false, playScream: false);
            ShowStaffWakeGreetingBubble(chef);
            DataManager.Instance?.RecordKickEmployee();

            var hudRoot = activeChefStateIcons.TryGetValue(chef, out var root) ? root : null;
            chefWakeRoutines[chef] = StartCoroutine(PlayChefWakeFlowRoutine(chef, hudRoot));
        }

        private IEnumerator PlayChefWakeFlowRoutine(GameObject chef, GameObject hudRoot)
        {
            var chefAnimator = chef != null ? chef.GetComponentInChildren<Animator>(true) : null;
            // 复用小二叫醒表现：角色 WakeUp + HudAnim shan。
            PlayWaiterWakeAnimation(chef, chefAnimator, hudRoot);

            var wakeDelay = Mathf.Max(0f, waiterWakeStateDelay);
            if (wakeDelay > 0f)
            {
                yield return new WaitForSeconds(wakeDelay);
            }
            else
            {
                yield return null;
            }

            var postHudDelay = Mathf.Max(0f, waiterWakePostHudDelay);
            if (postHudDelay > 0f)
            {
                yield return new WaitForSeconds(postHudDelay);
            }

            chefWakeRoutines.Remove(chef);
            FinalizeChefWakeFromNap(chef);
        }

        private void FinalizeChefWakeFromNap(GameObject chef)
        {
            if (chef == null)
            {
                return;
            }

            nappingChefs.Remove(chef);
            ClearChefNapHud(chef);
            ResetChefNapAnimation(chef);

            // 叫醒直接回满当前酒楼等级体力上限，不读 chefWakeStaminaRecover 配置。
            SetChefCurrentStamina(chef, GetConfiguredChefMaxStamina(), refreshHud: false);
            chefPassiveStaminaRecoverTimers[chef] = 0f;
            ApplyChefCookSpeedBoost(chefWakeSpeedMultiplier, chefWeakSpeedUpDuration);
            // 光效时长与厨师叫醒做菜加速一致；缩放按世界 0.5，不受厨师模型放大影响。
            PlayWaiterWakeBoostSmoke(chef, chefWeakSpeedUpDuration, StaffWakeBoostEffectScale);
            TrackChefState(chef, ChefStateKeys.Idle);
        }

        private void StopChefWakeRoutine(GameObject chef)
        {
            if (chef == null || !chefWakeRoutines.TryGetValue(chef, out var routine))
            {
                return;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            chefWakeRoutines.Remove(chef);
        }

        private void ClearAllChefNaps()
        {
            if (chefWakeRoutines.Count > 0)
            {
                var waking = new List<GameObject>(chefWakeRoutines.Keys);
                for (var index = 0; index < waking.Count; index++)
                {
                    StopChefWakeRoutine(waking[index]);
                }
            }

            if (nappingChefs.Count <= 0)
            {
                ClearAllChefNapHuds();
                return;
            }

            var chefs = new List<GameObject>(nappingChefs);
            nappingChefs.Clear();
            for (var index = 0; index < chefs.Count; index++)
            {
                ClearChefNapHud(chefs[index]);
            }
        }

        private TavernWorldRuntimeHudItemView GetOrCreateChefHudItem(GameObject chef)
        {
            if (chef == null)
            {
                return null;
            }

            activeChefStateIcons.TryGetValue(chef, out var existingRoot);
            var itemView = HudOverlayService.GetOrCreateWaiterTaskItem(existingRoot);
            if (itemView == null)
            {
                return null;
            }

            itemView.BindTarget(
                chef.transform,
                new Vector3(0f, TavernWorldRuntimeHudLayout.ChefProgressHeightOffset, 0f));
            activeChefStateIcons[chef] = itemView.gameObject;
            return itemView;
        }

        private void ShowChefNapHud(GameObject chef)
        {
            if (chef == null)
            {
                return;
            }

            var itemView = GetOrCreateChefHudItem(chef);
            if (itemView == null)
            {
                return;
            }

            // 与小二打盹按钮同一世界高度，不跟做菜进度条。
            itemView.BindTarget(
                chef.transform,
                new Vector3(0f, TavernWorldRuntimeHudLayout.StaffNapButtonHeightOffset, 0f));

            var icon = ResolveWaiterNappingIcon();
            if (icon == null)
            {
                return;
            }

            itemView.RefreshWaiterStateHud(
                icon,
                () => WakeChefFromNap(chef),
                GetChefCurrentStamina(chef),
                GetConfiguredChefMaxStamina(),
                false,
                preserveProgress: false,
                showStamina: false,
                useNativeSizeIcon: true,
                iconAnchoredY: 18f);
        }

        private void ClearChefNapHud(GameObject chef)
        {
            if (chef == null)
            {
                return;
            }

            if (!activeChefStateIcons.TryGetValue(chef, out var root))
            {
                return;
            }

            activeChefStateIcons.Remove(chef);
            if (root != null)
            {
                Destroy(root);
            }
        }

        private void ClearAllChefNapHuds()
        {
            foreach (var root in activeChefStateIcons.Values)
            {
                if (root != null)
                {
                    Destroy(root);
                }
            }

            activeChefStateIcons.Clear();
        }

        private void RefreshAllChefNapHuds()
        {
            foreach (var chef in nappingChefs)
            {
                if (chef != null)
                {
                    ShowChefNapHud(chef);
                }
            }
        }

        private void RefreshAllWaiterStateHuds()
        {
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            for (var index = 0; index < waiters.Length; index++)
            {
                RefreshWaiterStateHud(waiters[index]);
            }
        }

        private TavernWorldRuntimeHudItemView GetOrCreateWaiterHudItem(GameObject waiter)
        {
            if (waiter == null)
            {
                return null;
            }

            activeWaiterStateIcons.TryGetValue(waiter, out var existingRoot);
            var itemView = HudOverlayService.GetOrCreateWaiterTaskItem(existingRoot);
            if (itemView == null)
            {
                return null;
            }

            itemView.BindTarget(waiter.transform, new Vector3(0f, WaiterTaskProgressHeadOffset, 0f));
            activeWaiterStateIcons[waiter] = itemView.gameObject;
            return itemView;
        }

        private void RefreshWaiterStateHud(GameObject waiter)
        {
            RefreshWaiterStateHud(waiter, false);
        }

        private void RefreshWaiterStateHud(GameObject waiter, bool forceStateOnly)
        {
            if (waiter == null)
            {
                return;
            }

            if (!waiterServiceStates.TryGetValue(waiter, out var state))
            {
                state = WaiterServiceState.Idle;
                waiterServiceStates[waiter] = state;
            }

            // 仅打盹显示可点击偷懒图标；体力条始终隐藏。
            if (!CanShowWaiterStateIcon(state))
            {
                ClearWaiterStateIcon(waiter);
                return;
            }

            ShowWaiterStateIcon(waiter, state, !forceStateOnly && ShouldPreserveWaiterProgressHud(state));
        }

        private bool ShouldPreserveWaiterProgressHud(WaiterServiceState state)
        {
            return state == WaiterServiceState.Ordering
                   || state == WaiterServiceState.NotifyChef
                   || state == WaiterServiceState.Checkout
                   || state == WaiterServiceState.Stealing
                   || state == WaiterServiceState.CookStealing
                   || state == WaiterServiceState.Cleaning;
        }

        private void RefreshWaiterStaminaHudOnly(GameObject waiter)
        {
            // 体力条永久隐藏。
        }

        private bool TryStartWaiterNapAfterCleaning(int tableId, GameObject preferredWaiter = null)
        {
            if (!IsBusinessActive || isClosingBusiness)
            {
                return false;
            }

            if (!AllTables.TryGetValue(tableId, out var table) || table == null)
            {
                return false;
            }

            if (!TryGetWaiterForNap(tableId, table, preferredWaiter, out var waiter) || waiter == null)
            {
                return false;
            }

            if (!IsWaiterOutOfStamina(waiter) || IsWaiterNapping(waiter))
            {
                return false;
            }

            StartWaiterNapAtTable(waiter, tableId, table);
            return true;
        }

        private bool TryGetWaiterForNap(
            int tableId,
            TableArea table,
            GameObject preferredWaiter,
            out GameObject waiter)
        {
            if (preferredWaiter != null
                && IsWaiterOutOfStamina(preferredWaiter)
                && !IsWaiterNapping(preferredWaiter))
            {
                waiter = preferredWaiter;
                return true;
            }

            if (TryGetAssignedCleanWaiter(tableId, out waiter)
                && waiter != null
                && IsWaiterOutOfStamina(waiter)
                && !IsWaiterNapping(waiter))
            {
                return true;
            }

            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            waiter = null;
            if (waiters == null || waiters.Length == 0 || table == null)
            {
                return false;
            }

            var bestDistance = float.MaxValue;
            for (var index = 0; index < waiters.Length; index++)
            {
                var candidate = waiters[index];
                if (candidate == null
                    || !IsWaiterOutOfStamina(candidate)
                    || IsWaiterNapping(candidate)
                    || staffVisualsBeingAnimated.Contains(candidate))
                {
                    continue;
                }

                var distance = (candidate.transform.position - table.transform.position).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                waiter = candidate;
            }

            return waiter != null;
        }

        /// <summary>
        /// 解析清桌体力扣减对象：优先指定/清桌小二，否则就近未打盹小二。
        /// </summary>
        private GameObject ResolveWaiterForTableStaminaCost(int tableId, GameObject preferredWaiter)
        {
            if (preferredWaiter != null && !IsWaiterNapping(preferredWaiter))
            {
                return preferredWaiter;
            }

            if (TryGetAssignedCleanWaiter(tableId, out var assigned)
                && assigned != null
                && !IsWaiterNapping(assigned))
            {
                return assigned;
            }

            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            if (waiters == null || waiters.Length == 0)
            {
                return null;
            }

            GameObject fallback = null;
            var bestDistance = float.MaxValue;
            Vector3 tablePos = Vector3.zero;
            var hasTable = AllTables.TryGetValue(tableId, out var table) && table != null;
            if (hasTable)
            {
                tablePos = table.transform.position;
            }

            for (var index = 0; index < waiters.Length; index++)
            {
                var candidate = waiters[index];
                if (candidate == null || IsWaiterNapping(candidate))
                {
                    continue;
                }

                if (!hasTable)
                {
                    return candidate;
                }

                var distance = (candidate.transform.position - tablePos).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                fallback = candidate;
            }

            return fallback;
        }

        private bool TryGetAssignedCleanWaiter(int tableId, out GameObject waiter)
        {
            foreach (var pair in waiterCleanAssignments)
            {
                var candidate = pair.Key;
                if (pair.Value != tableId
                    || candidate == null
                    || staffVisualsBeingAnimated.Contains(candidate)
                    || IsWaiterNapping(candidate))
                {
                    continue;
                }

                waiter = candidate;
                return true;
            }

            waiter = null;
            return false;
        }

        private bool IsWaiterNapping(GameObject waiter)
        {
            if (waiter == null)
            {
                return false;
            }

            // 状态机优先；若服务态仍为 Napping 也视为打盹（防止两边不同步时派工误判）。
            if (waiterContexts.TryGetValue(waiter, out var context)
                && context != null
                && context.CurrentStateKey == WaiterStateKeys.Napping)
            {
                return true;
            }

            return waiterServiceStates.TryGetValue(waiter, out var state)
                   && state == WaiterServiceState.Napping;
        }

        private void EnterWaiterNap(GameObject waiter, int tableId = -1, bool resetServiceAnimation = true)
        {
            if (waiter == null)
            {
                return;
            }

            StopWaiterWakeRoutine(waiter);
            InterruptWaiterForNap(waiter, resetServiceAnimation);
            StopWaiterHomeReturn(waiter);
            BindWaiterNapTable(waiter, tableId);
            waiterTaskDispatchService.EnterNap(this, waiter);
            GameAudioManager.StopWaiterInterruptibleSounds(waiter);
            GameAudioManager.PlayWaiterNap(waiter);
            PlayWaiterNapAnimation(waiter, waiter.GetComponentInChildren<Animator>(true));
        }

        private void InterruptWaiterForNap(GameObject waiter, bool resetServiceAnimation = true)
        {
            if (waiter == null)
            {
                return;
            }

            SoftStopWaiterTaskRoutine(waiter);
            StopWaiterHomeReturn(waiter);

            if (waiterOrderAssignments.TryGetValue(waiter, out var orderTableId)
                && AllTables.TryGetValue(orderTableId, out var orderTable)
                && orderTable != null)
            {
                var orderTableData = DataManager.Instance.GetTableData(orderTableId);
                if (orderTableData != null
                    && (TavernTableRuntimeState)orderTableData.runtimeState == TavernTableRuntimeState.WaitingOrder)
                {
                    orderTable.RefreshRuntimeState(TavernTableRuntimeState.WaitingOrder);
                }
            }

            if (waiterCleanAssignments.TryGetValue(waiter, out var cleanTableId)
                && AllTables.TryGetValue(cleanTableId, out var cleanTable)
                && cleanTable != null)
            {
                var cleanTableData = DataManager.Instance.GetTableData(cleanTableId);
                if (cleanTableData != null
                    && (TavernTableRuntimeState)cleanTableData.runtimeState == TavernTableRuntimeState.Cleaning)
                {
                    cleanTable.RefreshRuntimeState(TavernTableRuntimeState.Cleaning, "等待清理");
                }
            }

            if (waiterContexts.TryGetValue(waiter, out var interruptContext)
                && interruptContext != null
                && interruptContext.PendingDishVisual != null)
            {
                ReleaseReservedServeDish();
            }

            ReleaseWaiterAssignments(waiter);
            busyWaiters.Remove(waiter);
            if (resetServiceAnimation)
            {
                ResetWaiterServiceAnimation(waiter.GetComponentInChildren<Animator>(true));
            }
        }

        private void WakeWaiterFromNap(GameObject waiter, System.Action onWakeFlowComplete = null)
        {
            if (waiter == null || !IsWaiterNapping(waiter) || waiterWakeRoutines.ContainsKey(waiter))
            {
                return;
            }

            GameAudioManager.StopWaiterNap(waiter);
            // 叫醒打盹不产钱、不惨叫：只播巴掌。
            GameAudioManager.PlayInterruptCombo(playCoinBurst: false, playScream: false);
            ShowStaffWakeGreetingBubble(waiter);
            // 开业任务 KickEmployee：玩家踢醒偷懒小二计 1 次。
            DataManager.Instance?.RecordKickEmployee();
            StartWaiterWakeFlow(
                waiter,
                WaiterWakeFlowKind.Napping,
                ResolveWaiterWakeHudRoot(waiter),
                true,
                () =>
                {
                    // 收尾必须清打盹标记；勿再依赖 IsWaiterNapping 早退，否则状态/占桌会残留。
                    FinalizeWaiterWakeFromNap(waiter, recoverStamina: true, applySpeedBoost: true);
                    onWakeFlowComplete?.Invoke();
                });
        }

        /// <summary>
        /// 叫醒瞬间弹出问候气泡（四选一，限时自动消失）；小二/厨师共用。
        /// </summary>
        private static void ShowStaffWakeGreetingBubble(GameObject staff)
        {
            if (staff == null || StaffWakeGreetingTips.Length == 0)
            {
                return;
            }

            var tip = StaffWakeGreetingTips[Random.Range(0, StaffWakeGreetingTips.Length)];
            HudOverlayService.ShowCustomerReviewTip(
                staff.transform,
                tip,
                durationSeconds: StaffWakeGreetingTipSeconds);
        }

        /// <summary>
        /// 叫醒收尾：统一清除打盹状态键、服务态、占桌、busy，并恢复体力（可幂等重复调用）。
        /// </summary>
        private void FinalizeWaiterWakeFromNap(GameObject waiter, bool recoverStamina, bool applySpeedBoost)
        {
            if (waiter == null)
            {
                return;
            }

            ReleaseWaiterNapTable(waiter);
            busyWaiters.Remove(waiter);
            SoftStopWaiterTaskRoutine(waiter);
            waiterTaskDispatchService.WakeFromNap(this, waiter);
            // WakeFromNap → SetPassive Idle 会走 ApplyWaiterPresentation，清掉 Napping 服务态。
            // 再强制写一遍，避免上下文缺失时 waiterServiceStates 残留 Napping。
            SetWaiterServiceState(waiter, WaiterServiceState.Idle);

            if (recoverStamina)
            {
                // 叫醒直接回满当前酒楼等级体力上限，不读 wakeStaminaRecover 配置。
                SetWaiterCurrentStamina(waiter, GetConfiguredWaiterMaxStamina(), refreshHud: false);
                waiterPassiveStaminaRecoverTimers[waiter] = 0f;
            }

            if (applySpeedBoost)
            {
                TavernBusinessModifierService.Instance.ApplyTimedServiceSpeedModifier(
                    this,
                    TavernBusinessModifierService.WaiterWakeBoostSource,
                    Mathf.Max(1f, waiterWakeSpeedMultiplier),
                    weekSpeedUpDuration);
                PlayWaiterWakeBoostSmoke(waiter, weekSpeedUpDuration);
            }

            ResetWaiterServiceAnimation(waiter.GetComponentInChildren<Animator>(true));
            RefreshWaiterStateHud(waiter, true);
        }

        private void StartWaiterWakeFlow(
            GameObject waiter,
            WaiterWakeFlowKind wakeFlowKind,
            GameObject hudRoot,
            bool resetNapCooldown,
            System.Action onComplete)
        {
            if (waiter == null || waiterWakeRoutines.ContainsKey(waiter) || !IsWaiterWakeFlowStateStillActive(waiter, wakeFlowKind))
            {
                return;
            }

            waiterWakeRoutines[waiter] = StartCoroutine(
                PlayWaiterWakeFlowRoutine(waiter, wakeFlowKind, hudRoot, resetNapCooldown, onComplete));
        }

        private IEnumerator PlayWaiterWakeFlowRoutine(
            GameObject waiter,
            WaiterWakeFlowKind wakeFlowKind,
            GameObject hudRoot,
            bool resetNapCooldown,
            System.Action onComplete)
        {
            var waiterAnimator = waiter != null ? waiter.GetComponentInChildren<Animator>(true) : null;
            PlayWaiterWakeAnimation(waiter, waiterAnimator, hudRoot);
            if (resetNapCooldown)
            {
                ResetWaiterNapCooldown(waiter);
            }

            var wakeDelay = Mathf.Max(0f, waiterWakeStateDelay);
            if (wakeDelay > 0f)
            {
                yield return new WaitForSeconds(wakeDelay);
            }
            else
            {
                yield return null;
            }

            var postHudDelay = Mathf.Max(0f, waiterWakePostHudDelay);
            if (postHudDelay > 0f)
            {
                yield return new WaitForSeconds(postHudDelay);
            }

            waiterWakeRoutines.Remove(waiter);
            if (wakeFlowKind == WaiterWakeFlowKind.Napping)
            {
                // 醒动画结束后再清占桌/打盹标记，避免动画期间客人抢回该桌。
                // 无论中间状态是否被改过，都走 Finalize，保证 Napping 标记被清掉。
                onComplete?.Invoke();
                TryPrepareFrontCounterOrders();
                yield break;
            }

            if (waiter != null && IsWaiterWakeFlowStateStillActive(waiter, wakeFlowKind))
            {
                onComplete?.Invoke();
            }
        }

        private void StopWaiterWakeRoutine(GameObject waiter)
        {
            if (waiter == null || !waiterWakeRoutines.TryGetValue(waiter, out var routine))
            {
                return;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            waiterWakeRoutines.Remove(waiter);
        }

        private void StopAllWaiterWakeRoutines()
        {
            foreach (var pair in waiterWakeRoutines)
            {
                if (pair.Value != null)
                {
                    StopCoroutine(pair.Value);
                }
            }

            waiterWakeRoutines.Clear();
        }

        private void WakeAllNappingWaitersForClosing()
        {
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            for (var index = 0; index < waiters.Length; index++)
            {
                var waiter = waiters[index];
                if (waiter == null || !IsWaiterNapping(waiter))
                {
                    continue;
                }

                StopWaiterWakeRoutine(waiter);
                ResetWaiterNapCooldown(waiter);
                FinalizeWaiterWakeFromNap(waiter, recoverStamina: false, applySpeedBoost: false);
            }
        }

        private bool IsWaiterWakeFlowStateStillActive(GameObject waiter, WaiterWakeFlowKind wakeFlowKind)
        {
            return wakeFlowKind switch
            {
                WaiterWakeFlowKind.Napping => IsWaiterNapping(waiter),
                WaiterWakeFlowKind.Stealing => IsWaiterInState(waiter, WaiterStateKeys.Stealing, WaiterServiceState.Stealing),
                WaiterWakeFlowKind.CookStealing => IsWaiterInState(waiter, WaiterStateKeys.CookStealing, WaiterServiceState.CookStealing),
                _ => false
            };
        }

        private bool IsWaiterInState(GameObject waiter, string stateKey, WaiterServiceState fallbackState)
        {
            if (waiter == null)
            {
                return false;
            }

            if (waiterContexts.TryGetValue(waiter, out var context) && context != null)
            {
                return context.CurrentStateKey == stateKey;
            }

            return waiterServiceStates.TryGetValue(waiter, out var state) && state == fallbackState;
        }

        private GameObject ResolveWaiterWakeHudRoot(GameObject waiter)
        {
            if (waiter == null)
            {
                return null;
            }

            if (activeWaiterStateIcons.TryGetValue(waiter, out var stateIconRoot) && stateIconRoot != null)
            {
                return stateIconRoot;
            }

            return null;
        }

        private void PlayWaiterWakeAnimation(GameObject waiter, Animator waiterAnimator, GameObject hudRoot = null)
        {
            if (waiterAnimator != null && waiterAnimator.isActiveAndEnabled)
            {
                SetAnimatorSpeed(waiterAnimator, 0f);
                if (!string.IsNullOrWhiteSpace(waiterNapTrigger))
                {
                    waiterAnimator.ResetTrigger(waiterNapTrigger);
                }

                if (!string.IsNullOrWhiteSpace(waiterWakeAnimationTrigger))
                {
                    TriggerAnimator(waiterAnimator, waiterWakeAnimationTrigger);
                }
                else
                {
                    CrossFadeMovementStateIfAvailable(waiterAnimator);
                }
            }

            if (hudRoot != null)
            {
                var customHudAnimTransform = HudBindingUtility.FindChildRecursive(hudRoot.transform, "HudAnim");
                var customHudAnimator = customHudAnimTransform != null ? customHudAnimTransform.GetComponent<Animator>() : null;
                if (customHudAnimator != null)
                {
                    customHudAnimator.ResetTrigger(waiterSleepHudTrigger);
                    TriggerAnimator(customHudAnimator, waiterWakeHudTrigger);
                }
                else
                {
                    Debug.LogWarning("[TavernSceneManager] Missing HudAnim animator on waiter HUD root.", hudRoot);
                }

                return;
            }

            if (waiter == null)
            {
                return;
            }

            if (!activeWaiterStateIcons.TryGetValue(waiter, out var stateIconRoot) || stateIconRoot == null)
            {
                return;
            }

            var hudAnimTransform = HudBindingUtility.FindChildRecursive(stateIconRoot.transform, "HudAnim");
            var hudAnimator = hudAnimTransform != null ? hudAnimTransform.GetComponent<Animator>() : null;
            if (hudAnimator == null)
            {
                Debug.LogWarning("[TavernSceneManager] 未找到 TavernWorldRuntimeHudItem/HudAnim 上的 Animator。", stateIconRoot);
                return;
            }

            hudAnimator.ResetTrigger(waiterSleepHudTrigger);
            TriggerAnimator(hudAnimator, waiterWakeHudTrigger);
        }

        private void StopAllWaiterWakeAnimations()
        {
            var handledRoots = new HashSet<GameObject>();
            foreach (var root in activeWaiterStateIcons.Values)
            {
                if (root != null)
                {
                    handledRoots.Add(root);
                }
            }

            foreach (var root in activeWaiterStealProgress.Values)
            {
                if (root != null)
                {
                    handledRoots.Add(root);
                }
            }

            foreach (var root in activeWaiterOrderCookProgress.Values)
            {
                if (root != null)
                {
                    handledRoots.Add(root);
                }
            }

            foreach (var root in handledRoots)
            {
                ResetWaiterWakeHudAnimation(root);
            }
        }

        private void ResetWaiterWakeHudAnimation(GameObject hudRoot)
        {
            if (hudRoot == null)
            {
                return;
            }

            var hudAnimTransform = HudBindingUtility.FindChildRecursive(hudRoot.transform, "HudAnim");
            var hudAnimator = hudAnimTransform != null ? hudAnimTransform.GetComponent<Animator>() : null;
            if (hudAnimator == null)
            {
                return;
            }

            hudAnimator.ResetTrigger(waiterSleepHudTrigger);
            hudAnimator.ResetTrigger(waiterWakeHudTrigger);
        }

        private void ResetWaiterNapCooldown(GameObject waiter)
        {
            if (waiter != null)
            {
                nextWaiterNapRollTimes[waiter] = Time.time + Mathf.Max(0.1f, waiterNapCooldown);
            }
        }

        private void ResetWaiterStealCooldown(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            EnsureWaiterStealCooldownInitialized(waiter);
            nextWaiterStealRollTimes[waiter] = Time.time + Mathf.Max(0.1f, waiterStealCooldown);
        }

        private void ResetCookPhaseWaiterStealCooldown(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            EnsureCookPhaseWaiterStealCooldownInitialized(waiter);
            nextCookPhaseWaiterStealRollTimes[waiter] = Time.time + Mathf.Max(0.1f, cookPhaseWaiterStealCooldown);
        }

        private bool ShouldWaiterStealBeforeCheckout(GameObject waiter, int tableId)
        {
            // 玩法调整：关闭小二结账前偷钱。
            return false;
        }

        private bool ShouldWaiterStealWhileCooking(GameObject waiter, int tableId)
        {
            // 玩法调整：关闭小二等菜时偷吃。
            return false;
        }

        private void NotifyWaiterStealStopped(GameObject waiter)
        {
            if (waiter == null || stoppedWaiterSteals.Contains(waiter))
            {
                return;
            }

            stoppedWaiterSteals.Add(waiter);
            GameAudioManager.StopWaiterCheckoutSteal(waiter);
            GameAudioManager.PlayInterruptCombo();
            ResetWaiterStealCooldown(waiter);
            ClearWaiterStealProgress(waiter);
        }

        private bool HasWaiterStealBeenStopped(GameObject waiter)
        {
            return waiter != null && stoppedWaiterSteals.Contains(waiter);
        }

        private void NotifyWaiterCookStealStopped(GameObject waiter)
        {
            if (waiter == null || stoppedCookPhaseWaiterSteals.Contains(waiter))
            {
                return;
            }

            stoppedCookPhaseWaiterSteals.Add(waiter);
            GameAudioManager.StopWaiterCookSteal(waiter);
            GameAudioManager.PlayInterruptCombo();
            if (waiterCookStealAssignments.TryGetValue(waiter, out var tableId))
            {
                ShowWaiterOrderCookProgress(waiter, tableId, GetCookOrderIcon(tableId));
            }
            else
            {
                ClearWaiterOrderCookProgress(waiter);
            }
        }

        private bool HasWaiterCookStealBeenStopped(GameObject waiter)
        {
            return waiter != null && stoppedCookPhaseWaiterSteals.Contains(waiter);
        }

        /// <summary>
        /// 把小二移动到桌位附近可通行的服务点。
        /// </summary>
        /// <param name="waiter">小二表现对象。</param>
        /// <param name="table">目标桌位。</param>
        /// <returns>协程迭代器。</returns>
        private IEnumerator MoveWaiterToTable(GameObject waiter, TableArea table)
        {
            var targetPosition = ResolveTableServicePosition(table, waiter.transform.position);
            yield return MoveCharacterAlongNavMesh(waiter.transform, targetPosition, GetEffectiveWaiterMoveSpeed(waiter), true);
            yield return RotateCharacterToFace(waiter.transform, table.transform.position);
        }

        /// <summary>
        /// 把小二移动到前台柜台点单；无柜台时回退到队首排队锚点。
        /// </summary>
        private IEnumerator MoveWaiterToCounter(GameObject waiter)
        {
            if (waiter == null)
            {
                yield break;
            }

            var counterTarget = ResolveFrontCounterTarget();
            if (counterTarget == null)
            {
                yield break;
            }

            var facePoint = counterTarget.transform.position;
            var targetPosition = ResolveObjectServicePosition(counterTarget, waiter.transform.position);
            yield return MoveCharacterAlongNavMesh(waiter.transform, targetPosition, GetEffectiveWaiterMoveSpeed(waiter), true);
            yield return RotateCharacterToFace(waiter.transform, facePoint);
        }

        /// <summary>
        /// 前台点单锚点：优先引导柜台，其次队首排队点。
        /// </summary>
        private GameObject ResolveFrontCounterTarget()
        {
            if (guideCounterObject != null && guideCounterObject.activeInHierarchy)
            {
                return guideCounterObject;
            }

            if (queuePointAnchors.Count > 0 && queuePointAnchors[0] != null)
            {
                return queuePointAnchors[0].gameObject;
            }

            return null;
        }

        /// <summary>
        /// 把小二移动到灶台或蒸笼旁边，表现为先到出餐点取菜。
        /// </summary>
        /// <param name="waiter">小二表现对象。</param>
        /// <returns>协程迭代器。</returns>
        private IEnumerator MoveWaiterToDishPickup(GameObject waiter)
        {
            if (waiter == null)
            {
                yield break;
            }

            var pickupTarget = ResolveDishPickupTarget();
            if (pickupTarget == null)
            {
                yield break;
            }

            var targetPosition = ResolveObjectServicePosition(pickupTarget, waiter.transform.position);
            yield return MoveCharacterAlongNavMesh(waiter.transform, targetPosition, GetEffectiveWaiterMoveSpeed(waiter), true);
            yield return RotateCharacterToFace(waiter.transform, pickupTarget.transform.position);
        }

        /// <summary>
        /// 收钱后若判定打盹，则让小二走到当前桌位并在座位上进入打盹。
        /// </summary>
        private void StartWaiterNapAtTable(GameObject waiter, int tableId, TableArea table)
        {
            if (waiter == null || table == null)
            {
                return;
            }

            StopAutoClean(tableId);
            CancelWaiterCleanTask(tableId);
            StopWaiterHomeReturn(waiter);
            SoftStopWaiterTaskRoutine(waiter);

            ReleaseWaiterAssignments(waiter);
            BindWaiterNapTable(waiter, tableId);
            busyWaiters.Add(waiter);
            var context = GetOrCreateWaiterContext(waiter);
            var epoch = context != null ? context.BeginNewRoutineEpoch() : 0;
            waiterTaskRoutines[waiter] = StartCoroutine(
                RunWaiterRoutineGuarded(context, MoveWaiterToNapTableRoutine(waiter, tableId, table), epoch));
        }

        private IEnumerator MoveWaiterToNapTableRoutine(GameObject waiter, int tableId, TableArea table)
        {
            if (waiter == null || table == null)
            {
                yield break;
            }

            EnsureWaiterAnimationReceiver(waiter);
            yield return MoveWaiterToTable(waiter, table);

            if (waiter == null || table == null || !IsTableBlockedByWaiterNapInternal(tableId))
            {
                busyWaiters.Remove(waiter);
                waiterTaskRoutines.Remove(waiter);
                ReleaseWaiterNapTable(waiter);
                yield break;
            }

            SnapWaiterToTableSeat(waiter, table);
            waiterTaskRoutines.Remove(waiter);
            // 已入座：不再 Reset 回 Movement，立刻进 Sleep，避免座位上僵坐。
            EnterWaiterNap(waiter, tableId, resetServiceAnimation: false);
        }

        private bool TryGetAvailableWaiterForNap(TableArea table, out GameObject waiter)
        {
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            waiter = null;
            if (waiters == null || waiters.Length == 0 || table == null)
            {
                return false;
            }

            var bestDistance = float.MaxValue;
            for (var index = 0; index < waiters.Length; index++)
            {
                var candidate = waiters[index];
                if (candidate == null
                    || busyWaiters.Contains(candidate)
                    || IsWaiterBlockedForNapSelection(candidate)
                    || staffVisualsBeingAnimated.Contains(candidate)
                    || IsWaiterNapping(candidate))
                {
                    continue;
                }

                var distance = (candidate.transform.position - table.transform.position).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                waiter = candidate;
            }

            return waiter != null;
        }

        private bool TryGetAssignedCheckoutWaiter(int tableId, out GameObject waiter)
        {
            foreach (var pair in waiterCheckoutAssignments)
            {
                var candidate = pair.Key;
                if (pair.Value != tableId
                    || candidate == null
                    || staffVisualsBeingAnimated.Contains(candidate)
                    || IsWaiterNapping(candidate))
                {
                    continue;
                }

                waiter = candidate;
                return true;
            }

            waiter = null;
            return false;
        }

        private bool IsWaiterBlockedForNapSelection(GameObject waiter)
        {
            if (waiter == null || !waiterTaskRoutines.ContainsKey(waiter))
            {
                return false;
            }

            if (!waiterContexts.TryGetValue(waiter, out var context) || context == null)
            {
                return true;
            }

            if (context.CurrentStateKey == WaiterStateKeys.ReturningHome)
            {
                return false;
            }

            if (context.CurrentStateKey == WaiterStateKeys.MoveToAttractPoint
                || context.CurrentStateKey == WaiterStateKeys.Attracting)
            {
                return false;
            }

            if (context.CurrentStateKey == WaiterStateKeys.Idle)
            {
                waiterTaskRoutines.Remove(waiter);
                return false;
            }

            return true;
        }

        private void SnapWaiterToTableSeat(GameObject waiter, TableArea table)
        {
            if (waiter == null || table == null)
            {
                return;
            }

            if (!table.TryGetNearestSeatPose(waiter.transform.position, out var seatPosition, out var lookAtPosition, out var seatIndex)
                && !table.TryGetPrimarySeatPose(out seatPosition, out lookAtPosition))
            {
                return;
            }

            if (seatIndex < 0)
            {
                seatIndex = 0;
            }

            // 与客人入座同一套：SeatSlot 世界坐标 + 向桌轻推 + Lv2/3 平面微调 + 座位 Y。
            // 不再 Sample NavMesh：服务点附近的网格常盖不住座位，会把 TableArea_2/3 上的打盹点拽偏。
            const float towardTableOffset = 0.08f;
            var snapPosition = seatPosition;
            var lookDirection = lookAtPosition - seatPosition;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                lookDirection.Normalize();
                snapPosition = seatPosition + lookDirection * towardTableOffset;
            }

            snapPosition += table.GetSeatSnapPlanarOffset(seatIndex, seatPosition);
            snapPosition.y = table.GetSeatedCustomerY();
            waiter.transform.position = snapPosition;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                waiter.transform.rotation = Quaternion.LookRotation(lookDirection);
            }
        }

        private void BindWaiterNapTable(GameObject waiter, int tableId)
        {
            if (waiter == null || tableId < 0)
            {
                return;
            }

            ReleaseWaiterNapTable(waiter);
            if (tableNapWaiters.TryGetValue(tableId, out var existingWaiter) && existingWaiter != null && existingWaiter != waiter)
            {
                waiterNapTableAssignments.Remove(existingWaiter);
            }

            waiterNapTableAssignments[waiter] = tableId;
            tableNapWaiters[tableId] = waiter;
        }

        private void ReleaseWaiterNapTable(GameObject waiter)
        {
            if (waiter == null || !waiterNapTableAssignments.TryGetValue(waiter, out var tableId))
            {
                return;
            }

            waiterNapTableAssignments.Remove(waiter);
            if (tableNapWaiters.TryGetValue(tableId, out var mappedWaiter) && mappedWaiter == waiter)
            {
                tableNapWaiters.Remove(tableId);
            }
        }

        private bool IsTableBlockedByWaiterNapInternal(int tableId)
        {
            return tableNapWaiters.ContainsKey(tableId);
        }

        /// <summary>
        /// 从出餐台取走菜品并在小二手上挂盘；出餐台无菜时随机取一份菜品预制体（兼容无 FoodTable 场景）。
        /// </summary>
        private GameObject TakePreparedDishForWaiter(GameObject waiter)
        {
            if (!TryReservePreparedDishForServe())
            {
                return null;
            }

            var dishPrefab = TakePreparedDishPrefab() ?? GetRandomDishPrefab();
            if (dishPrefab == null)
            {
                ReleaseReservedServeDish();
                return null;
            }

            AttachWaiterCarryPlate(waiter, dishPrefab);
            return dishPrefab;
        }

        /// <summary>
        /// 卸掉小二手上的挂盘，并把菜品退回出餐台。
        /// </summary>
        private void ReturnWaiterCarryDish(GameObject waiter, GameObject dishPrefab)
        {
            ClearWaiterCarryPlate(waiter);
            ReturnPreparedDishPrefab(dishPrefab);
        }

        /// <summary>
        /// 在小二手上创建餐盘+菜表现；无挂点时退化为根节点前方偏移。
        /// </summary>
        private void AttachWaiterCarryPlate(GameObject waiter, GameObject dishPrefab)
        {
            if (waiter == null || dishPrefab == null || platePrefab == null)
            {
                return;
            }

            var context = GetOrCreateWaiterContext(waiter);
            if (context == null)
            {
                return;
            }

            ClearWaiterCarryPlate(waiter);

            var attachPoint = ResolveWaiterCarryAttachPoint(waiter);
            var useFallbackAttach = attachPoint == null;
            if (useFallbackAttach)
            {
                attachPoint = waiter.transform;
            }

            var plateInstance = Instantiate(platePrefab, attachPoint, false);
            plateInstance.name = "WaiterCarryPlate";
            plateInstance.transform.localPosition = useFallbackAttach
                ? WaiterCarryPlateFallbackLocalPosition
                : WaiterCarryPlateLocalPosition;
            plateInstance.transform.localRotation = Quaternion.Euler(
                useFallbackAttach ? WaiterCarryPlateFallbackLocalEuler : WaiterCarryPlateLocalEuler);
            plateInstance.transform.localScale = Vector3.one * WaiterCarryPlateScale;

            var dishInstance = Instantiate(dishPrefab, plateInstance.transform, false);
            dishInstance.name = dishPrefab.name;
            dishInstance.transform.localPosition = Vector3.up * WaiterCarryDishOnPlateYOffset;
            dishInstance.transform.localRotation = Quaternion.identity;
            dishInstance.transform.localScale = Vector3.one;

            context.PendingDishVisual = plateInstance;
        }

        /// <summary>
        /// 销毁小二当前手上的挂盘表现。
        /// </summary>
        private void ClearWaiterCarryPlate(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            var context = waiter.GetComponent<WaiterCharacter>();
            if (context == null || context.PendingDishVisual == null)
            {
                return;
            }

            Destroy(context.PendingDishVisual);
            context.PendingDishVisual = null;
        }

        private static Transform ResolveWaiterCarryAttachPoint(GameObject waiter)
        {
            if (waiter == null)
            {
                return null;
            }

            if (IsGuideVisualWaiter(waiter))
            {
                return waiter.transform;
            }

            for (var index = 0; index < WaiterCarryAttachBoneNames.Length; index++)
            {
                var bone = HudBindingUtility.FindChildRecursive(waiter.transform, WaiterCarryAttachBoneNames[index]);
                if (bone != null)
                {
                    return bone;
                }
            }

            return null;
        }

        private static bool IsGuideVisualWaiter(GameObject waiter)
        {
            var name = waiter != null ? waiter.name : null;
            return !string.IsNullOrEmpty(name)
                   && name.StartsWith("Waiter_")
                   && name.Contains("GuideVisual");
        }

        /// <summary>
        /// 执行小二上菜后的数据和表现切换。
        /// </summary>
        /// <param name="tableId">桌位编号。</param>
        /// <param name="table">桌位对象。</param>
        private void ServeTableByWaiter(int tableId, TableArea table, GameObject dishPrefab)
        {
            ReleaseReservedServeDish();
            DataManager.Instance.ChangeAvailableDishes(-1);
            TryGetTableCustomerGroup(tableId, out var diningCustomers);
            SuppressTableCustomerWaitHud(tableId, CustomerWaitHudState.WaitingServe);
            tableStateService.SetDining(tableId, table, dishEatDuration, dishPrefab, diningCustomers);
            MarkTableDiningTiming(tableId, dishEatDuration, elapsedAlready: 0f);
            waitSatisfactionTracker.OnDining(tableId);
            RemoveCookOrderTicket(tableId);
        }

        /// <summary>
        /// 小二完成点单并通知厨师后，切换桌位到等待上菜。
        /// </summary>
        /// <param name="tableId">桌位编号。</param>
        /// <param name="table">桌位对象。</param>
        private void CompleteTableOrderByWaiter(int tableId, TableArea table, Sprite orderIcon = null)
        {
            var tableData = DataManager.Instance.GetTableData(tableId);
            if (table == null || tableData == null || (TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.WaitingOrder)
            {
                return;
            }

            ClearWaitingOrderBubbleFlow(tableId);
            CreatePendingCookOrderTicket(tableId, ResolveTableOrderIcon(tableId, orderIcon));
            tableStateService.SetWaitingServe(tableId, table, "待上菜", dispatchRuntimeChanged: false);
            waitSatisfactionTracker.OnWaitingServe(tableId);
            SeatFrontCounterOrderCustomers(tableId);
            if (tableCustomers.TryGetValue(tableId, out var orderingCustomer) && orderingCustomer != null)
            {
                //orderingCustomer.ShowOrderBubbles(GetRandomOrderNames());
            }

            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        /// <summary>
        /// 在桌面播放清扫烟雾特效。
        /// </summary>
        /// <param name="table">正在清扫的桌位。</param>
        private void CreatePendingCookOrderTicket(int tableId, Sprite orderIcon)
        {
            tableCookOrderTickets[tableId] = new CookOrderTicket
            {
                tableId = tableId,
                icon = orderIcon != null ? orderIcon : ResolveDefaultOrderIcon(),
                cookStartedAt = 0f,
                cookDuration = GetEffectiveDishCookDuration(),
                isChefNotified = false,
                isCooking = false,
                isCompleted = false
            };
        }

        /// <summary>
        /// 小二到达通知厨师目标点后，正式把工单交给后厨，允许厨师开始接单。
        /// </summary>
        private void NotifyChefCookOrderTicket(int tableId)
        {
            if (!tableCookOrderTickets.TryGetValue(tableId, out var ticket) || ticket == null || ticket.isCompleted)
            {
                return;
            }

            ticket.isChefNotified = true;
        }

        /// <summary>
        /// 厨师真正接到工单并开始做菜时，启动这张工单的做菜计时。
        /// </summary>
        /// <param name="tableId">桌位编号。</param>
        /// <param name="cookDurationOverride">指定时长（秒）；&lt;=0 时回退读表基础时长。</param>
        private void StartCookOrderTicket(int tableId, float cookDurationOverride = -1f)
        {
            if (!tableCookOrderTickets.TryGetValue(tableId, out var ticket) || ticket == null)
            {
                return;
            }

            ticket.cookStartedAt = Time.time;
            ticket.cookDuration = cookDurationOverride > 0f
                ? cookDurationOverride
                : GetEffectiveDishCookDuration();
            ticket.isCooking = true;
            ticket.isCompleted = false;
        }

        private void CompleteCookOrderTicket(int tableId)
        {
            if (!tableCookOrderTickets.TryGetValue(tableId, out var ticket) || ticket == null)
            {
                return;
            }

            ticket.isCooking = false;
            ticket.isCompleted = true;
        }

        private void RemoveCookOrderTicket(int tableId)
        {
            tableCookOrderTickets.Remove(tableId);
        }

        private void ClearCookOrderTickets()
        {
            tableCookOrderTickets.Clear();
        }

        private float GetCookOrderProgress(int tableId)
        {
            if (!tableCookOrderTickets.TryGetValue(tableId, out var ticket) || ticket == null)
            {
                return 0f;
            }

            if (ticket.isCompleted)
            {
                return 1f;
            }

            if (!ticket.isCooking)
            {
                return 0f;
            }

            var duration = Mathf.Max(0.1f, ticket.cookDuration);
            return Mathf.Clamp01((Time.time - ticket.cookStartedAt) / duration);
        }

        private Sprite ResolveDefaultOrderIcon()
        {
            if (waiterNotifyChefIcon != null)
            {
                return waiterNotifyChefIcon;
            }

            var product = SO_Product.GetById(0);
            if (product != null && product.icon != null)
            {
                return product.icon;
            }

            var products = SO_Product.GetAll();
            if (products != null)
            {
                for (var index = 0; index < products.Count; index++)
                {
                    if (products[index] != null && products[index].icon != null)
                    {
                        return products[index].icon;
                    }
                }
            }

            return GameplayResourceStore.LoadAsset<Sprite>(DefaultOrderIconPath);
        }

        private Sprite ResolveWaiterCheckoutIcon(int tableId = 0)
        {
            // 普客 coin，有贵客 checkout。
            var path = TableHasVipCustomer(tableId) ? CheckoutVipIconPath : CheckoutCoinIconPath;
            var loaded = GameplayResourceStore.LoadAsset<Sprite>(path);
            if (loaded != null)
            {
                return loaded;
            }

            return waiterCheckoutIcon;
        }

        private Sprite ResolveWaiterStealingIcon()
        {
            return waiterStealingIcon != null ? waiterStealingIcon : ResolveWaiterCheckoutIcon() ?? ResolveDefaultOrderIcon();
        }

        private Sprite ResolveCookPhaseWaiterStealingIcon()
        {
            return cookPhaseWaiterStealingIcon != null ? cookPhaseWaiterStealingIcon : ResolveDefaultOrderIcon();
        }

        private Sprite GetCookOrderIcon(int tableId)
        {
            if (tableCookOrderTickets.TryGetValue(tableId, out var ticket) && ticket != null && ticket.icon != null)
            {
                return ticket.icon;
            }

            return ResolveDefaultOrderIcon();
        }

        private Sprite ResolveTableOrderIcon(int tableId, Sprite fallbackIcon)
        {
            if (TryGetTableCustomerGroup(tableId, out var customers))
            {
                for (var index = 0; index < customers.Count; index++)
                {
                    var productIcon = customers[index]?.DesiredProduct?.icon;
                    if (productIcon != null)
                    {
                        return productIcon;
                    }
                }
            }

            return fallbackIcon != null ? fallbackIcon : ResolveDefaultOrderIcon();
        }

        private void ShowWaiterOrderCookProgress(GameObject waiter, int tableId, Sprite orderIcon)
        {
            // 玩法调整：关闭小二头顶进度条。
            return;
        }

        private void ShowWaiterCookStealingProgress(GameObject waiter, int tableId)
        {
            // 玩法调整：关闭小二偷吃进度条。
            return;
        }

        private void ClearWaiterOrderCookProgress(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            GameAudioManager.StopWaiterCookSteal(waiter);
            activeWaiterOrderCookProgress.Remove(waiter);
            RefreshWaiterStateHud(waiter, true);
        }

        private void ClearAllWaiterOrderCookProgress()
        {
            foreach (var waiter in activeWaiterOrderCookProgress.Keys)
            {
                GameAudioManager.StopWaiterCookSteal(waiter);
            }

            activeWaiterOrderCookProgress.Clear();
        }

        private GameObject ShowWaiterStealProgress(GameObject waiter, float duration, Sprite icon, System.Action onClick)
        {
            // 玩法调整：关闭小二偷钱进度条。
            return null;
        }

        private void ClearWaiterStealProgress(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            GameAudioManager.StopWaiterCheckoutSteal(waiter);
            activeWaiterStealProgress.Remove(waiter);
            RefreshWaiterStateHud(waiter, true);
        }

        private void ClearAllWaiterStealProgress()
        {
            foreach (var waiter in activeWaiterStealProgress.Keys)
            {
                GameAudioManager.StopWaiterCheckoutSteal(waiter);
            }

            activeWaiterStealProgress.Clear();
            stoppedWaiterSteals.Clear();
        }

        private bool IsWaiterCookStealing(GameObject waiter, int tableId)
        {
            if (waiter == null || tableId <= 0)
            {
                return false;
            }

            if (!waiterCookStealAssignments.TryGetValue(waiter, out var assignedTableId) || assignedTableId != tableId)
            {
                return false;
            }

            if (waiterContexts.TryGetValue(waiter, out var context) && context != null)
            {
                return context.CurrentStateKey == WaiterStateKeys.CookStealing;
            }

            return waiterServiceStates.TryGetValue(waiter, out var state) && state == WaiterServiceState.CookStealing;
        }

        private bool TryGetCookStealingWaiter(int tableId, out GameObject waiter)
        {
            foreach (var pair in waiterCookStealAssignments)
            {
                var currentWaiter = pair.Key;
                if (pair.Value != tableId
                    || currentWaiter == null
                    || stoppedCookPhaseWaiterSteals.Contains(currentWaiter)
                    || !IsWaiterCookStealing(currentWaiter, tableId))
                {
                    continue;
                }

                waiter = currentWaiter;
                return true;
            }

            waiter = null;
            return false;
        }

        private void ShowWaiterStateIcon(GameObject waiter, WaiterServiceState state, bool preserveProgress = false)
        {
            if (waiter == null)
            {
                return;
            }

            var itemView = GetOrCreateWaiterHudItem(waiter);
            if (itemView == null)
            {
                return;
            }

            var isNapping = state == WaiterServiceState.Napping;
            // 打盹叫醒按钮单独用更低的世界高度偏移，避免飘得太高。
            itemView.BindTarget(
                waiter.transform,
                new Vector3(
                    0f,
                    isNapping
                        ? TavernWorldRuntimeHudLayout.StaffNapButtonHeightOffset
                        : WaiterTaskProgressHeadOffset,
                    0f));

            if (state == WaiterServiceState.Idle)
            {
                itemView.HideWaiterStatusBar();
                return;
            }

            var icon = ResolveWaiterStateIcon(state);
            if (icon == null)
            {
                return;
            }

            var clickAction = isNapping
                ? (System.Action)(() =>
                {
                    // 叫醒打盹：只恢复体力/加速，不播金币、不发钱。
                    WakeWaiterFromNap(waiter);
                })
                : null;

            itemView.RefreshWaiterStateHud(
                icon,
                clickAction,
                GetWaiterCurrentStamina(waiter),
                GetConfiguredWaiterMaxStamina(),
                IsWaiterRecoveringStamina(waiter),
                preserveProgress,
                showStamina: false,
                useNativeSizeIcon: isNapping,
                iconAnchoredY: isNapping ? 18f : null);
        }

        private Sprite ResolveWaiterStateIcon(WaiterServiceState state)
        {
            switch (state)
            {
                case WaiterServiceState.Idle:
                    return waiterIdleIcon;
                case WaiterServiceState.Ordering:
                    return waiterOrderingIcon;
                case WaiterServiceState.NotifyChef:
                    return waiterNotifyChefIcon;
                case WaiterServiceState.Serving:
                    return waiterServingIcon;
                case WaiterServiceState.Checkout:
                    return ResolveWaiterCheckoutIcon();
                case WaiterServiceState.Stealing:
                    return ResolveWaiterStealingIcon();
                case WaiterServiceState.Cleaning:
                    return waiterCleaningIcon;
                case WaiterServiceState.Napping:
                    return ResolveWaiterNappingIcon();
                default:
                    return null;
            }
        }

        /// <summary>
        /// 打盹可点击图标：优先 Inspector 引用，缺省时加载 Resources/Textures/UI/Buttons/kick。
        /// </summary>
        private Sprite ResolveWaiterNappingIcon()
        {
            if (waiterNappingIcon != null)
            {
                return waiterNappingIcon;
            }

            waiterNappingIcon = Resources.Load<Sprite>("Textures/UI/Buttons/kick");
            return waiterNappingIcon;
        }

        private void ClearWaiterStateIcon(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            if (!activeWaiterStateIcons.TryGetValue(waiter, out var root))
            {
                return;
            }

            activeWaiterStateIcons.Remove(waiter);
            if (root != null)
            {
                Destroy(root);
            }
        }

        private void ClearAllWaiterStateIcons()
        {
            foreach (var root in activeWaiterStateIcons.Values)
            {
                if (root != null)
                {
                    Destroy(root);
                }
            }

            activeWaiterStateIcons.Clear();
        }

        private GameObject PlayCleanSmokeEffect(int tableId, TableArea table)
        {
            if (table == null)
            {
                return null;
            }

            StopCleanSmokeEffect(tableId, null);

            var prefab = LoadCleanSmokeEffectPrefab();
            if (prefab == null)
            {
                return null;
            }

            var effect = Instantiate(prefab, table.GetTableEffectPosition(), Quaternion.identity);
            if (effect == null)
            {
                return null;
            }

            effect.name = "Effect_Smoke_CleanRuntime";
            effect.transform.localScale = Vector3.one * CleanSmokeScale;
            effect.SetActive(true);
            foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particle.main;
                main.loop = true;
                particle.gameObject.SetActive(true);
                particle.Clear(true);
                particle.Play(true);
            }

            activeCleanSmokeEffects[tableId] = effect;
            return effect;
        }

        /// <summary>
        /// 停止桌面清扫烟雾循环并延迟销毁。
        /// </summary>
        /// <param name="effect">烟雾特效实例。</param>
        private void StopCleanSmokeEffect(int tableId, GameObject effect)
        {
            if (effect == null && activeCleanSmokeEffects.TryGetValue(tableId, out var activeEffect))
            {
                effect = activeEffect;
            }

            if (effect == null)
            {
                return;
            }

            if (activeCleanSmokeEffects.TryGetValue(tableId, out var trackedEffect) && trackedEffect == effect)
            {
                activeCleanSmokeEffects.Remove(tableId);
            }

            foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            Destroy(effect, 1.2f);
        }

        /// <summary>
        /// 加载并缓存桌面清扫烟雾特效。
        /// </summary>
        /// <returns>烟雾特效预制体。</returns>
        private static GameObject LoadCleanSmokeEffectPrefab()
        {
            if (cleanSmokeEffectPrefab != null)
            {
                return cleanSmokeEffectPrefab;
            }

            cleanSmokeEffectPrefab = GameplayResourceStore.LoadAsset<GameObject>(CleanSmokeEffectPath);
            return cleanSmokeEffectPrefab;
        }

        /// <summary>
        /// 叫醒加速期间在小二/厨师身上挂 <c>jiangnan_chushi</c>，时长与加速持续一致。
        /// </summary>
        /// <param name="worldUniformScale">指定时按世界均匀缩放（抵消角色模型缩放）；空则沿用本地 0.5。</param>
        private void PlayWaiterWakeBoostSmoke(GameObject staff, float duration, float? worldUniformScale = null)
        {
            if (staff == null)
            {
                return;
            }

            StopWaiterWakeBoostSmoke(staff);

            var prefab = LoadStaffWakeBoostEffectPrefab();
            if (prefab == null)
            {
                return;
            }

            var effect = Instantiate(prefab, staff.transform, false);
            if (effect == null)
            {
                return;
            }

            effect.name = "Effect_Chushi_WakeBoost";
            effect.transform.localPosition = StaffWakeBoostEffectLocalOffset;
            // 预制体默认朝向与角色根节点不一致，本地 X 转 90° 贴地/贴身显示。
            effect.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            effect.transform.localScale = worldUniformScale.HasValue
                ? ComputeChildLocalScaleForWorldUniform(staff.transform, worldUniformScale.Value)
                : Vector3.one * StaffWakeBoostEffectScale;
            effect.SetActive(true);
            foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particle.main;
                main.loop = true;
                particle.gameObject.SetActive(true);
                particle.Clear(true);
                particle.Play(true);
            }

            // 必须登记，定时结束时才能按 staff 找到实例并销毁。
            activeWaiterWakeBoostSmokeEffects[staff] = effect;
            var holdSeconds = Mathf.Max(0f, duration);
            waiterWakeBoostSmokeRoutines[staff] = StartCoroutine(StopWaiterWakeBoostSmokeAfterDelay(staff, holdSeconds));
        }

        /// <summary>
        /// 把子物体本地缩放换算成目标世界均匀缩放，避免厨师模型放大把光效一起撑大。
        /// </summary>
        private static Vector3 ComputeChildLocalScaleForWorldUniform(Transform parent, float worldScale)
        {
            var lossy = parent != null ? parent.lossyScale : Vector3.one;
            return new Vector3(
                worldScale / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                worldScale / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                worldScale / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
        }

        /// <summary>
        /// 加载并缓存叫醒加速光效（jiangnan_chushi）。
        /// </summary>
        private static GameObject LoadStaffWakeBoostEffectPrefab()
        {
            if (staffWakeBoostEffectPrefab != null)
            {
                return staffWakeBoostEffectPrefab;
            }

            staffWakeBoostEffectPrefab = GameplayResourceStore.LoadAsset<GameObject>(StaffWakeBoostEffectPath);
            return staffWakeBoostEffectPrefab;
        }

        /// <summary>
        /// 加载并缓存拜访拉客拖尾（jiangnan_tuowei）。
        /// </summary>
        private static GameObject LoadVisitPullTrailEffectPrefab()
        {
            if (visitPullTrailEffectPrefab != null)
            {
                return visitPullTrailEffectPrefab;
            }

            visitPullTrailEffectPrefab = GameplayResourceStore.LoadAsset<GameObject>(VisitPullTrailEffectPath);
            return visitPullTrailEffectPrefab;
        }

        private IEnumerator StopWaiterWakeBoostSmokeAfterDelay(GameObject waiter, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            else
            {
                yield return null;
            }

            waiterWakeBoostSmokeRoutines.Remove(waiter);
            StopWaiterWakeBoostSmoke(waiter);
        }

        private void StopWaiterWakeBoostSmoke(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            if (waiterWakeBoostSmokeRoutines.TryGetValue(waiter, out var routine))
            {
                waiterWakeBoostSmokeRoutines.Remove(waiter);
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
            }

            if (!activeWaiterWakeBoostSmokeEffects.TryGetValue(waiter, out var effect))
            {
                return;
            }

            activeWaiterWakeBoostSmokeEffects.Remove(waiter);
            if (effect == null)
            {
                return;
            }

            foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            Destroy(effect);
        }

        private void StopAllWaiterWakeBoostSmoke()
        {
            var waiters = new List<GameObject>(activeWaiterWakeBoostSmokeEffects.Keys);
            for (var index = 0; index < waiters.Count; index++)
            {
                StopWaiterWakeBoostSmoke(waiters[index]);
            }

            activeWaiterWakeBoostSmokeEffects.Clear();
            waiterWakeBoostSmokeRoutines.Clear();
        }

        /// <summary>
        /// 获取可执行服务动作的小二表现，不存在时按招聘配置创建。
        /// </summary>
        /// <param name="task">待派发任务；用于按技能筛选。</param>
        /// <returns>小二表现对象。</returns>
        private GameObject GetAvailableServiceWaiterVisual(WaiterTask task = null, bool ignoreSkillGate = false)
        {
            var taskKey = task?.TaskKey;
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            GameObject attractingFallback = null;
            for (var index = 0; index < waiters.Length; index++)
            {
                var waiter = waiters[index];
                if (waiter == null
                    || busyWaiters.Contains(waiter)
                    || staffVisualsBeingAnimated.Contains(waiter)
                    || IsWaiterNapping(waiter))
                {
                    continue;
                }

                EnsureWaiterStaffIdBound(waiter);
                if (!ignoreSkillGate && !CanWaiterHandleTask(waiter, taskKey))
                {
                    continue;
                }

                if (IsWaiterAttractFeatureActive()
                    && (attractingWaiters.Contains(waiter) || IsWaiterInAttractFlow(waiter)))
                {
                    if (IsWaiterAttractLockedForWork(waiter))
                    {
                        continue;
                    }

                    attractingFallback ??= waiter;
                    continue;
                }

                EnsureWaiterAnimationReceiver(waiter);
                return waiter;
            }

            if (attractingFallback != null)
            {
                EnsureWaiterAnimationReceiver(attractingFallback);
                return attractingFallback;
            }

            if (waiters.Length > 0)
            {
                return null;
            }

            var hasHomePose = TryGetWaiterHomePose(0, out var homePosition, out var homeRotation, out var homeScale);
            var fallbackWaiterId = ResolvePreferredFloorWaiterStaffId();
            var waiterVisual = GetOrCreateGuideStaffVisual(GuideWaiterVisualKey, StaffRole.Waiter, fallbackWaiterId);
            if (waiterVisual == null)
            {
                return null;
            }

            EnsureWaiterStaffIdBound(waiterVisual);
            if (!ignoreSkillGate && !CanWaiterHandleTask(waiterVisual, taskKey))
            {
                return null;
            }

            EnsureWaiterAnimationReceiver(waiterVisual);
            if (hasHomePose)
            {
                waiterVisual.transform.position = homePosition;
                waiterVisual.transform.rotation = homeRotation;
                waiterVisual.transform.localScale = ResolveGuideStaffVisualScale(GuideWaiterVisualKey, homeScale);
            }

            return waiterVisual;
        }

        private static void EnsureWaiterStaffIdBound(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            var character = waiter.GetComponent<WaiterCharacter>();
            if (character == null || character.StaffId > 0)
            {
                StaffSceneClickUtility.EnsureClickCollider(waiter);
                return;
            }

            var staffId = ResolvePreferredFloorWaiterStaffId();
            if (staffId > 0)
            {
                character.BindStaffId(staffId);
            }

            StaffSceneClickUtility.EnsureClickCollider(waiter);
        }

        private static int ResolveWaiterVisualStaffId(GameObject waiter)
        {
            var character = waiter != null ? waiter.GetComponent<WaiterCharacter>() : null;
            if (character != null && character.StaffId > 0)
            {
                return character.StaffId;
            }

            return ResolvePreferredFloorWaiterStaffId();
        }

        private static bool CanWaiterHandleTask(GameObject waiter, string taskKey)
        {
            if (string.IsNullOrEmpty(taskKey))
            {
                return true;
            }

            if (taskKey is "Clean")
            {
                return true;
            }

            var staffId = ResolveWaiterVisualStaffId(waiter);
            if (staffId <= 0)
            {
                return false;
            }

            var profile = StaffConfigUtility.GetProfile(staffId);
            return profile != null && profile.CanHandleWaiterTaskKey(taskKey);
        }

        /// <summary>
        /// 读取已经存在的小二表现。
        /// </summary>
        /// <returns>小二表现对象。</returns>
        private GameObject GetExistingServiceWaiterVisual()
        {
            if (guideStaffVisuals.TryGetValue(GuideWaiterVisualKey, out var waiter) && waiter != null)
            {
                EnsureWaiterAnimationReceiver(waiter);
                return waiter;
            }

            waiter = GameObject.Find($"{GuideWaiterVisualKey}_GuideVisual");
            EnsureWaiterAnimationReceiver(waiter);
            return waiter;
        }

        /// <summary>
        /// 给小二动画器补充动画事件接收器，防止清扫动画事件无人接收。
        /// </summary>
        /// <param name="waiter">小二表现对象。</param>
        private static void EnsureWaiterAnimationReceiver(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            var animators = waiter.GetComponentsInChildren<Animator>(true);
            for (var index = 0; index < animators.Length; index++)
            {
                var animator = animators[index];
                if (animator == null || animator.GetComponent<WaiterAnimationEventReceiver>() != null)
                {
                    continue;
                }

                animator.gameObject.AddComponent<WaiterAnimationEventReceiver>();
            }
        }

        /// <summary>
        /// 获取小二没有任务时返回的场景标记位。
        /// </summary>
        /// <returns>场景标记位。</returns>
        private static bool TryGetWaiterHomePose(int index, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            // 小二默认站位与雇佣挂点「小二雇佣1~4」一致。
            if (TryResolveGuideWaiterHomePose(index, out position, out rotation, out scale))
            {
                return true;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;
            return false;
        }

        /// <summary>
        /// 让小二回到招聘后站立的原点。
        /// </summary>
        /// <param name="waiter">小二表现对象。</param>
        /// <param name="waiterIndex">小二序号。</param>
        /// <returns>协程迭代器。</returns>
        private IEnumerator ReturnWaiterHome(GameObject waiter, int waiterIndex)
        {
            if (waiter == null || !TryGetWaiterHomePose(Mathf.Max(0, waiterIndex), out var homePosition, out var homeRotation, out var homeScale))
            {
                yield break;
            }

            yield return MoveCharacterAlongNavMesh(waiter.transform, homePosition, GetEffectiveWaiterMoveSpeed(waiter), true);
            if (waiter == null)
            {
                yield break;
            }

            waiter.transform.rotation = homeRotation;
            waiter.transform.localScale = ResolveGuideStaffVisualScale(GuideWaiterVisualKey, homeScale);
            SetAnimatorSpeed(waiter.GetComponentInChildren<Animator>(true), 0f);
        }

        private void StartWaiterHomeReturn(GameObject waiter, int waiterIndex)
        {
            if (waiter == null || busyWaiters.Contains(waiter))
            {
                return;
            }

            var context = GetOrCreateWaiterContext(waiter);
            context.HomeIndex = Mathf.Max(0, waiterIndex);
            context.CurrentTask = null;
            context.TransitionTo(new WaiterReturningHomeState());
        }

        private IEnumerator WaiterHomeReturnRoutine(GameObject waiter, int waiterIndex)
        {
            yield return ReturnWaiterHome(waiter, waiterIndex);

            waiterHomeReturnRoutines.Remove(waiter);
            if (waiter != null)
            {
                SetWaiterServiceState(waiter, WaiterServiceState.Idle);
            }
        }

        private void StopWaiterHomeReturn(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            if (waiterHomeReturnRoutines.TryGetValue(waiter, out var routine))
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }

                waiterHomeReturnRoutines.Remove(waiter);
            }

            if (!waiterContexts.TryGetValue(waiter, out var context) || context.CurrentStateKey != WaiterStateKeys.ReturningHome)
            {
                return;
            }

            StopTrackedWaiterRoutine(waiter);
            context.SetPassiveState(new WaiterIdleState());
        }

        /// <summary>
        /// 记录某个小二已经开始执行独立任务，可选地登记任务关联的桌位编号，避免被其他循环重复派发。
        /// </summary>
        /// <param name="waiter">小二对象。</param>
        /// <param name="routine">任务协程。</param>
        /// <param name="serveTableId">本次任务关联的上菜桌位编号；为空表示不属于上菜任务。</param>
        /// <param name="cleanTableId">本次任务关联的清扫桌位编号；为空表示不属于清扫任务。</param>
        private void StartWaiterTask(GameObject waiter, IEnumerator routine, WaiterServiceState state, int? orderTableId = null, int? serveTableId = null, int? cleanTableId = null)
        {
            if (waiter == null || routine == null || staffVisualsBeingAnimated.Contains(waiter))
            {
                return;
            }

            // 软中断前一任务，避免对同一个小二同时跑多个任务（勿 StopCoroutine）。
            StopWaiterHomeReturn(waiter);
            SoftStopWaiterTaskRoutine(waiter);

            // 先释放旧派发，再写入本次任务的桌位映射，确保派发记录与协程是同一事务
            ReleaseWaiterAssignments(waiter);
            if (orderTableId.HasValue)
            {
                assignedOrderTableIds.Add(orderTableId.Value);
                waiterOrderAssignments[waiter] = orderTableId.Value;
            }

            if (serveTableId.HasValue)
            {
                assignedServeTableIds.Add(serveTableId.Value);
                waiterServeAssignments[waiter] = serveTableId.Value;
                SuppressTableCustomerWaitHud(serveTableId.Value, CustomerWaitHudState.WaitingServe);
            }

            if (cleanTableId.HasValue)
            {
                assignedCleanTableIds.Add(cleanTableId.Value);
                waiterCleanAssignments[waiter] = cleanTableId.Value;
            }

            busyWaiters.Add(waiter);
            SetWaiterServiceState(waiter, state);
            var context = GetOrCreateWaiterContext(waiter);
            var epoch = context != null ? context.BeginNewRoutineEpoch() : 0;
            waiterTaskRoutines[waiter] = StartCoroutine(RunWaiterRoutineGuarded(context, routine, epoch));
        }

        /// <summary>
        /// 清理某个小二的任务占用状态。
        /// </summary>
        /// <param name="waiter">小二对象。</param>
        private void FinishWaiterTask(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            ReleaseWaiterAssignments(waiter);
            waiterTaskRoutines.Remove(waiter);
            busyWaiters.Remove(waiter);
            SetWaiterServiceState(waiter, WaiterServiceState.Idle);
            ResetWaiterServiceAnimation(waiter.GetComponentInChildren<Animator>(true));
        }

        /// <summary>
        /// 释放小二关联的桌位派发记录，让下一个调度循环重新选择目标。
        /// </summary>
        /// <param name="waiter">小二对象。</param>
        private void ReleaseWaiterAssignments(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            GameAudioManager.StopWaiterInterruptibleSounds(waiter);
            ClearWaiterOrderCookProgress(waiter);
            ClearWaiterStealProgress(waiter);
            stoppedWaiterSteals.Remove(waiter);
            stoppedCookPhaseWaiterSteals.Remove(waiter);
            waiterCookStealAssignments.Remove(waiter);
            // 打盹桌绑定由 StartWaiterNapAtTable / Wake / 打烊清理单独管理，
            // 不可在任务收尾里清掉，否则刚触发的偷懒协程会判定失败并退出。

            if (waiterOrderAssignments.TryGetValue(waiter, out var orderTableId))
            {
                assignedOrderTableIds.Remove(orderTableId);
                waiterOrderAssignments.Remove(waiter);
                RestoreWaitingOrderDisplayIfStillWaiting(orderTableId);
            }

            if (waiterServeAssignments.TryGetValue(waiter, out var serveTableId))
            {
                assignedServeTableIds.Remove(serveTableId);
                waiterServeAssignments.Remove(waiter);
                RefreshFoodTableServeBubble();
            }

            if (waiterCheckoutAssignments.TryGetValue(waiter, out var checkoutTableId))
            {
                assignedCheckoutTableIds.Remove(checkoutTableId);
                waiterCheckoutAssignments.Remove(waiter);
                RestoreCheckoutDisplayIfStillWaiting(checkoutTableId);
            }

            if (waiterCleanAssignments.TryGetValue(waiter, out var cleanTableId))
            {
                assignedCleanTableIds.Remove(cleanTableId);
                waiterCleanAssignments.Remove(waiter);
            }
        }

        /// <summary>
        /// 为指定小二创建或获取运行时上下文。
        /// </summary>
        internal WaiterCharacter GetOrCreateWaiterContext(GameObject waiter)
        {
            if (waiter == null)
            {
                return null;
            }

            if (waiterContexts.TryGetValue(waiter, out var context) && context != null)
            {
                return context;
            }

            context = waiter.GetComponent<WaiterCharacter>();
            if (context == null)
            {
                Debug.LogWarning($"Waiter visual missing {nameof(WaiterCharacter)}: {waiter.name}");
                return null;
            }

            ClearStaffHeadOrderBubbleNodes(waiter);
            context.InitializeWaiter(this, this);
            waiterContexts[waiter] = context;
            return context;
        }

        /// <summary>
        /// 新状态机入口：登记任务桌位并启动首个状态。
        /// </summary>
        internal bool TryStartWaiterTask(GameObject waiter, WaiterTask task, ICharacterState<WaiterCharacter> initialState)
        {
            if (waiter == null || task == null || initialState == null || staffVisualsBeingAnimated.Contains(waiter))
            {
                return false;
            }

            if (IsWaiterAttractLockedForWork(waiter))
            {
                return false;
            }

            ClearStaffHeadOrderBubbleNodes(waiter);
            StopWaiterHomeReturn(waiter);
            StopWaiterAttract(waiter);
            SoftStopWaiterTaskRoutine(waiter);
            waitersSuppressHomeReturn.Remove(waiter);

            ReleaseWaiterAssignments(waiter);
            switch (task)
            {
                case WaiterOrderTask orderTask:
                    assignedOrderTableIds.Add(task.TableId);
                    waiterOrderAssignments[waiter] = task.TableId;
                    HideWaitingOrderDisplayForTable(orderTask.TableId);
                    ClearWaitingOrderBubbleFlow(orderTask.TableId);
                    SuppressTableCustomerWaitHud(orderTask.TableId, CustomerWaitHudState.WaitingOrder);
                    break;
                case WaiterServeTask serveTask:
                    assignedServeTableIds.Add(task.TableId);
                    waiterServeAssignments[waiter] = task.TableId;
                    SuppressTableCustomerWaitHud(serveTask.TableId, CustomerWaitHudState.WaitingServe);
                    RefreshFoodTableServeBubble();
                    break;
                case WaiterCheckoutTask checkoutTask:
                    assignedCheckoutTableIds.Add(task.TableId);
                    waiterCheckoutAssignments[waiter] = task.TableId;
                    SuppressTableCustomerWaitHud(checkoutTask.TableId, CustomerWaitHudState.WaitingCheckout);
                    break;
                case WaiterCleanTask:
                    assignedCleanTableIds.Add(task.TableId);
                    waiterCleanAssignments[waiter] = task.TableId;
                    break;
            }

            EnsureWaiterAnimationReceiver(waiter);
            busyWaiters.Add(waiter);
            var context = GetOrCreateWaiterContext(waiter);
            if (context == null)
            {
                busyWaiters.Remove(waiter);
                ReleaseWaiterAssignments(waiter);
                return false;
            }

            context.HomeIndex = ResolveWaiterHomeIndex(waiter);
            ClearWaiterCarryPlate(waiter);
            context.PendingDishPrefab = null;
            context.CurrentTask = task;
            context.TransitionTo(initialState);
            return true;
        }

        /// <summary>
        /// 启动店小二当前状态对应的协程。
        /// </summary>
        internal void StartWaiterStateRoutine(WaiterCharacter context, IEnumerator routine)
        {
            if (context == null || context.gameObject == null || routine == null)
            {
                return;
            }

            // 新 epoch 使旧守卫协程在下一 yield 后自行退出，勿 StopCoroutine。
            var epoch = context.BeginNewRoutineEpoch();
            waiterTaskRoutines[context.gameObject] = StartCoroutine(
                RunWaiterRoutineGuarded(context, routine, epoch));
        }

        /// <summary>
        /// 外部打断当前店小二状态协程（软取消：递增 epoch + 清登记）。
        /// </summary>
        public void StopTrackedWaiterRoutine(GameObject waiter)
        {
            SoftStopWaiterTaskRoutine(waiter);
        }

        /// <summary>
        /// 软停止小二任务协程：不调用 StopCoroutine，避免 WaitForSeconds 上 continue failure。
        /// </summary>
        private void SoftStopWaiterTaskRoutine(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            if (waiterContexts.TryGetValue(waiter, out var context) && context != null)
            {
                context.BeginNewRoutineEpoch();
            }

            waiterTaskRoutines.Remove(waiter);
        }

        /// <summary>
        /// 带 epoch 守卫的小二协程：代数变化后在 yield 边界退出。
        /// </summary>
        private IEnumerator RunWaiterRoutineGuarded(WaiterCharacter context, IEnumerator routine, int epoch)
        {
            if (routine == null)
            {
                yield break;
            }

            while (context != null && context.IsRoutineEpochCurrent(epoch) && routine.MoveNext())
            {
                yield return routine.Current;
                if (context == null || !context.IsRoutineEpochCurrent(epoch))
                {
                    yield break;
                }
            }
        }

        /// <summary>
        /// 状态链完成后的统一收尾。
        /// </summary>
        internal void CompleteWaiterTask(WaiterCharacter context)
        {
            if (context == null || context.gameObject == null)
            {
                return;
            }

            var waiter = context.gameObject;
            // 已进入/正在前往偷懒：保留打盹协程与 busy，只清任务指针。
            if (IsWaiterTransitioningToNap(waiter) || IsWaiterNapping(waiter))
            {
                ReleaseWaiterTaskAssignmentsOnly(waiter);
                context.CurrentTask = null;
                context.PendingDishPrefab = null;
                ClearWaiterCarryPlate(waiter);
                return;
            }

            waiterTaskRoutines.Remove(waiter);
            ReleaseWaiterAssignments(waiter);
            busyWaiters.Remove(waiter);
            ClearWaiterCarryPlate(waiter);
            context.PendingDishPrefab = null;
            context.CurrentTask = null;
            ResetWaiterServiceAnimation(waiter.GetComponentInChildren<Animator>(true));
            context.SetPassiveState(new WaiterIdleState());
            RefreshWaiterStateHud(waiter);
        }

        /// <summary>
        /// 结账任务目标桌已变清理：把同一小二无缝切到清扫，避免停住/遣回站位。
        /// </summary>
        internal bool TryContinueCheckoutWaiterAsClean(WaiterCharacter context)
        {
            if (context == null || context.gameObject == null)
            {
                return false;
            }

            if (context.CurrentTask is not WaiterCheckoutTask checkoutTask)
            {
                return false;
            }

            var tableId = checkoutTask.TableId;
            if (!IsTableInState(tableId, TavernTableRuntimeState.Cleaning)
                || assignedCleanTableIds.Contains(tableId)
                || IsTableUpgrading(tableId))
            {
                return false;
            }

            var waiter = context.gameObject;
            assignedCheckoutTableIds.Remove(tableId);
            if (waiterCheckoutAssignments.TryGetValue(waiter, out var assignedId) && assignedId == tableId)
            {
                waiterCheckoutAssignments.Remove(waiter);
            }

            assignedCleanTableIds.Add(tableId);
            waiterCleanAssignments[waiter] = tableId;
            context.CurrentTask = new WaiterCleanTask(tableId);
            waitersSuppressHomeReturn.Remove(waiter);

            // 已在桌边读条/偷钱：直接清扫；赶路中：继续走向该桌再清。
            var atTable = context.CurrentStateKey == WaiterStateKeys.Checkouting
                          || context.CurrentStateKey == WaiterStateKeys.Stealing;
            context.TransitionTo(atTable
                ? new WaiterCleaningState()
                : new WaiterMoveToCleanTableState());
            return true;
        }

        /// <summary>
        /// 结账目标已失效且无法接清扫：释放任务后原地 Idle，不遣回默认站位。
        /// </summary>
        internal void CompleteWaiterCheckoutCancelledStayInPlace(WaiterCharacter context)
        {
            if (context == null || context.gameObject == null)
            {
                return;
            }

            var waiter = context.gameObject;
            CompleteWaiterTask(context);
            if (waiter != null)
            {
                waitersSuppressHomeReturn.Add(waiter);
                SetAnimatorSpeed(waiter.GetComponentInChildren<Animator>(true), 0f);
            }
        }

        /// <summary>
        /// 只释放点单/上菜/结账/清扫派发，不影响打盹桌绑定。
        /// </summary>
        private void ReleaseWaiterTaskAssignmentsOnly(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            if (waiterOrderAssignments.TryGetValue(waiter, out var orderTableId))
            {
                assignedOrderTableIds.Remove(orderTableId);
                waiterOrderAssignments.Remove(waiter);
                RestoreWaitingOrderDisplayIfStillWaiting(orderTableId);
            }

            if (waiterServeAssignments.TryGetValue(waiter, out var serveTableId))
            {
                assignedServeTableIds.Remove(serveTableId);
                waiterServeAssignments.Remove(waiter);
                RefreshFoodTableServeBubble();
            }

            if (waiterCheckoutAssignments.TryGetValue(waiter, out var checkoutTableId))
            {
                assignedCheckoutTableIds.Remove(checkoutTableId);
                waiterCheckoutAssignments.Remove(waiter);
                RestoreCheckoutDisplayIfStillWaiting(checkoutTableId);
            }

            if (waiterCleanAssignments.TryGetValue(waiter, out var cleanTableId))
            {
                assignedCleanTableIds.Remove(cleanTableId);
                waiterCleanAssignments.Remove(waiter);
            }
        }

        /// <summary>
        /// 按 StaffId 解雇场景中的小二：中断任务、释放桌位并销毁对应 visual。
        /// </summary>
        public void DismissWaiterByStaffId(int staffId)
        {
            if (staffId <= 0)
            {
                return;
            }

            var group = GetGuideStaffVisualGroup(GuideWaiterVisualKey);
            GameObject target = null;
            for (var index = 0; index < group.Count; index++)
            {
                var visual = group[index];
                if (visual == null)
                {
                    continue;
                }

                var character = visual.GetComponent<WaiterCharacter>();
                if (character != null && character.StaffId == staffId)
                {
                    target = visual;
                    break;
                }
            }

            if (target == null)
            {
                return;
            }

            ForceDismissWaiterVisual(target);
        }

        private void ForceDismissWaiterVisual(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            StopWaiterAttract(waiter);
            StopTrackedWaiterRoutine(waiter);
            ReleaseWaiterAssignments(waiter);
            waiterTaskRoutines.Remove(waiter);
            busyWaiters.Remove(waiter);
            ClearWaiterCarryPlate(waiter);

            if (waiterContexts.TryGetValue(waiter, out var context) && context != null)
            {
                context.PendingDishPrefab = null;
                context.CurrentTask = null;
                context.SetPassiveState(new WaiterIdleState());
                waiterContexts.Remove(waiter);
            }

            ResetWaiterServiceAnimation(waiter.GetComponentInChildren<Animator>(true));
            staffVisualsBeingAnimated.Remove(waiter);

            var group = GetGuideStaffVisualGroup(GuideWaiterVisualKey);
            group.Remove(waiter);
            if (guideStaffVisuals.TryGetValue(GuideWaiterVisualKey, out var primary) && primary == waiter)
            {
                guideStaffVisuals[GuideWaiterVisualKey] = group.Count > 0 ? group[0] : null;
            }

            Destroy(waiter);
        }

        /// <summary>
        /// 根据状态键刷新小二的逻辑状态和持久图标。
        /// </summary>
        internal void ApplyWaiterPresentation(GameObject waiter, string stateKey)
        {
            if (waiter == null)
            {
                return;
            }

            var state = stateKey switch
            {
                WaiterStateKeys.Idle or WaiterStateKeys.ReturningHome => WaiterServiceState.Idle,
                WaiterStateKeys.MoveToAttractPoint or WaiterStateKeys.Attracting => WaiterServiceState.Attracting,
                WaiterStateKeys.MoveToTableForOrder or WaiterStateKeys.Ordering => WaiterServiceState.Ordering,
                WaiterStateKeys.MoveToNotifyChef or WaiterStateKeys.MoveToPickupDish => WaiterServiceState.NotifyChef,
                WaiterStateKeys.CookStealing => WaiterServiceState.CookStealing,
                WaiterStateKeys.MoveToServeTable or WaiterStateKeys.Serving => WaiterServiceState.Serving,
                WaiterStateKeys.MoveToTableForCheckout or WaiterStateKeys.Checkouting => WaiterServiceState.Checkout,
                WaiterStateKeys.Stealing => WaiterServiceState.Stealing,
                WaiterStateKeys.MoveToCleanTable or WaiterStateKeys.Cleaning => WaiterServiceState.Cleaning,
                WaiterStateKeys.Napping => WaiterServiceState.Napping,
                _ => WaiterServiceState.Idle
            };

            SetWaiterServiceState(waiter, state);
        }

        internal int ResolveWaiterHomeIndex(GameObject waiter)
        {
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            for (var index = 0; index < waiters.Length; index++)
            {
                if (waiters[index] == waiter)
                {
                    return index;
                }
            }

            return 0;
        }

        private bool TryReservePreparedDishForServe()
        {
            if (DataManager.Instance?.TavernData == null)
            {
                return false;
            }

            var freeDishCount = Mathf.Max(0, DataManager.Instance.TavernData.availableDishes - reservedServeDishCount);
            if (freeDishCount <= 0)
            {
                return false;
            }

            reservedServeDishCount++;
            return true;
        }

        /// <summary>
        /// 全量清空小二任务队列与派发缓存，通常在打烊或场景重置时调用。
        /// </summary>
        private void TransitionWaiterOrderAssignmentToServe(GameObject waiter, int tableId)
        {
            if (waiter == null)
            {
                return;
            }

            if (waiterOrderAssignments.TryGetValue(waiter, out var orderTableId))
            {
                assignedOrderTableIds.Remove(orderTableId);
                waiterOrderAssignments.Remove(waiter);
            }

            assignedServeTableIds.Add(tableId);
            waiterServeAssignments[waiter] = tableId;
            SuppressTableCustomerWaitHud(tableId, CustomerWaitHudState.WaitingServe);
            RefreshFoodTableServeBubble();
        }

        /// <summary>
        /// 鍏ㄩ噺娓呯┖灏忎簩浠诲姟闃熷垪涓庢淳鍙戠紦瀛橈紝閫氬父鍦ㄦ墦鐑婃垨鍦烘櫙閲嶇疆鏃惰皟鐢ㄣ€?        /// </summary>
        private void ResetWaiterTaskState()
        {
            var waiterSnapshot = new List<GameObject>(waiterTaskRoutines.Keys);
            for (var index = 0; index < waiterSnapshot.Count; index++)
            {
                SoftStopWaiterTaskRoutine(waiterSnapshot[index]);
            }

            foreach (var pair in waiterContexts)
            {
                pair.Value?.BeginNewRoutineEpoch();
            }

            foreach (var pair in waiterHomeReturnRoutines)
            {
                if (pair.Value != null)
                {
                    StopCoroutine(pair.Value);
                }
            }

            waiterTaskRoutines.Clear();
            waiterHomeReturnRoutines.Clear();
            busyWaiters.Clear();
            assignedOrderTableIds.Clear();
            assignedServeTableIds.Clear();
            assignedCheckoutTableIds.Clear();
            assignedCleanTableIds.Clear();
            waiterOrderAssignments.Clear();
            waiterServeAssignments.Clear();
            waiterCheckoutAssignments.Clear();
            waiterCleanAssignments.Clear();
            waitersSuppressHomeReturn.Clear();
            waiterNapTableAssignments.Clear();
            tableNapWaiters.Clear();
            waiterServiceStates.Clear();
            nextWaiterNapRollTimes.Clear();
            nextWaiterStealRollTimes.Clear();
            nextCookPhaseWaiterStealRollTimes.Clear();
            waiterCurrentStamina.Clear();
            waiterPassiveStaminaRecoverTimers.Clear();
            stoppedCookPhaseWaiterSteals.Clear();
            waiterCookStealAssignments.Clear();
            ClearAllWaiterOrderCookProgress();
            ClearAllWaiterStealProgress();
            StopAllWaiterWakeRoutines();
            ClearAllWaiterStateIcons();
            StopAllWaiterWakeAnimations();
            ClearCookOrderTickets();
            reservedServeDishCount = 0;
            StopAllWaiterWakeBoostSmoke();

            foreach (var effect in activeCleanSmokeEffects.Values)
            {
                if (effect != null)
                {
                    Destroy(effect);
                }
            }

            activeCleanSmokeEffects.Clear();
        }

        /// <summary>
        /// 打烊收尾时保留已经开始的清理任务，避免清理倒计时和烟雾表现被重置。
        /// </summary>
        private void PrepareWaiterTasksForClosing()
        {
            foreach (var pair in waiterHomeReturnRoutines)
            {
                if (pair.Value != null)
                {
                    StopCoroutine(pair.Value);
                }
            }

            waiterHomeReturnRoutines.Clear();
            WakeAllNappingWaitersForClosing();

            var preservedCleanAssignments = new Dictionary<GameObject, int>();
            foreach (var pair in waiterCleanAssignments)
            {
                var waiter = pair.Key;
                var tableId = pair.Value;
                if (waiter == null
                    || !assignedCleanTableIds.Contains(tableId)
                    || !waiterTaskRoutines.TryGetValue(waiter, out var routine)
                    || routine == null)
                {
                    continue;
                }

                preservedCleanAssignments[waiter] = tableId;
            }

            var stoppedWaiters = new List<GameObject>();
            foreach (var pair in waiterTaskRoutines)
            {
                var waiter = pair.Key;
                if (waiter != null && preservedCleanAssignments.ContainsKey(waiter))
                {
                    continue;
                }

                stoppedWaiters.Add(waiter);
            }

            for (var index = 0; index < stoppedWaiters.Count; index++)
            {
                var waiter = stoppedWaiters[index];
                SoftStopWaiterTaskRoutine(waiter);
                ReleaseWaiterAssignments(waiter);
                busyWaiters.Remove(waiter);
                ClearWaiterStateIcon(waiter);
                ResetWaiterServiceAnimation(waiter != null ? waiter.GetComponentInChildren<Animator>(true) : null);
                SetWaiterServiceState(waiter, WaiterServiceState.Idle);
            }

            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            for (var index = 0; index < waiters.Length; index++)
            {
                var waiter = waiters[index];
                if (waiter == null || preservedCleanAssignments.ContainsKey(waiter))
                {
                    continue;
                }

                busyWaiters.Remove(waiter);
                ClearWaiterStateIcon(waiter);
                ResetWaiterServiceAnimation(waiter.GetComponentInChildren<Animator>(true));
                SetWaiterServiceState(waiter, WaiterServiceState.Idle);
            }

            assignedOrderTableIds.Clear();
            assignedServeTableIds.Clear();
            assignedCheckoutTableIds.Clear();
            waiterOrderAssignments.Clear();
            waiterServeAssignments.Clear();
            waiterCheckoutAssignments.Clear();
            assignedCleanTableIds.Clear();
            waiterCleanAssignments.Clear();
            waitersSuppressHomeReturn.Clear();
            waiterNapTableAssignments.Clear();
            tableNapWaiters.Clear();

            foreach (var pair in preservedCleanAssignments)
            {
                var waiter = pair.Key;
                var tableId = pair.Value;
                assignedCleanTableIds.Add(tableId);
                waiterCleanAssignments[waiter] = tableId;
                busyWaiters.Add(waiter);
                waiterServiceStates[waiter] = WaiterServiceState.Cleaning;
            }

            nextWaiterNapRollTimes.Clear();
            nextWaiterStealRollTimes.Clear();
            nextCookPhaseWaiterStealRollTimes.Clear();
            stoppedCookPhaseWaiterSteals.Clear();
            waiterCookStealAssignments.Clear();
            ClearAllWaiterOrderCookProgress();
            ClearAllWaiterStealProgress();
            StopAllWaiterWakeRoutines();
            ClearAllWaiterStateIcons();
            StopAllWaiterWakeAnimations();
            ClearCookOrderTickets();
            reservedServeDishCount = 0;
            StopAllWaiterWakeBoostSmoke();
        }

        /// <summary>
        /// 打烊或停止营业后统一恢复员工动画，避免清扫、做菜等动作残留到停业展示。
        /// </summary>
        private void ResetAllGuideStaffServiceAnimations()
        {
            StopAllWaiterWakeRoutines();
            StopAllWaiterWakeAnimations();
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            for (var index = 0; index < waiters.Length; index++)
            {
                var waiter = waiters[index];
                if (waiter == null)
                {
                    continue;
                }

                SetWaiterServiceState(waiter, WaiterServiceState.Idle);
                ResetWaiterNapCooldown(waiter);
                ResetWaiterServiceAnimation(waiter.GetComponentInChildren<Animator>(true));
                EnsureWaiterStaminaInitialized(waiter, true);
            }

            var chefs = GetGuideStaffVisuals(GuideChefVisualKey);
            for (var index = 0; index < chefs.Length; index++)
            {
                var chef = chefs[index];
                if (chef == null)
                {
                    continue;
                }

                ResetChefCookAnimationInternal(chef.GetComponentInChildren<Animator>(true));
            }

            if (!guideStaffVisuals.TryGetValue(GuideShopkeeperVisualKey, out var shopkeeper) || shopkeeper == null)
            {
                return;
            }

            ResetWaiterServiceAnimation(shopkeeper.GetComponentInChildren<Animator>(true));
        }

        bool IWaiterRuntimeHost.IsBusinessOpen => DataManager.Instance != null
                                                  && DataManager.Instance.TavernData != null
                                                  && DataManager.Instance.TavernData.isOpen;

        GameObject IWaiterRuntimeHost.GetAvailableWaiterForTask(WaiterTask task, bool ignoreSkillGate)
        {
            return GetAvailableServiceWaiterVisual(task, ignoreSkillGate);
        }

        bool IWaiterRuntimeHost.TryStartWaiterTask(GameObject waiter, WaiterTask task, ICharacterState<WaiterCharacter> initialState)
        {
            return TryStartWaiterTask(waiter, task, initialState);
        }

        WaiterCharacter IWaiterRuntimeHost.GetOrCreateWaiterContext(GameObject waiter)
        {
            return GetOrCreateWaiterContext(waiter);
        }

        void IWaiterRuntimeHost.StartWaiterStateRoutine(WaiterCharacter context, IEnumerator routine)
        {
            StartWaiterStateRoutine(context, routine);
        }

        void IWaiterRuntimeHost.StopTrackedWaiterRoutine(GameObject waiter)
        {
            StopTrackedWaiterRoutine(waiter);
        }

        void IWaiterRuntimeHost.ReleaseTrackedWaiterRoutineReference(GameObject waiter)
        {
            if (waiter != null)
            {
                waiterTaskRoutines.Remove(waiter);
            }
        }

        void IWaiterRuntimeHost.CompleteWaiterTask(WaiterCharacter context)
        {
            CompleteWaiterTask(context);
        }

        void IWaiterRuntimeHost.CompleteWaiterCheckoutCancelledStayInPlace(WaiterCharacter context)
        {
            CompleteWaiterCheckoutCancelledStayInPlace(context);
        }

        bool IWaiterRuntimeHost.TryContinueCheckoutWaiterAsClean(WaiterCharacter context)
        {
            return TryContinueCheckoutWaiterAsClean(context);
        }

        void IWaiterRuntimeHost.ApplyWaiterPresentation(GameObject waiter, string stateKey)
        {
            ApplyWaiterPresentation(waiter, stateKey);
        }

        void IWaiterRuntimeHost.EnsureWaiterAnimationReceiver(GameObject waiter)
        {
            EnsureWaiterAnimationReceiver(waiter);
        }

        bool IWaiterRuntimeHost.TryGetTable(int tableId, out TableArea table)
        {
            return AllTables.TryGetValue(tableId, out table);
        }

        private bool IsTableInState(int tableId, TavernTableRuntimeState state)
        {
            var tableData = DataManager.Instance != null
                ? DataManager.Instance.GetTableData(tableId)
                : null;
            return tableData != null && (TavernTableRuntimeState)tableData.runtimeState == state;
        }

        bool IWaiterRuntimeHost.IsTableInState(int tableId, TavernTableRuntimeState state)
        {
            return IsTableInState(tableId, state);
        }

        IEnumerator IWaiterRuntimeHost.MoveWaiterToTable(GameObject waiter, TableArea table)
        {
            return MoveWaiterToTable(waiter, table);
        }

        IEnumerator IWaiterRuntimeHost.MoveWaiterToCounter(GameObject waiter)
        {
            return MoveWaiterToCounter(waiter);
        }

        IEnumerator IWaiterRuntimeHost.MoveWaiterToDishPickup(GameObject waiter)
        {
            return MoveWaiterToDishPickup(waiter);
        }

        IEnumerator IWaiterRuntimeHost.ReturnWaiterHome(GameObject waiter, int waiterIndex)
        {
            return ReturnWaiterHome(waiter, waiterIndex);
        }

        int IWaiterRuntimeHost.ResolveWaiterHomeIndex(GameObject waiter)
        {
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            for (var index = 0; index < waiters.Length; index++)
            {
                if (waiters[index] == waiter)
                {
                    return index;
                }
            }

            return 0;
        }

        Animator IWaiterRuntimeHost.GetWaiterAnimator(GameObject waiter)
        {
            return waiter != null ? waiter.GetComponentInChildren<Animator>(true) : null;
        }

        void IWaiterRuntimeHost.SetWaiterAnimatorSpeed(Animator animator, float speed)
        {
            SetAnimatorSpeed(animator, speed);
        }

        void IWaiterRuntimeHost.ResetWaiterAnimation(Animator animator)
        {
            ResetWaiterServiceAnimation(animator);
        }

        void IWaiterRuntimeHost.TriggerWaiterCleanAnimation(Animator animator)
        {
            TriggerAnimator(animator, WaiterCleanTrigger);
        }

        void IWaiterRuntimeHost.ConsumeWaiterStamina(GameObject waiter, WaiterStaminaAction action)
        {
            ConsumeWaiterStamina(waiter, action);
        }

        float IWaiterRuntimeHost.GetEffectiveWaiterOrderDuration(GameObject waiter)
        {
            return GetEffectiveWaiterOrderDuration(waiter);
        }

        float IWaiterRuntimeHost.GetEffectiveWaiterCheckoutDuration(GameObject waiter)
        {
            return GetEffectiveWaiterCheckoutDuration(waiter);
        }

        float IWaiterRuntimeHost.GetEffectiveWaiterServeDuration(GameObject waiter)
        {
            return GetEffectiveWaiterServeDuration(waiter);
        }

        float IWaiterRuntimeHost.GetEffectiveWaiterStealDuration()
        {
            return GetEffectiveWaiterStealDuration();
        }

        float IWaiterRuntimeHost.GetEffectiveAutoCleanDuration(GameObject waiter)
        {
            return GetEffectiveAutoCleanDuration(waiter);
        }

        void IWaiterRuntimeHost.HideWaitingOrderDisplay(TableArea table)
        {
            table?.linkedUI?.HideWaitingOrderDisplay();
        }

        void IWaiterRuntimeHost.HideCheckoutDisplay(TableArea table)
        {
            table?.linkedUI?.HideCheckoutDisplay("结账中");
        }

        private void HideWaitingOrderDisplayForTable(int tableId)
        {
            if (tableId <= 0 || !AllTables.TryGetValue(tableId, out var table))
            {
                return;
            }

            table.linkedUI?.HideWaitingOrderDisplay();
        }

        private void HideCheckoutDisplayForTable(int tableId)
        {
            if (tableId <= 0 || !AllTables.TryGetValue(tableId, out var table))
            {
                return;
            }

            table.linkedUI?.HideCheckoutDisplay("结账中");
        }

        /// <summary>
        /// 是否已有小二代为点单（含赶来途中）。
        /// </summary>
        public bool IsWaiterAssignedToOrderTable(int tableId)
        {
            return tableId > 0 && assignedOrderTableIds.Contains(tableId);
        }

        /// <summary>
        /// 已派单的小二正在赶来时，隐藏桌上/头顶等待气泡，避免点击后仍显示。
        /// </summary>
        public bool ShouldSuppressCustomerWaitHud(int tableId, CustomerWaitHudState state)
        {
            if (tableId <= 0)
            {
                return false;
            }

            return state switch
            {
                CustomerWaitHudState.WaitingOrder => assignedOrderTableIds.Contains(tableId),
                CustomerWaitHudState.WaitingServe => assignedServeTableIds.Contains(tableId),
                CustomerWaitHudState.WaitingCheckout => assignedCheckoutTableIds.Contains(tableId),
                _ => false
            };
        }

        void IWaiterRuntimeHost.SealCustomerWaitOnWaiterArrival(int tableId, CustomerWaitHudState waitState)
        {
            waitSatisfactionTracker.SealActiveWait(tableId, waitState);
        }

        private void RestoreWaitingOrderDisplayIfStillWaiting(int tableId)
        {
            if (tableId <= 0
                || DataManager.Instance == null
                || !AllTables.TryGetValue(tableId, out var table))
            {
                return;
            }

            var tableData = DataManager.Instance.GetTableData(tableId);
            if (tableData == null
                || (TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.WaitingOrder)
            {
                return;
            }

            if (TableHasVipCustomer(tableId) && VipGuestDishGuessService.ShouldAutoDispatchOrder(tableId))
            {
                return;
            }

            table.linkedUI?.RestoreWaitingOrderDisplay();
        }

        private void RestoreCheckoutDisplayIfStillWaiting(int tableId)
        {
            if (tableId <= 0
                || DataManager.Instance == null
                || !AllTables.TryGetValue(tableId, out var table))
            {
                return;
            }

            var tableData = DataManager.Instance.GetTableData(tableId);
            if (tableData == null
                || (TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.Checkout)
            {
                return;
            }

            table.linkedUI?.RestoreCheckoutDisplay();
        }

        void IWaiterRuntimeHost.ShowWaiterTaskProgress(GameObject waiter, float duration, Sprite icon)
        {
            // 玩法调整：关闭小二头顶任务进度/点单气泡。
            ClearStaffHeadOrderBubbleNodes(waiter);
        }

        GameObject IWaiterRuntimeHost.ShowWaiterClickableTaskProgress(GameObject waiter, float duration, Sprite icon, System.Action onClick)
        {
            return ShowWaiterStealProgress(waiter, duration, icon, onClick);
        }

        Sprite IWaiterRuntimeHost.ResolveWaiterOrderingIcon()
        {
            return waiterOrderingIcon;
        }

        Sprite IWaiterRuntimeHost.ResolveWaiterServingIcon()
        {
            return waiterServingIcon;
        }

        Sprite IWaiterRuntimeHost.ResolveWaiterCheckoutIcon(int tableId)
        {
            return ResolveWaiterCheckoutIcon(tableId);
        }

        Sprite IWaiterRuntimeHost.ResolveWaiterStealingIcon()
        {
            return ResolveWaiterStealingIcon();
        }

        Sprite IWaiterRuntimeHost.ResolveWaiterNotifyChefIcon()
        {
            return waiterNotifyChefIcon != null ? waiterNotifyChefIcon : ResolveDefaultOrderIcon();
        }

        Sprite IWaiterRuntimeHost.ResolveWaiterCleaningIcon()
        {
            return waiterCleaningIcon;
        }

        bool IWaiterRuntimeHost.ShouldWaiterStealBeforeCheckout(GameObject waiter, int tableId)
        {
            return ShouldWaiterStealBeforeCheckout(waiter, tableId);
        }

        bool IWaiterRuntimeHost.HasWaiterStealBeenStopped(GameObject waiter)
        {
            return HasWaiterStealBeenStopped(waiter);
        }

        void IWaiterRuntimeHost.NotifyWaiterStealStopped(GameObject waiter)
        {
            NotifyWaiterStealStopped(waiter);
        }

        void IWaiterRuntimeHost.ClearWaiterStealProgress(GameObject waiter)
        {
            ClearWaiterStealProgress(waiter);
        }

        void IWaiterRuntimeHost.ResetWaiterStealCooldown(GameObject waiter)
        {
            ResetWaiterStealCooldown(waiter);
        }

        void IWaiterRuntimeHost.MarkTableCheckoutInProgress(TableArea table, string customText)
        {
            if (table != null)
            {
                SetCheckoutRuntimeTextOverride(table.GetTableIdFromInternal(), customText);
            }

            table?.RefreshRuntimeState(TavernTableRuntimeState.Checkout, customText);
        }

        void IWaiterRuntimeHost.CompleteCheckoutWithIncome(int tableId, GameObject waiter)
        {
            CompleteCheckoutWithIncome(tableId, waiter);
        }

        void IWaiterRuntimeHost.CompleteCheckoutWithoutIncome(int tableId)
        {
            CompleteCheckoutWithoutIncome(tableId);
        }

        private bool IsWaiterTransitioningToNap(GameObject waiter)
        {
            return waiter != null && waiterNapTableAssignments.ContainsKey(waiter);
        }

        bool IWaiterRuntimeHost.IsWaiterTransitioningToNap(GameObject waiter)
        {
            return IsWaiterTransitioningToNap(waiter);
        }

        bool IWaiterRuntimeHost.IsWaiterNapping(GameObject waiter)
        {
            return IsWaiterNapping(waiter);
        }

        bool IWaiterRuntimeHost.TryStartWaiterNapAfterCleaning(int tableId, GameObject preferredWaiter)
        {
            return TryStartWaiterNapAfterCleaning(tableId, preferredWaiter);
        }

        void IWaiterRuntimeHost.CompleteTableOrderByWaiter(int tableId, TableArea table, Sprite orderIcon)
        {
            CompleteTableOrderByWaiter(tableId, table, orderIcon);
        }

        void IWaiterRuntimeHost.NotifyChefCookOrderTicket(int tableId)
        {
            NotifyChefCookOrderTicket(tableId);
        }

        bool IWaiterRuntimeHost.ShouldWaiterStealWhileCooking(GameObject waiter, int tableId)
        {
            return ShouldWaiterStealWhileCooking(waiter, tableId);
        }

        void IWaiterRuntimeHost.ShowWaiterOrderCookProgress(GameObject waiter, int tableId, Sprite icon)
        {
            ShowWaiterOrderCookProgress(waiter, tableId, icon);
        }

        void IWaiterRuntimeHost.ShowWaiterCookStealingProgress(GameObject waiter, int tableId)
        {
            ShowWaiterCookStealingProgress(waiter, tableId);
        }

        void IWaiterRuntimeHost.ClearWaiterOrderCookProgress(GameObject waiter)
        {
            ClearWaiterOrderCookProgress(waiter);
        }

        bool IWaiterRuntimeHost.HasWaiterCookStealBeenStopped(GameObject waiter)
        {
            return HasWaiterCookStealBeenStopped(waiter);
        }

        void IWaiterRuntimeHost.NotifyWaiterCookStealStopped(GameObject waiter)
        {
            NotifyWaiterCookStealStopped(waiter);
        }

        bool IWaiterRuntimeHost.HasAvailablePreparedDishForServe(int tableId)
        {
            return HasAvailablePreparedDishForServe(tableId);
        }

        void IWaiterRuntimeHost.ReleaseReservedServeDish()
        {
            ReleaseReservedServeDish();
        }

        GameObject IWaiterRuntimeHost.TakePreparedDishPrefab()
        {
            return TakePreparedDishPrefab();
        }

        void IWaiterRuntimeHost.ReturnPreparedDishPrefab(GameObject dishPrefab)
        {
            ReturnPreparedDishPrefab(dishPrefab);
        }

        GameObject IWaiterRuntimeHost.TakePreparedDishForWaiter(GameObject waiter)
        {
            return TakePreparedDishForWaiter(waiter);
        }

        void IWaiterRuntimeHost.ReturnWaiterCarryDish(GameObject waiter, GameObject dishPrefab)
        {
            ReturnWaiterCarryDish(waiter, dishPrefab);
        }

        void IWaiterRuntimeHost.ClearWaiterCarryPlate(GameObject waiter)
        {
            ClearWaiterCarryPlate(waiter);
        }

        void IWaiterRuntimeHost.TransitionWaiterOrderAssignmentToServe(GameObject waiter, int tableId)
        {
            TransitionWaiterOrderAssignmentToServe(waiter, tableId);
        }

        void IWaiterRuntimeHost.ServeTableByWaiter(int tableId, TableArea table, GameObject dishPrefab)
        {
            ServeTableByWaiter(tableId, table, dishPrefab);
        }

        void IWaiterRuntimeHost.MarkTableCleaningInProgress(TableArea table)
        {
            table?.RefreshRuntimeState(TavernTableRuntimeState.Cleaning, "清理中");
        }

        GameObject IWaiterRuntimeHost.PlayCleanSmokeEffect(int tableId, TableArea table)
        {
            return PlayCleanSmokeEffect(tableId, table);
        }

        void IWaiterRuntimeHost.StopCleanSmokeEffect(int tableId, GameObject smokeEffect)
        {
            StopCleanSmokeEffect(tableId, smokeEffect);
        }

        void IWaiterRuntimeHost.FinishCleaning(int tableId)
        {
            FinishCleaning(tableId);
        }

        void IWaiterRuntimeHost.PlayCleanAudio(int tableId)
        {
            GameAudioManager.PlayWiping(tableId);
        }

        void IWaiterRuntimeHost.StopCleanAudio(int tableId)
        {
            GameAudioManager.StopWiping(tableId);
        }

        /// <summary>
        /// 记录小二当前服务状态。
        /// </summary>
        /// <param name="waiter">小二对象。</param>
        /// <param name="state">服务状态。</param>
        private void SetWaiterServiceState(GameObject waiter, WaiterServiceState state)
        {
            if (waiter == null)
            {
                return;
            }

            waiterServiceStates[waiter] = state;
            RefreshWaiterStateHud(waiter);
        }

        private bool CanShowWaiterStateIcon(WaiterServiceState state)
        {
            // 仅偷懒（打盹）显示头顶图标，供玩家点击叫醒；体力条不显示。
            return state == WaiterServiceState.Napping;
        }

        /// <summary>
        /// 清理员工根节点下残留的点单气泡（OrderBubble_0 等）。
        /// </summary>
        private static void ClearStaffHeadOrderBubbleNodes(GameObject staffRoot)
        {
            if (staffRoot == null)
            {
                return;
            }

            var staffTransform = staffRoot.transform;
            for (var index = staffTransform.childCount - 1; index >= 0; index--)
            {
                var child = staffTransform.GetChild(index);
                if (child != null && child.name.StartsWith("OrderBubble_"))
                {
                    Destroy(child.gameObject);
                }
            }
        }

        /// <summary>
        /// 当前是否还有未被预占的成品菜可以派给小二。
        /// 无 FoodTable 视觉队列时，只要有可用份数且能随机到菜品预制体即可上菜。
        /// </summary>
        private bool HasAvailablePreparedDishForServe(int tableId = 0)
        {
            if (tableId > 0)
            {
                if (!tableCookOrderTickets.TryGetValue(tableId, out var ticket)
                    || ticket == null
                    || !ticket.isCompleted)
                {
                    return false;
                }
            }

            if (DataManager.Instance?.TavernData == null)
            {
                return false;
            }

            var freeDishCount = Mathf.Max(0, DataManager.Instance.TavernData.availableDishes - reservedServeDishCount);
            if (freeDishCount <= 0)
            {
                return false;
            }

            return GetPreparedDishQueueCount() > 0 || GetRandomDishPrefab() != null;
        }

        /// <summary>
        /// 释放一份已预占但尚未真正上桌的菜品名额。
        /// </summary>
        private void ReleaseReservedServeDish()
        {
            reservedServeDishCount = Mathf.Max(0, reservedServeDishCount - 1);
        }

        /// <summary>
        /// 在桌子四角选择离小二最近、可寻路的服务点（点单/上菜/结账/清扫）。
        /// </summary>
        private static Vector3 ResolveTableServicePosition(TableArea table, Vector3 fromPosition)
        {
            if (table == null)
            {
                return fromPosition;
            }

            var tableXf = table.tableObj != null && table.tableObj.activeInHierarchy
                ? table.tableObj.transform
                : table.transform;
            var center = tableXf.position;
            TryGetNavMeshPosition(fromPosition, out fromPosition);

            var forward = tableXf.forward;
            var right = tableXf.right;
            forward.y = 0f;
            right.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }

            if (right.sqrMagnitude < 0.01f)
            {
                right = Vector3.right;
            }

            forward.Normalize();
            right.Normalize();

            ResolveTableHalfExtents(table, out var halfRight, out var halfForward);
            var outward = TableServiceOutwardPadding;
            var cornerOffsets = new[]
            {
                forward * (halfForward + outward) + right * (halfRight + outward),
                forward * (halfForward + outward) - right * (halfRight + outward),
                -forward * (halfForward + outward) + right * (halfRight + outward),
                -forward * (halfForward + outward) - right * (halfRight + outward)
            };

            var bestPosition = Vector3.zero;
            var bestDistance = float.MaxValue;
            var found = false;

            for (var scaleIndex = 0; scaleIndex < TableServiceCornerScales.Length; scaleIndex++)
            {
                var scale = TableServiceCornerScales[scaleIndex];
                for (var cornerIndex = 0; cornerIndex < cornerOffsets.Length; cornerIndex++)
                {
                    var candidate = center + cornerOffsets[cornerIndex] * scale;
                    if (!TryGetNavMeshPosition(candidate, out var navMeshPosition))
                    {
                        continue;
                    }

                    if (IsWaiterServicePointTooCloseToSeat(table, navMeshPosition, TableServiceSeatClearance))
                    {
                        continue;
                    }

                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(fromPosition, navMeshPosition, NavMesh.AllAreas, path)
                        || path.status != NavMeshPathStatus.PathComplete)
                    {
                        continue;
                    }

                    var distance = Vector3.Distance(fromPosition, navMeshPosition);
                    if (distance >= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = distance;
                    bestPosition = navMeshPosition;
                    found = true;
                }

                if (found && scaleIndex == 0)
                {
                    break;
                }
            }

            if (found)
            {
                return bestPosition;
            }

            var toWaiter = fromPosition - center;
            toWaiter.y = 0f;
            var fallbackDir = toWaiter.sqrMagnitude > 0.01f
                ? toWaiter.normalized
                : (forward + right).normalized;
            var fallbackCandidate = center + fallbackDir * (Mathf.Max(halfRight, halfForward) + outward + TableServiceFallbackExtraPadding);
            if (TryGetNavMeshPosition(fallbackCandidate, out var fallbackNav)
                && !IsWaiterServicePointTooCloseToSeat(table, fallbackNav, TableServiceFallbackSeatClearance))
            {
                return fallbackNav;
            }

            return TryGetNavMeshPosition(fromPosition, out var stay) ? stay : fromPosition;
        }

        /// <summary>
        /// 估算桌面在 right/forward 方向的半宽，用于四角站位。
        /// </summary>
        private static void ResolveTableHalfExtents(TableArea table, out float halfRight, out float halfForward)
        {
            halfRight = 0.48f;
            halfForward = 0.48f;
            if (table == null)
            {
                return;
            }

            var target = table.tableObj != null && table.tableObj.activeInHierarchy
                ? table.tableObj
                : table.gameObject;
            if (target == null)
            {
                return;
            }

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            var hasBounds = false;
            var bounds = default(Bounds);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            if (!hasBounds)
            {
                return;
            }

            // 世界 AABB 在水平面的半尺寸；方桌大约 0.4~0.7
            halfRight = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.72f, 0.38f, 0.85f);
            halfForward = halfRight;
        }

        /// <summary>
        /// 判断服务点是否过近于任一座位（平面距离）。
        /// </summary>
        private static bool IsWaiterServicePointTooCloseToSeat(TableArea table, Vector3 servicePosition, float minDistance)
        {
            if (table == null || minDistance <= 0f)
            {
                return false;
            }

            var minSqr = minDistance * minDistance;
            var capacity = table.GetSeatCapacity();
            for (var seatIndex = 0; seatIndex < capacity; seatIndex++)
            {
                if (!table.TryGetSeatPoseByIndex(seatIndex, out var seatPosition, out _))
                {
                    continue;
                }

                var delta = seatPosition - servicePosition;
                delta.y = 0f;
                if (delta.sqrMagnitude < minSqr)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 获取上菜取菜点，优先使用蒸笼，其次使用灶台。
        /// </summary>
        /// <returns>取菜目标对象。</returns>
        private GameObject ResolveDishPickupTarget()
        {
            if (foodTableObject != null && foodTableObject.activeInHierarchy)
            {
                return foodTableObject;
            }

            if (guideSteamerObject != null && guideSteamerObject.activeInHierarchy)
            {
                return guideSteamerObject;
            }

            if (guideStoveObject != null && guideStoveObject.activeInHierarchy)
            {
                return guideStoveObject;
            }

            return FindSceneGameObjectByName("Steamer_1")
                   ?? FindSceneGameObjectByName("Steamer")
                   ?? FindSceneGameObjectByName("BigStove")
                   ?? FindSceneGameObjectByName("灶台");
        }

        /// <summary>
        /// 在目标物体周围选择离小二最近且可寻路的交互点。
        /// 环绕半径按渲染包围盒水平半对角线外推，避免站进灶台/蒸笼体积内。
        /// </summary>
        /// <param name="targetObject">目标物体。</param>
        /// <param name="fromPosition">小二当前坐标。</param>
        /// <returns>可寻路交互点。</returns>
        private static Vector3 ResolveObjectServicePosition(GameObject targetObject, Vector3 fromPosition)
        {
            if (targetObject == null)
            {
                return fromPosition;
            }

            TryGetNavMeshPosition(fromPosition, out fromPosition);
            var center = ResolveObjectCenter(targetObject);
            var approachRadius = ResolveObjectApproachRadius(targetObject);
            var transform = targetObject.transform;
            var directions = new[]
            {
                transform.forward,
                -transform.forward,
                transform.right,
                -transform.right,
                (fromPosition - center).normalized
            };

            // 由近到远多档尝试，优先贴边，失败再外扩。
            var radiusScales = new[] { 1f, 1.25f, 1.55f, 1.9f };
            var bestPosition = center;
            var bestDistance = float.MaxValue;
            var found = false;
            for (var scaleIndex = 0; scaleIndex < radiusScales.Length; scaleIndex++)
            {
                var radius = approachRadius * radiusScales[scaleIndex];
                for (var index = 0; index < directions.Length; index++)
                {
                    var direction = directions[index];
                    if (direction.sqrMagnitude < 0.01f)
                    {
                        continue;
                    }

                    var flatDirection = new Vector3(direction.x, 0f, direction.z);
                    if (flatDirection.sqrMagnitude < 0.01f)
                    {
                        continue;
                    }

                    var candidate = center + flatDirection.normalized * radius;
                    candidate.y = fromPosition.y;
                    if (!TryGetNavMeshPosition(candidate, out var navMeshPosition))
                    {
                        continue;
                    }

                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(fromPosition, navMeshPosition, NavMesh.AllAreas, path)
                        || path.status != NavMeshPathStatus.PathComplete)
                    {
                        continue;
                    }

                    var distance = Vector3.Distance(fromPosition, navMeshPosition);
                    if (distance >= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = distance;
                    bestPosition = navMeshPosition;
                    found = true;
                }

                if (found)
                {
                    break;
                }
            }

            return TryGetNavMeshPosition(bestPosition, out var fallbackPosition) ? fallbackPosition : bestPosition;
        }

        /// <summary>
        /// 取菜/交互环绕半径：包围盒水平半对角线 + 垫边，至少 0.85。
        /// </summary>
        private static float ResolveObjectApproachRadius(GameObject targetObject)
        {
            const float minRadius = 0.85f;
            const float padding = 0.35f;
            if (!TryGetObjectMeshBounds(targetObject, out var bounds))
            {
                return minRadius;
            }

            var halfX = bounds.extents.x;
            var halfZ = bounds.extents.z;
            var horizontalHalfDiagonal = Mathf.Sqrt(halfX * halfX + halfZ * halfZ);
            return Mathf.Max(minRadius, horizontalHalfDiagonal + padding);
        }

        /// <summary>
        /// 根据网格渲染包围盒获取物体中心（忽略粒子等特效），缺少网格时用根节点。
        /// </summary>
        /// <param name="targetObject">目标物体。</param>
        /// <returns>物体中心坐标。</returns>
        private static Vector3 ResolveObjectCenter(GameObject targetObject)
        {
            if (!TryGetObjectMeshBounds(targetObject, out var bounds))
            {
                return targetObject.transform.position;
            }

            // 站位按水平中心算，Y 用物体根坐标，避免抬到灶台半空。
            var center = bounds.center;
            center.y = targetObject.transform.position.y;
            return center;
        }

        /// <summary>
        /// 统计物体网格类 Renderer 的世界包围盒，排除粒子以免半径被火焰撑爆。
        /// </summary>
        private static bool TryGetObjectMeshBounds(GameObject targetObject, out Bounds bounds)
        {
            bounds = default;
            if (targetObject == null)
            {
                return false;
            }

            var renderers = targetObject.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }

            var hasAny = false;
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                if (!hasAny)
                {
                    bounds = renderer.bounds;
                    hasAny = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasAny;
        }

        private static IEnumerator MoveCharacterAlongNavMesh(Transform target, Vector3 destination, float speed, bool snapToNavMesh)
        {
            if (target == null)
            {
                yield break;
            }

            var moveTrail = WaiterMoveTrailView.Resolve(target);
            moveTrail?.BeginMove(speed, target.gameObject);
            try
            {
                yield return MoveCharacterAlongNavMeshInternal(target, destination, speed, snapToNavMesh);
            }
            finally
            {
                moveTrail?.EndMove();
            }
        }

        /// <summary>
        /// 沿导航网格路径移动角色，并同步速度参数驱动行走动画。
        /// 加入卡住检测与整体超时，避免角色在拐点附近原地空转。
        /// </summary>
        private static IEnumerator MoveCharacterAlongNavMeshInternal(Transform target, Vector3 destination, float speed, bool snapToNavMesh)
        {
            if (target == null)
            {
                yield break;
            }

            var animator = target.GetComponentInChildren<Animator>(true);
            PrepareAnimatorForMovement(animator);
            var start = target.position;
            if (snapToNavMesh)
            {
                TryGetNavMeshPosition(start, out start);
                TryGetNavMeshPosition(destination, out destination);
                target.position = start;
            }

            var corners = BuildMovementCorners(start, destination);
            SetAnimatorSpeed(animator, WalkAnimationSpeed);
            var totalElapsed = 0f;
            for (var cornerIndex = 0; cornerIndex < corners.Count; cornerIndex++)
            {
                var corner = corners[cornerIndex];
                var stuckSamplePosition = target.position;
                var stuckSampleTime = 0f;
                while (Vector3.Distance(target.position, corner) > WaiterReachDistance)
                {
                    if (target == null)
                    {
                        yield break;
                    }

                    if (totalElapsed > WaiterMoveTotalTimeout)
                    {
                        target.position = destination;
                        SetAnimatorSpeed(animator, 0f);
                        yield break;
                    }

                    var nextPosition = Vector3.MoveTowards(target.position, corner, speed * Time.deltaTime);
                    var direction = ResolveMovementLookDirection(target.position, corners, cornerIndex, corner);
                    if (direction.sqrMagnitude > 0.0001f)
                    {
                        var lookRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                        target.rotation = Quaternion.RotateTowards(target.rotation, lookRotation, WaiterTurnSpeed * Time.deltaTime);
                    }

                    target.position = nextPosition;

                    var deltaTime = Time.deltaTime;
                    totalElapsed += deltaTime;
                    stuckSampleTime += deltaTime;
                    if (stuckSampleTime >= WaiterMoveStuckCheckInterval)
                    {
                        if (Vector3.Distance(stuckSamplePosition, target.position) < WaiterMoveStuckProgressThreshold)
                        {
                            // 视为卡死，直接吸附到当前拐点继续后续路径，避免无限循环
                            target.position = corner;
                            break;
                        }

                        stuckSampleTime = 0f;
                        stuckSamplePosition = target.position;
                    }

                    yield return null;
                }
            }
            target.position = destination;
            SetAnimatorSpeed(animator, 0f);
        }

        private static IEnumerator MoveCharacterDirectly(
            Transform target,
            Vector3 destination,
            float speed,
            bool snapDestinationToNavMesh)
        {
            if (target == null)
            {
                yield break;
            }

            var moveTrail = WaiterMoveTrailView.Resolve(target);
            moveTrail?.BeginMove(speed, target.gameObject);
            try
            {
                yield return MoveCharacterDirectlyInternal(target, destination, speed, snapDestinationToNavMesh);
            }
            finally
            {
                moveTrail?.EndMove();
            }
        }

        /// <summary>
        /// 沿直线移动到目标点，不走导航网格绕路，适合门口拉客等短距离可见移动。
        /// </summary>
        private static IEnumerator MoveCharacterDirectlyInternal(
            Transform target,
            Vector3 destination,
            float speed,
            bool snapDestinationToNavMesh)
        {
            if (target == null)
            {
                yield break;
            }

            if (snapDestinationToNavMesh)
            {
                TryGetNavMeshPosition(destination, out destination);
            }

            var animator = target.GetComponentInChildren<Animator>(true);
            PrepareAnimatorForMovement(animator);
            SetAnimatorSpeed(animator, WalkAnimationSpeed);

            var totalElapsed = 0f;
            var stuckSamplePosition = target.position;
            var stuckSampleTime = 0f;
            while (Vector3.Distance(target.position, destination) > WaiterReachDistance)
            {
                if (target == null)
                {
                    yield break;
                }

                if (totalElapsed > WaiterMoveTotalTimeout)
                {
                    break;
                }

                var nextPosition = Vector3.MoveTowards(target.position, destination, speed * Time.deltaTime);
                var direction = destination - target.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    var lookRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    target.rotation = Quaternion.RotateTowards(target.rotation, lookRotation, WaiterTurnSpeed * Time.deltaTime);
                }

                target.position = nextPosition;

                var deltaTime = Time.deltaTime;
                totalElapsed += deltaTime;
                stuckSampleTime += deltaTime;
                if (stuckSampleTime >= WaiterMoveStuckCheckInterval)
                {
                    if (Vector3.Distance(stuckSamplePosition, target.position) < WaiterMoveStuckProgressThreshold)
                    {
                        break;
                    }

                    stuckSampleTime = 0f;
                    stuckSamplePosition = target.position;
                }

                yield return null;
            }

            if (target != null)
            {
                target.position = destination;
                SetAnimatorSpeed(animator, 0f);
            }
        }

        /// <summary>
        /// 根据当前路径拐点计算移动朝向，提前看向下一段路径避免到拐角处突然转身。
        /// </summary>
        /// <param name="currentPosition">当前坐标。</param>
        /// <param name="corners">导航路径拐点。</param>
        /// <param name="cornerIndex">当前拐点索引。</param>
        /// <param name="currentCorner">当前正在靠近的拐点。</param>
        /// <returns>水平移动朝向。</returns>
        private static Vector3 ResolveMovementLookDirection(Vector3 currentPosition, System.Collections.Generic.List<Vector3> corners, int cornerIndex, Vector3 currentCorner)
        {
            var lookTarget = currentCorner;
            if (corners != null
                && cornerIndex + 1 < corners.Count
                && Vector3.Distance(currentPosition, currentCorner) <= WaiterLookAheadDistance)
            {
                lookTarget = corners[cornerIndex + 1];
            }

            var direction = lookTarget - currentPosition;
            direction.y = 0f;
            return direction;
        }

        /// <summary>
        /// 根据导航网格路径生成移动拐点，寻路失败时退回直线路径。
        /// </summary>
        /// <param name="start">起点坐标。</param>
        /// <param name="destination">终点坐标。</param>
        /// <returns>路径拐点列表。</returns>
        private static System.Collections.Generic.List<Vector3> BuildMovementCorners(Vector3 start, Vector3 destination)
        {
            var corners = new System.Collections.Generic.List<Vector3>();
            var path = new NavMeshPath();
            if (NavMesh.CalculatePath(start, destination, NavMesh.AllAreas, path) && path.corners != null && path.corners.Length > 0)
            {
                for (var index = 1; index < path.corners.Length; index++)
                {
                    corners.Add(path.corners[index]);
                }
            }

            if (corners.Count == 0)
            {
                corners.Add(destination);
            }

            return corners;
        }

        /// <summary>
        /// 让角色只在水平面上朝向目标点。
        /// </summary>
        /// <param name="target">需要旋转的角色。</param>
        /// <param name="lookAtPosition">朝向目标坐标。</param>
        private static void FaceTargetOnGround(Transform target, Vector3 lookAtPosition)
        {
            if (target == null)
            {
                return;
            }

            var direction = lookAtPosition - target.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                target.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }

        /// <summary>
        /// 让角色平滑转向目标点，避免服务动作开始前瞬间大幅度扭头。
        /// </summary>
        /// <param name="target">需要旋转的角色。</param>
        /// <param name="lookAtPosition">朝向目标坐标。</param>
        /// <returns>协程迭代器。</returns>
        private static IEnumerator RotateCharacterToFace(Transform target, Vector3 lookAtPosition)
        {
            if (target == null)
            {
                yield break;
            }

            var direction = lookAtPosition - target.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                yield break;
            }

            var targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            var timeout = 0.35f;
            while (timeout > 0f && Quaternion.Angle(target.rotation, targetRotation) > 1f)
            {
                target.rotation = Quaternion.RotateTowards(target.rotation, targetRotation, WaiterTurnSpeed * Time.deltaTime);
                timeout -= Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// 触发单个厨师的做菜动画。
        /// 先尝试走触发器，若控制器没有及时切换，再兜底切到 Cook 状态。
        /// </summary>
        /// <param name="animator">厨师动画器。</param>
        private static void PlayChefCookAnimationInternal(Animator animator)
        {
            if (animator == null || !animator.isActiveAndEnabled)
            {
                return;
            }

            SetAnimatorSpeed(animator, 0f);
            if (IsAnimatorInCookState(animator))
            {
                return;
            }

            if (HasAnimatorCookState(animator))
            {
                CrossFadeStateIfAvailable(animator, ChefBaseLayerCookState, ChefCookState);
                return;
            }

            if (HasAnimatorParameter(animator, ChefCookTrigger, AnimatorControllerParameterType.Trigger))
            {
                animator.ResetTrigger(ChefCookTrigger);
                animator.SetTrigger(ChefCookTrigger);
            }
        }

        /// <summary>
        /// 结束单个厨师的做菜状态，恢复到正常待机或移动状态。
        /// </summary>
        /// <param name="animator">厨师动画器。</param>
        private static void ResetChefCookAnimationInternal(Animator animator)
        {
            if (animator == null || !animator.isActiveAndEnabled)
            {
                return;
            }

            SetAnimatorSpeed(animator, 0f);
            if (HasAnimatorParameter(animator, ChefCookTrigger, AnimatorControllerParameterType.Trigger))
            {
                animator.ResetTrigger(ChefCookTrigger);
            }

            CrossFadeMovementStateIfAvailable(animator);
        }

        /// <summary>
        /// 判断动画器当前是否已经在 Cook 状态，避免每次轮询都重复打断动作。
        /// </summary>
        /// <param name="animator">厨师动画器。</param>
        /// <returns>当前已经在做菜状态时返回 true。</returns>
        private static bool IsAnimatorInCookState(Animator animator)
        {
            if (animator == null || !animator.isActiveAndEnabled)
            {
                return false;
            }

            var currentState = animator.GetCurrentAnimatorStateInfo(0);
            return currentState.IsName(ChefBaseLayerCookState) || currentState.IsName(ChefCookState);
        }

        /// <summary>
        /// 判断控制器里是否存在 Cook 状态。
        /// </summary>
        /// <param name="animator">动画器。</param>
        /// <returns>存在 Cook 状态时返回 true。</returns>
        private static bool HasAnimatorCookState(Animator animator)
        {
            if (animator == null || !animator.isActiveAndEnabled)
            {
                return false;
            }

            return animator.HasState(0, Animator.StringToHash(ChefBaseLayerCookState))
                   || animator.HasState(0, Animator.StringToHash(ChefCookState));
        }

        /// <summary>
        /// 根据动画器参数安全触发指定 Trigger。
        /// </summary>
        /// <param name="animator">动画器。</param>
        /// <param name="triggerName">Trigger 参数名。</param>
        private static void TriggerAnimator(Animator animator, string triggerName)
        {
            if (animator == null || string.IsNullOrEmpty(triggerName))
            {
                return;
            }

            foreach (var parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == triggerName)
                {
                    animator.SetTrigger(triggerName);
                    return;
                }
            }
        }

        /// <summary>
        /// 把角色从服务或入座状态切回移动准备状态，避免上一段动作残留到下一段路。
        /// </summary>
        /// <param name="animator">角色动画器。</param>
        private static void PrepareAnimatorForMovement(Animator animator)
        {
            if (animator == null)
            {
                return;
            }

            SetAnimatorSpeed(animator, 0f);
            if (HasAnimatorParameter(animator, WaiterCleanTrigger, AnimatorControllerParameterType.Trigger))
            {
                animator.ResetTrigger(WaiterCleanTrigger);
            }

            if (HasAnimatorParameter(animator, AnimatorIsEatingParam, AnimatorControllerParameterType.Bool))
            {
                animator.SetBool(AnimatorIsEatingParam, false);
            }

            CrossFadeMovementStateIfAvailable(animator);
        }

        /// <summary>
        /// 把小二从清扫状态切回待机状态，避免清扫动作残留到下一段路。
        /// </summary>
        /// <param name="animator">小二动画器。</param>
        private static void ResetWaiterServiceAnimation(Animator animator)
        {
            if (animator == null)
            {
                return;
            }

            SetAnimatorSpeed(animator, 0f);
            if (HasAnimatorParameter(animator, WaiterCleanTrigger, AnimatorControllerParameterType.Trigger))
            {
                animator.ResetTrigger(WaiterCleanTrigger);
            }

            CrossFadeMovementStateIfAvailable(animator);
        }

        private void PlayWaiterNapAnimation(GameObject waiter, Animator animator)
        {
            if (animator == null || !animator.isActiveAndEnabled)
            {
                PlayWaiterSleepingHudAnimation(waiter);
                return;
            }

            // Sleep 需要正常播放；速度置 0 会导致动作卡在过渡帧，看起来像整模倒地/陷进地面。
            SetAnimatorSpeed(animator, 1f);
            if (HasAnimatorParameter(animator, AnimatorIsEatingParam, AnimatorControllerParameterType.Bool))
            {
                animator.SetBool(AnimatorIsEatingParam, false);
            }

            if (HasAnimatorParameter(animator, "IsSitting", AnimatorControllerParameterType.Bool))
            {
                animator.SetBool("IsSitting", true);
            }

            if (!string.IsNullOrWhiteSpace(waiterWakeAnimationTrigger))
            {
                animator.ResetTrigger(waiterWakeAnimationTrigger);
            }

            // 入座后立刻切 Sleep：优先短 CrossFade，避免只靠 Trigger 时在座位 Idle 上僵坐。
            if (!string.IsNullOrWhiteSpace(waiterNapTrigger))
            {
                CrossFadeStateImmediate(animator, "Base Layer.Sleep", waiterNapTrigger);
                TriggerAnimator(animator, waiterNapTrigger);
            }

            PlayWaiterSleepingHudAnimation(waiter);
        }

        /// <summary>
        /// 短过渡强制切到目标状态，用于打盹等需要「到位立刻播」的表现。
        /// </summary>
        private static void CrossFadeStateImmediate(Animator animator, string fullPathStateName, string shortStateName)
        {
            if (animator == null)
            {
                return;
            }

            const float fade = 0.05f;
            var fullPathHash = Animator.StringToHash(fullPathStateName);
            var shortNameHash = Animator.StringToHash(shortStateName);
            if (animator.HasState(0, fullPathHash))
            {
                animator.CrossFade(fullPathHash, fade, 0, 0f);
                return;
            }

            if (animator.HasState(0, shortNameHash))
            {
                animator.CrossFade(shortNameHash, fade, 0, 0f);
            }
        }

        private void PlayWaiterSleepingHudAnimation(GameObject waiter)
        {
            if (waiter == null || !activeWaiterStateIcons.TryGetValue(waiter, out var root) || root == null)
            {
                return;
            }

            var hudAnimTransform = HudBindingUtility.FindChildRecursive(root.transform, "HudAnim");
            var hudAnimator = hudAnimTransform != null ? hudAnimTransform.GetComponent<Animator>() : null;
            if (hudAnimator == null)
            {
                return;
            }

            hudAnimator.ResetTrigger(waiterWakeHudTrigger);
            hudAnimator.ResetTrigger(waiterSleepHudTrigger);
            TriggerAnimator(hudAnimator, waiterSleepHudTrigger);
        }

        /// <summary>
        /// 仅在控制器确实包含移动状态时切回 Movement，避免不同 NPC 控制器被硬切到不存在的状态。
        /// </summary>
        /// <param name="animator">角色动画器。</param>
        private static void CrossFadeMovementStateIfAvailable(Animator animator)
        {
            if (animator == null || !animator.isActiveAndEnabled)
            {
                return;
            }

            CrossFadeStateIfAvailable(animator, AnimatorBaseLayerMovementState, AnimatorMovementState);
        }

        /// <summary>
        /// 仅在控制器确实存在目标状态时执行 CrossFade。
        /// </summary>
        /// <param name="animator">角色动画器。</param>
        /// <param name="fullPathStateName">完整状态名。</param>
        /// <param name="shortStateName">短状态名。</param>
        private static void CrossFadeStateIfAvailable(Animator animator, string fullPathStateName, string shortStateName)
        {
            if (animator == null || !animator.isActiveAndEnabled)
            {
                return;
            }

            var fullPathHash = Animator.StringToHash(fullPathStateName);
            var shortNameHash = Animator.StringToHash(shortStateName);
            if (animator.HasState(0, fullPathHash))
            {
                animator.CrossFade(fullPathHash, 0.12f, 0);
                return;
            }

            if (animator.HasState(0, shortNameHash))
            {
                animator.CrossFade(shortNameHash, 0.12f, 0);
            }
        }

        /// <summary>
        /// 根据动画器参数安全设置移动速度。
        /// </summary>
        /// <param name="animator">动画器。</param>
        /// <param name="speed">速度值。</param>
        private static void SetAnimatorSpeed(Animator animator, float speed)
        {
            if (animator == null)
            {
                return;
            }

            foreach (var parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Float && parameter.name == WaiterSpeedParam)
                {
                    animator.SetFloat(WaiterSpeedParam, speed);
                    return;
                }
            }
        }

        #endregion
    }
}
