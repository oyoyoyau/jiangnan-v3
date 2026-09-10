using System.Collections;
using System.Collections.Generic;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.Model;
using JN.Client.UI;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace JN.Client.Scene
{
    /// <summary>
    /// 负责酒楼场景相关的运行时逻辑。
    /// </summary>
    public partial class TavernSceneManager : IChefRuntimeHost
    {
        private const float CookDemandPollInterval = 0.25f;
        private const float DishOnPlateYOffset = 0.025f;
        private const float FoodTablePlateSurfaceYOffset = 0.015f;
        /// <summary>出餐台首槽本地 X（第 1 列）。</summary>
        private const float FoodTablePlateStartLocalX = -0.14f;
        /// <summary>出餐台首槽本地 Z（第 1 行）。</summary>
        private const float FoodTablePlateStartLocalZ = -0.4f;
        /// <summary>出餐台列间距（本地 X）；(0,0)→(0,1) 走 X。</summary>
        private const float FoodTablePlateSpacingX = 0.3f;
        /// <summary>出餐台行间距（本地 Z）；第 2 行 Z=0。</summary>
        private const float FoodTablePlateSpacingZ = 0.4f;
        /// <summary>出餐台餐盘本地 Y 旋转（度）。</summary>
        private const float FoodTablePlateLocalYawDegrees = 90f;
        /// <summary>每行列数（沿本地 X）。</summary>
        private const int FoodTablePlateColumnCount = 3;
        /// <summary>行数（沿本地 Z）。</summary>
        private const int FoodTablePlateRowCount = 2;
        private const int FoodTablePlateSlotCount = FoodTablePlateColumnCount * FoodTablePlateRowCount;
        private const float FoodTablePlateOverlapStackYOffset = 0.045f;
        private const float FoodTableServeBubbleLocalYOffset = TavernWorldRuntimeHudLayout.FoodTableServeHeightOffset;
        private const float GroupSpawnSpacing = 0.55f;
        private const float ChefCookAnimPulseSeconds = 1.35f;
        private GameObject foodTableServeBubble;
        private Sprite foodTableServeBubbleIcon;

        /// <summary>厨师做菜计时会话（不用协程，避免 StopCoroutine continue failure）。</summary>
        private readonly Dictionary<GameObject, ChefCookSession> chefCookSessions = new();
        private readonly List<GameObject> chefCookTickBuffer = new();
        /// <summary>前台点单计时会话（不用协程，快照恢复续跑时同样避免 StopCoroutine continue failure）。</summary>
        private readonly Dictionary<int, FrontCounterOrderSession> frontCounterOrderSessions = new();
        private readonly List<int> frontCounterOrderTickBuffer = new();
        /// <summary>贵客菜单下普通客：点单入座后再抱怨离店（仅标记本轮前台点单桌）。</summary>
        private readonly HashSet<int> pendingVipMenuRejectAfterSeatTableIds = new();

        private sealed class ChefCookSession
        {
            public ChefCharacter Context;
            public int TableId;
            public float EndsAt;
            public float NextAnimPulseAt;
        }

        private sealed class FrontCounterOrderSession
        {
            public float EndsAt;
        }

        /// <summary>
        /// 厨师调度循环：只负责派发做菜任务和同步空闲表现，不再直接执行做菜细节。
        /// </summary>
        private IEnumerator ChefServiceLoop()
        {
            var demandWait = new WaitForSeconds(CookDemandPollInterval);
            while (DataManager.Instance?.TavernData != null && DataManager.Instance.TavernData.isOpen)
            {
                DispatchPendingChefTasks();
                SyncChefPassiveStates();
                yield return demandWait;
            }
        }

        /// <summary>
        /// 根据当前缺菜需求派发厨师做菜任务，每个任务对应一个已被小二正式通知的桌位工单。
        /// </summary>
        private void DispatchPendingChefTasks()
        {
            var pendingDishDemand = GetPendingDishDemand();
            while (pendingDishDemand > 0 && TryGetNextCookableTableId(out var tableId))
            {
                if (!chefTaskDispatchService.TryDispatchChefTask(this, new CookDishTask(tableId)))
                {
                    break;
                }

                pendingDishDemand--;
            }
        }

        /// <summary>
        /// 同步未在执行中的厨师表现，避免厨师停留在旧动作或状态残留。
        /// </summary>
        private void SyncChefPassiveStates()
        {
            var activeChefs = GetGuideStaffVisuals(GuideChefVisualKey);
            if (activeChefs.Length == 0)
            {
                return;
            }

            var hasCookableTicket = TryGetNextCookableTableId(out _);
            for (var index = 0; index < activeChefs.Length; index++)
            {
                var chef = activeChefs[index];
                if (chef == null || busyChefs.Contains(chef) || IsChefNapping(chef))
                {
                    continue;
                }

                TrackChefState(chef, hasCookableTicket ? ChefStateKeys.Blocked : ChefStateKeys.Idle);
            }
        }

        /// <summary>
        /// 获取下一个可真正开工的做菜工单。
        /// 只处理已正式通知后厨、未完成且尚未派给其他厨师的待上菜桌位。
        /// </summary>
        private bool TryGetNextCookableTableId(out int tableId)
        {
            foreach (var tablePair in AllTables)
            {
                var currentTableId = tablePair.Key;
                if (assignedCookTableIds.Contains(currentTableId))
                {
                    continue;
                }

                var tableData = DataManager.Instance.GetTableData(currentTableId);
                if (tableData == null || !tableData.isUnlocked)
                {
                    continue;
                }

                if ((TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.WaitingServe)
                {
                    continue;
                }

                if (!tableCookOrderTickets.TryGetValue(currentTableId, out var ticket)
                    || ticket == null
                    || !ticket.isChefNotified
                    || ticket.isCompleted)
                {
                    continue;
                }

                tableId = currentTableId;
                return true;
            }

            tableId = -1;
            return false;
        }

        /// <summary>
        /// 计算当前仍有多少张待上菜桌位尚未完成对应工单。
        /// 这里按已正式通知后厨的桌位工单计数，而不是按全局成品库存抵扣，
        /// 避免前一单的现有库存误伤后一单，也避免 waiter 尚未报到后厨时厨师提前开做。
        /// </summary>
        /// <returns>大于 0 表示还有工单需要厨师继续处理；0 表示当前无需开火。</returns>
        private int GetPendingDishDemand()
        {
            var activeCookDemandCount = 0;
            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance.GetTableData(tablePair.Key);
                if (tableData == null || !tableData.isUnlocked)
                {
                    continue;
                }

                if ((TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.WaitingServe)
                {
                    continue;
                }

                if (!tableCookOrderTickets.TryGetValue(tablePair.Key, out var ticket)
                    || ticket == null
                    || !ticket.isChefNotified
                    || ticket.isCompleted)
                {
                    continue;
                }

                activeCookDemandCount++;
            }

            return activeCookDemandCount;
        }

        /// <summary>
        /// 获取或缓存厨师运行时上下文。
        /// </summary>
        internal ChefCharacter GetOrCreateChefContext(GameObject chefVisual)
        {
            if (chefVisual == null)
            {
                return null;
            }

            if (chefRuntimeContexts.TryGetValue(chefVisual, out var context) && context != null)
            {
                return context;
            }

            context = chefVisual.GetComponent<ChefCharacter>();
            if (context == null)
            {
                Debug.LogWarning($"Chef visual missing {nameof(ChefCharacter)}: {chefVisual.name}");
                return null;
            }

            ClearStaffHeadOrderBubbleNodes(chefVisual);
            context.InitializeChef(this, this);
            chefRuntimeContexts[chefVisual] = context;
            return context;
        }

        /// <summary>
        /// 获取当前可接做菜任务的厨师。
        /// </summary>
        private GameObject GetAvailableChefForTask(CookDishTask task)
        {
            var activeChefs = GetGuideStaffVisuals(GuideChefVisualKey);
            if (activeChefs == null || activeChefs.Length == 0)
            {
                return null;
            }

            for (var offset = 0; offset < activeChefs.Length; offset++)
            {
                var chefIndex = (nextChefCookIndex + offset) % activeChefs.Length;
                var chef = activeChefs[chefIndex];
                if (chef == null || busyChefs.Contains(chef) || staffVisualsBeingAnimated.Contains(chef) || IsChefNapping(chef))
                {
                    continue;
                }

                nextChefCookIndex = (chefIndex + 1) % activeChefs.Length;
                return chef;
            }

            return null;
        }

        /// <summary>
        /// 兼容旧接口：厨师不再启动状态协程。
        /// </summary>
        private void StartChefStateRoutine(ChefCharacter context, IEnumerator routine)
        {
        }

        /// <summary>
        /// 尝试把指定厨师切到做菜状态（计时会话，非协程）。
        /// </summary>
        private bool TryStartChefTask(GameObject chef, CookDishTask task, ICharacterState<ChefCharacter> initialState)
        {
            if (chef == null || task == null || initialState == null || assignedCookTableIds.Contains(task.TableId))
            {
                return false;
            }

            var context = GetOrCreateChefContext(chef);
            if (context == null)
            {
                return false;
            }

            busyChefs.Add(chef);
            assignedCookTableIds.Add(task.TableId);
            chefCookAssignments[chef] = task.TableId;
            // 快照续跑：工单已在 cooking 时保留 cookStartedAt，避免重置出餐进度。
            var resumeExistingCook = tableCookOrderTickets.TryGetValue(task.TableId, out var existingTicket)
                                     && existingTicket != null
                                     && existingTicket.isCooking
                                     && existingTicket.cookStartedAt > 0f
                                     && !existingTicket.isCompleted;
            if (!resumeExistingCook)
            {
                // 工单计时与厨师实际做菜时长一致（含厨师速度画像，不含营业加速）。
                StartCookOrderTicket(task.TableId, GetChefCookDuration(context, task));
            }

            context.CurrentTask = task;
            // SetPassiveState：只切表现，不跑 Execute 协程。
            context.SetPassiveState(initialState);
            BeginChefCookSession(context, task.TableId);
            return true;
        }

        /// <summary>
        /// 开始/覆盖厨师做菜计时会话。
        /// </summary>
        private void BeginChefCookSession(ChefCharacter context, int tableId)
        {
            if (context == null || context.gameObject == null || tableId <= 0)
            {
                return;
            }

            var chef = context.gameObject;
            var duration = GetChefCookDuration(context, context.CurrentTask as CookDishTask);
            ShowChefCookProgress(chef, duration);
            PlayChefCookAnimation(chef);
            chefCookSessions[chef] = new ChefCookSession
            {
                Context = context,
                TableId = tableId,
                EndsAt = Time.time + duration,
                NextAnimPulseAt = Time.time + ChefCookAnimPulseSeconds
            };
        }

        /// <summary>
        /// 逐帧推进厨师做菜计时；完成/中止走事件式收尾，不 StopCoroutine。
        /// </summary>
        private void TickChefCookSessions()
        {
            if (chefCookSessions.Count <= 0)
            {
                return;
            }

            chefCookTickBuffer.Clear();
            foreach (var pair in chefCookSessions)
            {
                chefCookTickBuffer.Add(pair.Key);
            }

            for (var index = 0; index < chefCookTickBuffer.Count; index++)
            {
                var chef = chefCookTickBuffer[index];
                if (chef == null || !chefCookSessions.TryGetValue(chef, out var session) || session?.Context == null)
                {
                    if (chef != null)
                    {
                        chefCookSessions.Remove(chef);
                    }

                    continue;
                }

                if (!IsBusinessOpenForChefCook() || !IsCookTicketActive(session.TableId))
                {
                    chefCookSessions.Remove(chef);
                    AbortChefTask(session.Context);
                    continue;
                }

                if (Time.time >= session.NextAnimPulseAt)
                {
                    PlayChefCookAnimation(chef);
                    session.NextAnimPulseAt = Time.time + ChefCookAnimPulseSeconds;
                }

                if (Time.time < session.EndsAt)
                {
                    continue;
                }

                chefCookSessions.Remove(chef);
                CompleteChefCookTask(session.Context, session.TableId);
            }
        }

        private bool IsBusinessOpenForChefCook()
        {
            return DataManager.Instance != null
                   && DataManager.Instance.TavernData != null
                   && DataManager.Instance.TavernData.isOpen;
        }

        /// <summary>
        /// 结束厨师任务并回收派发占用。
        /// </summary>
        private void ReleaseChefTask(ChefCharacter context, bool stopRunningRoutine = true)
        {
            if (context == null)
            {
                return;
            }

            var chef = context.gameObject;
            if (chef != null)
            {
                chefCookSessions.Remove(chef);
            }

            if (chef != null && chefTaskRoutines.TryGetValue(chef, out var routine))
            {
                // 遗留协程句柄：仅移除登记。做菜已不跑协程，避免 Stop 触发 continue failure。
                if (stopRunningRoutine && routine != null)
                {
                    // 不再 StopCoroutine(routine)；只清字典。
                }

                chefTaskRoutines.Remove(chef);
            }

            busyChefs.Remove(chef);
            if (chef != null && chefCookAssignments.TryGetValue(chef, out var tableId))
            {
                assignedCookTableIds.Remove(tableId);
            }

            if (chef != null)
            {
                chefCookAssignments.Remove(chef);
            }

            context.CurrentTask = null;
        }

        /// <summary>
        /// 停业或打烊时清空厨师任务状态。
        /// </summary>
        private void ResetChefTaskState()
        {
            chefCookSessions.Clear();
            chefTaskRoutines.Clear();
            busyChefs.Clear();
            assignedCookTableIds.Clear();
            chefCookAssignments.Clear();
            ClearAllChefNaps();
            var activeChefs = GetGuideStaffVisuals(GuideChefVisualKey);
            for (var index = 0; index < activeChefs.Length; index++)
            {
                var chef = activeChefs[index];
                if (chef == null)
                {
                    continue;
                }

                GameAudioManager.StopChefCook(chef);

                if (chefRuntimeContexts.TryGetValue(chef, out var context) && context != null)
                {
                    context.CurrentTask = null;
                    context.SetPassiveState(new ChefIdleState());
                }
            }
        }

        private void ApplyChefPresentation(GameObject chef, string stateKey)
        {
            if (chef == null)
            {
                return;
            }

            var animator = chef.GetComponentInChildren<Animator>(true);
            switch (stateKey)
            {
                case ChefStateKeys.Cooking:
                    break;
                case ChefStateKeys.Napping:
                    // Sleep 由 PlayChefNapAnimation 驱动，这里不要 Reset 回 Movement。
                    GameAudioManager.StopChefCook(chef);
                    break;
                default:
                    GameAudioManager.StopChefCook(chef);
                    ResetChefCookAnimationInternal(animator);
                    break;
            }
        }

        private bool IsCookTicketActive(int tableId)
        {
            return tableCookOrderTickets.TryGetValue(tableId, out var ticket)
                   && ticket != null
                   && ticket.isCooking
                   && !ticket.isCompleted;
        }

        private float GetChefCookDuration(ChefCharacter chef, CookDishTask task)
        {
            // 快照续跑：工单已在 cooking 时按剩余时长出餐，避免恢复后重跑完整做菜时间。
            if (task != null
                && tableCookOrderTickets.TryGetValue(task.TableId, out var ticket)
                && ticket != null
                && ticket.isCooking
                && !ticket.isCompleted
                && ticket.cookStartedAt > 0f
                && ticket.cookDuration > 0f)
            {
                var remaining = ticket.cookDuration - (Time.time - ticket.cookStartedAt);
                return Mathf.Max(0.1f, remaining);
            }

            // 严格按 TbConfig.chefCookTime；叫醒加速倍率缩短做菜时长。
            return Mathf.Max(0.1f, GetEffectiveDishCookDuration() / GetEffectiveChefCookSpeedMultiplier());
        }

        private void ShowChefCookProgress(GameObject chef, float duration)
        {
            if (chef == null || duration <= 0f)
            {
                return;
            }

            // 定时进度条：跟随厨师头顶，时长与做菜计时一致，结束后自动回收。
            HudOverlayService.ShowChefCookProgress(
                chef.transform,
                duration,
                new Vector3(0f, TavernWorldRuntimeHudLayout.ChefProgressHeightOffset, 0f));
        }

        private void PlayChefCookAnimation(GameObject chef)
        {
            if (chef != null && !staffVisualsBeingAnimated.Contains(chef))
            {
                GameAudioManager.PlayChefCook(chef);
                PlayChefCookAnimationInternal(chef.GetComponentInChildren<Animator>(true));
            }
        }

        private void CompleteChefCookTask(ChefCharacter context, int tableId)
        {
            if (context == null)
            {
                return;
            }

            if (TryGetCookStealingWaiter(tableId, out _))
            {
                StartCookOrderTicket(tableId);
            }
            else
            {
                var pendingOrderDishes = GetPendingDishDemand();
                var cookCount = StaffTalentConfigUtility.TryRollDoubleOrderDishCook(
                    StaffConfigUtility.GetOrNull(context.StaffId),
                    pendingOrderDishes)
                    ? 2
                    : 1;
                CompleteCookOrderTicket(tableId);
                AwardCookedDish(cookCount);
            }

            GameAudioManager.StopChefCook(context.gameObject);
            ResetChefCookAnimationInternal(context.gameObject.GetComponentInChildren<Animator>(true));
            ReleaseChefTask(context);
            var chefGo = context.gameObject;
            ConsumeChefCookStamina(chefGo);
            if (IsChefOutOfStamina(chefGo))
            {
                EnterChefNap(chefGo);
            }
            else
            {
                context.SetPassiveState(new ChefIdleState());
            }

            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        private void AwardCookedDish(int count)
        {
            if (count <= 0 || DataManager.Instance == null)
            {
                return;
            }

            DataManager.Instance.ChangeAvailableDishes(count);
            AddPreparedDishesToFoodTable(count);
            DataManager.Instance.RecordCookedDish(count);
        }

        private void AbortChefTask(ChefCharacter context)
        {
            if (context == null)
            {
                return;
            }

            GameAudioManager.StopChefCook(context.gameObject);
            ResetChefCookAnimationInternal(context.gameObject.GetComponentInChildren<Animator>(true));
            ReleaseChefTask(context);
            context.SetPassiveState(new ChefIdleState());
        }

        bool IChefRuntimeHost.IsBusinessOpen => DataManager.Instance != null && DataManager.Instance.TavernData != null && DataManager.Instance.TavernData.isOpen;

        GameObject IChefRuntimeHost.GetAvailableChefForTask(CookDishTask task)
        {
            return GetAvailableChefForTask(task);
        }

        bool IChefRuntimeHost.TryStartChefTask(GameObject chef, CookDishTask task, ICharacterState<ChefCharacter> initialState)
        {
            return TryStartChefTask(chef, task, initialState);
        }

        void IChefRuntimeHost.StartChefStateRoutine(ChefCharacter context, IEnumerator routine)
        {
            StartChefStateRoutine(context, routine);
        }

        void IChefRuntimeHost.ApplyChefPresentation(GameObject chef, string stateKey)
        {
            ApplyChefPresentation(chef, stateKey);
        }

        bool IChefRuntimeHost.IsCookTicketActive(int tableId)
        {
            return IsCookTicketActive(tableId);
        }

        float IChefRuntimeHost.GetChefCookDuration(ChefCharacter chef, CookDishTask task)
        {
            return GetChefCookDuration(chef, task);
        }

        void IChefRuntimeHost.ShowChefCookProgress(GameObject chef, float duration)
        {
            ShowChefCookProgress(chef, duration);
        }

        void IChefRuntimeHost.PlayChefCookAnimation(GameObject chef)
        {
            PlayChefCookAnimation(chef);
        }

        void IChefRuntimeHost.CompleteChefCookTask(ChefCharacter context, int tableId)
        {
            CompleteChefCookTask(context, tableId);
        }

        void IChefRuntimeHost.AbortChefTask(ChefCharacter context)
        {
            AbortChefTask(context);
        }

        /// <summary>
        /// 小二招聘后，循环检查需要点菜、上菜、结账和清理的桌位。
        /// </summary>
        /// <returns>协程迭代器。</returns>
        private IEnumerator WaiterServiceLoop()
        {
            var wait = new WaitForSeconds(0.75f);
            while (DataManager.Instance?.TavernData != null
                   && (DataManager.Instance.TavernData.isOpen || postCloseCleanupActive))
            {
                EvaluateWaiterNapTransitions();

                if (DataManager.Instance.GetHiredGuideWaiterCount() <= 0)
                {
                    yield return wait;
                    continue;
                }

                if (busyWaiters.Count >= Mathf.Max(1, GetGuideStaffVisuals(GuideWaiterVisualKey).Length))
                {
                    EnsureIdleWaiterPosture();
                    yield return wait;
                    continue;
                }

                // 先派发一个任务；无论是否派到，空闲小二都要回默认站位（避免一直停在桌边）
                var dispatched = TryHandleOneWaiterService();
                EnsureIdleWaiterPosture();
                RefreshFoodTableServeBubble();
                yield return dispatched ? new WaitForSeconds(0.45f) : wait;
            }
        }

        /// <summary>
        /// 尝试让小二处理当前等待最久的一个任务（跨点单/上菜/结账/清扫比较）。
        /// 同一张桌不会被多个小二抢占，已派发的桌位会被跳过。
        /// </summary>
        /// <returns>成功处理任意桌位时返回 true，否则返回 false。</returns>
        private bool TryHandleOneWaiterService()
        {
            // 接客已在软/硬打烊时停掉；此处只服务已入座桌。
            // 硬打烊：点单/上菜不受教学门槛限制，避免「点菜文案但无气泡无人服务」卡死。
            // 收账：小二默认不会自动收账，显示结账气泡由玩家点击派工；全员会收账后才自动派单。
            var postCloseCleanupOnly = postCloseCleanupActive
                                       && DataManager.Instance?.TavernData != null
                                       && !DataManager.Instance.TavernData.isOpen;
            var closingAuto = isClosingBusiness;
            var candidates = new List<WaiterAutoDispatchCandidate>(8);

            if (!postCloseCleanupOnly)
            {
                if (closingAuto || CanAutoDispatchWaiterServe())
                {
                    CollectServeDispatchCandidates(candidates);
                }

                // 点单改由前台自动完成，不再派小二去点单。

                if (closingAuto || CanAutoDispatchWaiterCheckout())
                {
                    CollectCheckoutDispatchCandidates(candidates);
                }
            }

            CollectCleanDispatchCandidates(candidates);
            if (candidates.Count == 0)
            {
                return false;
            }

            candidates.RemoveAll(candidate => !CanDispatchWaiterAutoCandidate(candidate, closingAuto));
            if (candidates.Count == 0)
            {
                return false;
            }

            candidates.Sort(CompareWaiterAutoDispatchCandidates);
            for (var index = 0; index < candidates.Count; index++)
            {
                if (TryDispatchWaiterAutoCandidate(candidates[index], closingAuto))
                {
                    return true;
                }
            }

            return false;
        }

        private enum WaiterAutoDispatchKind
        {
            Serve,
            Order,
            Checkout,
            Clean
        }

        private readonly struct WaiterAutoDispatchCandidate
        {
            public WaiterAutoDispatchCandidate(WaiterAutoDispatchKind kind, int tableId, float waitDuration)
            {
                Kind = kind;
                TableId = tableId;
                WaitDuration = waitDuration;
            }

            public WaiterAutoDispatchKind Kind { get; }
            public int TableId { get; }
            public float WaitDuration { get; }
        }

        private void CollectServeDispatchCandidates(List<WaiterAutoDispatchCandidate> candidates)
        {
            foreach (var tablePair in AllTables)
            {
                if (!IsServeDispatchEligible(tablePair.Key))
                {
                    continue;
                }

                candidates.Add(new WaiterAutoDispatchCandidate(
                    WaiterAutoDispatchKind.Serve,
                    tablePair.Key,
                    waiterTaskWaitTracker.GetWaitDuration(tablePair.Key)));
            }
        }

        private void CollectOrderDispatchCandidates(List<WaiterAutoDispatchCandidate> candidates)
        {
            // 前台点单：不再收集小二点单候选。
        }

        private void CollectCheckoutDispatchCandidates(List<WaiterAutoDispatchCandidate> candidates)
        {
            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance.GetTableData(tablePair.Key);
                if (tableData == null || !tableData.isUnlocked)
                {
                    continue;
                }

                if ((TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.Checkout)
                {
                    continue;
                }

                if (assignedCheckoutTableIds.Contains(tablePair.Key))
                {
                    continue;
                }

                candidates.Add(new WaiterAutoDispatchCandidate(
                    WaiterAutoDispatchKind.Checkout,
                    tablePair.Key,
                    waiterTaskWaitTracker.GetWaitDuration(tablePair.Key)));
            }
        }

        private void CollectCleanDispatchCandidates(List<WaiterAutoDispatchCandidate> candidates)
        {
            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance.GetTableData(tablePair.Key);
                if (tableData == null || !tableData.isUnlocked)
                {
                    continue;
                }

                if ((TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.Cleaning)
                {
                    continue;
                }

                if (pendingUpgradeTableIds.Contains(tablePair.Key))
                {
                    continue;
                }

                if (IsTableBlockedByWaiterNap(tablePair.Key))
                {
                    continue;
                }

                if (assignedCleanTableIds.Contains(tablePair.Key))
                {
                    continue;
                }

                candidates.Add(new WaiterAutoDispatchCandidate(
                    WaiterAutoDispatchKind.Clean,
                    tablePair.Key,
                    waiterTaskWaitTracker.GetWaitDuration(tablePair.Key)));
            }
        }

        /// <summary>
        /// 是否还有未派清扫的待清理桌（点结账后常见）。有则禁止把空闲小二遣回默认站位。
        /// </summary>
        private bool HasUnassignedCleaningTables()
        {
            if (DataManager.Instance == null)
            {
                return false;
            }

            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance.GetTableData(tablePair.Key);
                if (tableData == null || !tableData.isUnlocked)
                {
                    continue;
                }

                if ((TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.Cleaning)
                {
                    continue;
                }

                if (pendingUpgradeTableIds.Contains(tablePair.Key)
                    || IsTableBlockedByWaiterNap(tablePair.Key)
                    || assignedCleanTableIds.Contains(tablePair.Key))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static int CompareWaiterAutoDispatchCandidates(
            WaiterAutoDispatchCandidate left,
            WaiterAutoDispatchCandidate right)
        {
            var waitCompare = right.WaitDuration.CompareTo(left.WaitDuration);
            if (waitCompare != 0)
            {
                return waitCompare;
            }

            var kindCompare = GetWaiterAutoDispatchKindPriority(left.Kind)
                .CompareTo(GetWaiterAutoDispatchKindPriority(right.Kind));
            if (kindCompare != 0)
            {
                return kindCompare;
            }

            return left.TableId.CompareTo(right.TableId);
        }

        /// <summary>
        /// 同等待时长时优先点单，其次上菜、结账、清扫。
        /// </summary>
        private static int GetWaiterAutoDispatchKindPriority(WaiterAutoDispatchKind kind)
        {
            return kind switch
            {
                WaiterAutoDispatchKind.Order => 0,
                WaiterAutoDispatchKind.Serve => 1,
                WaiterAutoDispatchKind.Checkout => 2,
                WaiterAutoDispatchKind.Clean => 3,
                _ => 4
            };
        }

        private bool CanDispatchWaiterAutoCandidate(WaiterAutoDispatchCandidate candidate, bool closingAuto)
        {
            var vipOrderReady = candidate.Kind == WaiterAutoDispatchKind.Order
                                && TableHasVipCustomer(candidate.TableId)
                                && VipGuestDishGuessService.ShouldAutoDispatchOrder(candidate.TableId);
            var ignoreSkillGate = closingAuto || vipOrderReady;
            WaiterTask task = candidate.Kind switch
            {
                WaiterAutoDispatchKind.Serve => new WaiterServeTask(candidate.TableId),
                WaiterAutoDispatchKind.Order => new WaiterOrderTask(candidate.TableId),
                WaiterAutoDispatchKind.Checkout => new WaiterCheckoutTask(candidate.TableId),
                WaiterAutoDispatchKind.Clean => new WaiterCleanTask(candidate.TableId),
                _ => null
            };

            return task != null && GetAvailableServiceWaiterVisual(task, ignoreSkillGate) != null;
        }

        private bool TryDispatchWaiterAutoCandidate(WaiterAutoDispatchCandidate candidate, bool closingAuto)
        {
            var vipOrderReady = candidate.Kind == WaiterAutoDispatchKind.Order
                                && TableHasVipCustomer(candidate.TableId)
                                && VipGuestDishGuessService.ShouldAutoDispatchOrder(candidate.TableId);

            return candidate.Kind switch
            {
                WaiterAutoDispatchKind.Serve => TryStartWaiterServeTask(candidate.TableId, playerDirected: closingAuto),
                WaiterAutoDispatchKind.Order => TryStartWaiterOrderTask(candidate.TableId, playerDirected: closingAuto || vipOrderReady),
                WaiterAutoDispatchKind.Checkout => TryStartWaiterCheckoutTask(candidate.TableId, playerDirected: closingAuto),
                WaiterAutoDispatchKind.Clean => TryStartWaiterCleanTask(candidate.TableId),
                _ => false
            };
        }

        /// <summary>
        /// 非首波开业瞬间刷客时，限制单次组人数上限（0 表示不额外限制）。
        /// </summary>
        private int spawnGroupSizeCap;

        /// <summary>
        /// 贵客科技刚解锁时，优先保证刷出一名贵客（营业中立即刷，否则下次开业/刷客时补刷）。
        /// </summary>
        private bool pendingGuaranteedVipSpawn;

        private const int OpeningInitialTableCount = 2;

        /// <summary>
        /// 启动高峰分批刷客：按预定人数每隔配置秒数进一批，直到达预定人数或排队节点已满且无空桌。
        /// </summary>
        private void TriggerPeakCustomerWave()
        {
            if (!IsBusinessActive
                || isClosingBusiness
                || customerTemplates.Count == 0
                || customerEntryPoint == null
                || DataManager.Instance == null)
            {
                return;
            }

            var tableCount = DataManager.Instance.GetUnlockedTableCount();
            var seatCapacity = GetUnlockedSeatCapacity();
            if (tableCount <= 0 || seatCapacity <= 0)
            {
                return;
            }

            // 总进客 = 高峰批次数 × 每批人数；批次配置固定取数组第 1 项。
            var batchCount = TbConfigRuntime.GetPeakCustomerBatchCount(1, 5);
            var guestsPerBatch = TbConfigRuntime.GetPeakCustomerBatchSize(2);
            var plannedGuests = Mathf.Max(1, batchCount * guestsPerBatch);

            // 抬高本波活跃上限，避免高峰客流被动态上限截断。
            var peakActiveNeed = activeCustomers.Count + plannedGuests;
            peakSpawnActiveCapacityOverride = Mathf.Max(peakActiveNeed, GetDynamicMaxActiveCustomers());

            peakSpawnRemainingGuests = plannedGuests;
            peakSpawnBatchActive = true;
            // 首批立即进客，之后按间隔分批。
            peakSpawnBatchCooldown = 0f;
            TickPeakCustomerBatch(0f);
        }

        /// <summary>
        /// 高峰分批推进：每批进配置人数（默认 2），间隔默认 2 秒。
        /// </summary>
        private void TickPeakCustomerBatch(float deltaTime)
        {
            if (!peakSpawnBatchActive)
            {
                return;
            }

            if (!IsBusinessActive
                || isClosingBusiness
                || customerTemplates.Count == 0
                || customerEntryPoint == null
                || peakSpawnRemainingGuests <= 0)
            {
                FinishPeakCustomerBatch();
                return;
            }

            peakSpawnBatchCooldown -= Mathf.Max(0f, deltaTime);
            if (peakSpawnBatchCooldown > 0f)
            {
                return;
            }

            if (IsPeakQueueFull())
            {
                FinishPeakCustomerBatch();
                return;
            }

            var batchSize = TbConfigRuntime.GetPeakCustomerBatchSize(2);
            var spawned = SpawnPeakBatchGuests(batchSize);
            if (spawned <= 0)
            {
                FinishPeakCustomerBatch();
                return;
            }

            if (peakSpawnRemainingGuests <= 0 || IsPeakQueueFull())
            {
                FinishPeakCustomerBatch();
                return;
            }

            peakSpawnBatchCooldown = TbConfigRuntime.GetPeakCustomerBatchIntervalSeconds(2f);
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        /// <summary>
        /// 本批进客：一律进排队，由前台点单批次再软预留空桌。
        /// </summary>
        private int SpawnPeakBatchGuests(int batchSize)
        {
            var toSpawn = Mathf.Min(Mathf.Max(1, batchSize), peakSpawnRemainingGuests);
            if (toSpawn <= 0)
            {
                return 0;
            }

            var beforeActive = activeCustomers.Count;
            if (!IsPeakQueueFull())
            {
                var queueSlots = Mathf.Max(0, GetPeakQueueCapacity() - queuedCustomers.Count);
                var queueGuests = Mathf.Min(toSpawn, queueSlots);
                if (queueGuests > 0)
                {
                    EnqueuePeakGuests(queueGuests);
                }
            }

            var spawned = Mathf.Clamp(activeCustomers.Count - beforeActive, 0, toSpawn);
            peakSpawnRemainingGuests = Mathf.Max(0, peakSpawnRemainingGuests - spawned);
            return spawned;
        }

        private int GetPeakQueueCapacity()
        {
            if (queuePointAnchors.Count > 0)
            {
                return queuePointAnchors.Count;
            }

            return GetEffectiveMaxQueueSize();
        }

        private bool IsPeakQueueFull()
        {
            return queuedCustomers.Count >= GetPeakQueueCapacity();
        }

        private void FinishPeakCustomerBatch()
        {
            if (!peakSpawnBatchActive && peakSpawnRemainingGuests <= 0 && peakSpawnActiveCapacityOverride == 0)
            {
                return;
            }

            peakSpawnBatchActive = false;
            peakSpawnRemainingGuests = 0;
            peakSpawnBatchCooldown = 0f;
            peakSpawnActiveCapacityOverride = 0;
            spawnGroupSizeCap = 0;
            TrySpawnGuaranteedVipCustomer();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        private void StopPeakCustomerBatch()
        {
            peakSpawnBatchActive = false;
            peakSpawnRemainingGuests = 0;
            peakSpawnBatchCooldown = 0f;
            peakSpawnActiveCapacityOverride = 0;
            spawnGroupSizeCap = 0;
        }

        /// <summary>
        /// 本轮唯一高峰：原「开业/续轮后按 peakCustomerWaveSeconds 定时触发」已停用。
        /// 现仅在酒楼升级后由 TryTriggerPeakWaveAfterTavernUpgrade 触发。
        /// </summary>
        private void TickPeakCustomerSecondWave()
        {
            // 节奏微调：注释固定时间开启高峰，保留方法便于以后恢复。
            return;
            /*
            if (!customerSpawnLoopActive
                || isClosingBusiness
                || peakSecondWaveTriggered
                || DataManager.Instance?.TavernData == null
                || !DataManager.Instance.TavernData.isOpen)
            {
                return;
            }

            var triggerSeconds = TbConfigRuntime.GetPeakCustomerWaveSeconds(
                DataManager.Instance.GetBusinessCycleRoundForConfig(),
                60f);
            if (businessOpenElapsedSeconds < triggerSeconds)
            {
                return;
            }

            peakSecondWaveTriggered = true;
            TriggerPeakCustomerWave();
            */
        }

        /// <summary>
        /// 酒楼升星后触发高峰（仅自家、营业中）。
        /// 刷客立刻开始；高峰提示等恭喜升级弹窗关闭后再出。
        /// </summary>
        private void TryTriggerPeakWaveAfterTavernUpgrade()
        {
            if (DataManager.Instance == null || DataManager.Instance.IsVisitingOtherTavern)
            {
                return;
            }

            if (!IsBusinessActive || isClosingBusiness)
            {
                return;
            }

            peakSecondWaveTriggered = true;
            TriggerPeakCustomerWave();
            HudOverlayService.RequestPeakTimeWarningAfterUpgradePopClosed();
        }

        /// <summary>
        /// 启动高峰并弹出「限时客流+200%」提示（开业 / 升星共用）。
        /// </summary>
        private void BeginPeakCustomerWaveWithWarning()
        {
            if (!IsBusinessActive || isClosingBusiness)
            {
                return;
            }

            peakSecondWaveTriggered = true;
            TriggerPeakCustomerWave();
            HudOverlayService.ShowPeakTimeWarning();
        }

        /// <summary>
        /// 按经营经过时间扫描低谷阈值：到达配置秒数且本账号未触发过的下标触发一次；每帧最多触发一档。
        /// </summary>
        private void TickValleyCustomerWave()
        {
            if (!customerSpawnLoopActive
                || isClosingBusiness
                || valleySpawnBatchActive
                || DataManager.Instance?.TavernData == null
                || !DataManager.Instance.TavernData.isOpen)
            {
                return;
            }

            var slotCount = TbConfigRuntime.GetValleyCustomerSecondWaveSlotCount();
            if (slotCount <= 0)
            {
                return;
            }

            for (var valleyIndex = 0; valleyIndex < slotCount; valleyIndex++)
            {
                if (DataManager.Instance.HasTriggeredValleyWave(valleyIndex))
                {
                    continue;
                }

                var triggerSeconds = TbConfigRuntime.GetValleyCustomerSecondWaveSecondsAt(valleyIndex, 120f);
                if (businessOpenElapsedSeconds < triggerSeconds)
                {
                    continue;
                }

                if (!TriggerValleyCustomerWave(valleyIndex))
                {
                    return;
                }

                DataManager.Instance.MarkValleyWaveTriggered(valleyIndex);
                return;
            }
        }

        /// <summary>
        /// 启动指定低谷档的分批刷客（批次数取同下标 valleyCustomerBatchCount）。
        /// </summary>
        private bool TriggerValleyCustomerWave(int valleyIndex)
        {
            if (!IsBusinessActive
                || isClosingBusiness
                || customerTemplates.Count == 0
                || customerEntryPoint == null
                || DataManager.Instance == null)
            {
                return false;
            }

            var tableCount = DataManager.Instance.GetUnlockedTableCount();
            var seatCapacity = GetUnlockedSeatCapacity();
            if (tableCount <= 0 || seatCapacity <= 0)
            {
                return false;
            }

            var batchCount = TbConfigRuntime.GetValleyCustomerBatchCountAt(valleyIndex, 2);
            var guestsPerBatch = TbConfigRuntime.GetValleyCustomerBatchSize(2);
            var plannedGuests = Mathf.Max(1, batchCount * guestsPerBatch);

            var valleyActiveNeed = activeCustomers.Count + plannedGuests;
            valleySpawnActiveCapacityOverride = Mathf.Max(valleyActiveNeed, GetDynamicMaxActiveCustomers());

            valleySpawnRemainingGuests = plannedGuests;
            valleySpawnBatchActive = true;
            valleySpawnBatchCooldown = 0f;
            TickValleyCustomerBatch(0f);
            return true;
        }

        /// <summary>
        /// 低谷分批推进。
        /// </summary>
        private void TickValleyCustomerBatch(float deltaTime)
        {
            if (!valleySpawnBatchActive)
            {
                return;
            }

            if (!IsBusinessActive
                || isClosingBusiness
                || customerTemplates.Count == 0
                || customerEntryPoint == null
                || valleySpawnRemainingGuests <= 0)
            {
                FinishValleyCustomerBatch();
                return;
            }

            valleySpawnBatchCooldown -= Mathf.Max(0f, deltaTime);
            if (valleySpawnBatchCooldown > 0f)
            {
                return;
            }

            if (IsPeakQueueFull())
            {
                FinishValleyCustomerBatch();
                return;
            }

            var batchSize = TbConfigRuntime.GetValleyCustomerBatchSize(2);
            var spawned = SpawnValleyBatchGuests(batchSize);
            if (spawned <= 0)
            {
                FinishValleyCustomerBatch();
                return;
            }

            if (valleySpawnRemainingGuests <= 0 || IsPeakQueueFull())
            {
                FinishValleyCustomerBatch();
                return;
            }

            valleySpawnBatchCooldown = TbConfigRuntime.GetValleyCustomerBatchIntervalSeconds(10f);
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        private int SpawnValleyBatchGuests(int batchSize)
        {
            var toSpawn = Mathf.Min(Mathf.Max(1, batchSize), valleySpawnRemainingGuests);
            if (toSpawn <= 0)
            {
                return 0;
            }

            var beforeActive = activeCustomers.Count;
            if (!IsPeakQueueFull())
            {
                var queueSlots = Mathf.Max(0, GetPeakQueueCapacity() - queuedCustomers.Count);
                var queueGuests = Mathf.Min(toSpawn, queueSlots);
                if (queueGuests > 0)
                {
                    EnqueuePeakGuests(queueGuests);
                }
            }

            var spawned = Mathf.Clamp(activeCustomers.Count - beforeActive, 0, toSpawn);
            valleySpawnRemainingGuests = Mathf.Max(0, valleySpawnRemainingGuests - spawned);
            return spawned;
        }

        private void FinishValleyCustomerBatch()
        {
            if (!valleySpawnBatchActive && valleySpawnRemainingGuests <= 0 && valleySpawnActiveCapacityOverride == 0)
            {
                return;
            }

            valleySpawnBatchActive = false;
            valleySpawnRemainingGuests = 0;
            valleySpawnBatchCooldown = 0f;
            valleySpawnActiveCapacityOverride = 0;
            spawnGroupSizeCap = 0;
            TrySpawnGuaranteedVipCustomer();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        private void StopValleyCustomerBatch()
        {
            valleySpawnBatchActive = false;
            valleySpawnRemainingGuests = 0;
            valleySpawnBatchCooldown = 0f;
            valleySpawnActiveCapacityOverride = 0;
        }

        /// <summary>
        /// 向一张空闲桌刷入一组客人（人数取桌容与剩余主客流的较小值）。
        /// </summary>
        private bool TrySpawnPeakGroupOntoIdleTable(ref int remainingMain)
        {
            if (remainingMain <= 0
                || !TryGetSpawnPosition(out var spawnPosition)
                || !TryResolveFirstIdleTable(1, out var tableId, out var table))
            {
                return false;
            }

            var tableCapacity = Mathf.Max(1, table.GetSeatCapacity());
            var groupSize = Mathf.Min(remainingMain, tableCapacity);
            // 桌容>=4 时按 4 人组；否则按 2 人组凑桌，避免单人占满。
            if (tableCapacity >= 4)
            {
                groupSize = Mathf.Min(remainingMain, 4);
            }
            else if (tableCapacity >= 2)
            {
                groupSize = Mathf.Min(remainingMain, 2);
            }

            if (groupSize <= 0 || table.GetSeatCapacity() < groupSize)
            {
                return false;
            }

            if (activeCustomers.Count + groupSize > GetPeakAwareMaxActiveCustomers())
            {
                return false;
            }

            var spawnedCustomers = customerSpawnService.SpawnCustomerGroup(
                groupSize,
                spawnPosition,
                GetGroupSpawnPosition,
                this,
                GetSpawnCustomerTemplates(),
                customerEntryPoint,
                GetExitPosition,
                PrepareSpawnedCustomer,
                customerFlowService,
                activeCustomers);
            if (spawnedCustomers == null)
            {
                return false;
            }

            AssignWaitHudGroupMembers(spawnedCustomers);
            if (!customerPlacementService.TryAssignSpawnedGroupToTable(
                    tableId,
                    table,
                    pendingUpgradeTableIds,
                    spawnedCustomers,
                    groupSize,
                    ResolveSeatApproach,
                    tableStateService,
                    customerFlowService,
                    tableCustomers,
                    tableCustomerGroups))
            {
                customerSpawnService.RollbackSpawnedCustomers(spawnedCustomers, activeCustomers);
                return false;
            }

            remainingMain -= groupSize;
            return true;
        }

        /// <summary>
        /// 高峰溢出/未入座客人整组进排队（允许突破常规队列上限到本波需求）。
        /// </summary>
        private void EnqueuePeakGuests(int guestCount)
        {
            if (guestCount <= 0 || !TryGetSpawnPosition(out _))
            {
                return;
            }

            var remaining = guestCount;
            while (remaining > 0)
            {
                if (!TryGetSpawnPosition(out var spawnPosition))
                {
                    break;
                }

                if (activeCustomers.Count >= GetPeakAwareMaxActiveCustomers())
                {
                    break;
                }

                // 与常时刷客同一套贵客/稀客概率；命中则只进 1 人。
                if (TrySpawnSpecialCustomerByChance(spawnPosition))
                {
                    remaining -= 1;
                    continue;
                }

                var groupSize = remaining >= 2 ? 2 : 1;
                if (activeCustomers.Count + groupSize > GetPeakAwareMaxActiveCustomers())
                {
                    groupSize = 1;
                }

                if (groupSize >= 2)
                {
                    var spawnedCustomers = customerSpawnService.SpawnCustomerGroup(
                        groupSize,
                        spawnPosition,
                        GetGroupSpawnPosition,
                        this,
                        GetSpawnCustomerTemplates(),
                        customerEntryPoint,
                        GetExitPosition,
                        PrepareSpawnedCustomer,
                        customerFlowService,
                        activeCustomers);
                    if (spawnedCustomers == null)
                    {
                        break;
                    }

                    for (var i = 0; i < spawnedCustomers.Count; i++)
                    {
                        var spawned = spawnedCustomers[i];
                        customerFlowService.EnqueueCustomer(queuedCustomers, spawned);
                        if (spawned != null)
                        {
                            waitSatisfactionTracker.OnCustomerQueued(spawned.GetInstanceID());
                        }
                    }

                    AssignWaitHudGroupMembers(spawnedCustomers);
                    for (var i = 0; i < spawnedCustomers.Count; i++)
                    {
                        TryDesignateFrontCounterOrderOnEnter(spawnedCustomers[i]);
                    }

                    remaining -= groupSize;
                    continue;
                }

                var single = SpawnCustomerRuntime(spawnPosition);
                if (single == null)
                {
                    break;
                }

                EnqueueSpawnedCustomer(single);
                remaining -= 1;
            }

            UpdateQueuePositions();
        }

        private int GetPeakAwareMaxActiveCustomers()
        {
            if (peakSpawnActiveCapacityOverride > 0)
            {
                return peakSpawnActiveCapacityOverride;
            }

            if (valleySpawnActiveCapacityOverride > 0)
            {
                return valleySpawnActiveCapacityOverride;
            }

            return GetDynamicMaxActiveCustomers();
        }

        /// <summary>
        /// 兼容旧调用名：开业瞬间不再刷高峰；定时高峰已停用，改由酒楼升级触发。
        /// </summary>
        private void SpawnInitialCustomers()
        {
        }

        /// <summary>
        /// 贵客科技解锁后通知场景：当前营业中立即尝试刷贵客，否则保留至下次开业。
        /// </summary>
        public void NotifyVipCustomerTechUnlocked()
        {
            pendingGuaranteedVipSpawn = true;
            TrySpawnGuaranteedVipCustomer();
        }

        /// <summary>
        /// 开业首波：按桌容整组进排队（不再直入座）。
        /// </summary>
        /// <returns>成功入队时返回 true。</returns>
        private bool TrySpawnOpeningInitialCustomerGroup()
        {
            if (!IsBusinessActive
                || isClosingBusiness
                || customerTemplates.Count == 0
                || customerEntryPoint == null
                || DataManager.Instance.GetUnlockedTableCount() == 0
                || !TryGetSpawnPosition(out var spawnPosition))
            {
                return false;
            }

            if (!TryResolveFirstIdleTable(2, out _, out var table))
            {
                // 无空桌时仍按常规排队组规模进队。
                var fallbackSize = GetPreferredQueuedSpawnGroupSize();
                return fallbackSize > 1
                    ? TryEnqueueCustomerGroup(fallbackSize, spawnPosition)
                    : SpawnCustomerIfPossible(allowVipSpawn: false);
            }

            var groupSize = ResolveOpeningGroupSizeForTable(table);
            if (groupSize <= 1)
            {
                var single = SpawnCustomerRuntime(spawnPosition);
                if (single == null)
                {
                    return false;
                }

                EnqueueSpawnedCustomer(single);
                Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
                return true;
            }

            if (!TryEnqueueCustomerGroup(groupSize, spawnPosition))
            {
                return false;
            }

            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
            return true;
        }

        /// <summary>
        /// 开业首波单桌人数：4 人桌满座 4 人，其余按 2 人桌处理。
        /// </summary>
        private static int ResolveOpeningGroupSizeForTable(TableArea table)
        {
            if (table == null)
            {
                return 2;
            }

            return table.GetSeatCapacity() >= 4 ? 4 : 2;
        }

        /// <summary>
        /// 取 tableId 最小、且容量满足的空闲桌作为首波入座目标。
        /// </summary>
        private bool TryResolveFirstIdleTable(int minSeatCapacity, out int tableId, out TableArea table)
        {
            tableId = 0;
            table = null;
            var bestTableId = int.MaxValue;

            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance.GetTableData(tablePair.Key);
                if (tableData == null
                    || !tableData.isUnlocked
                    || (TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.Idle
                    || pendingUpgradeTableIds.Contains(tablePair.Key)
                    || tablePair.Value == null
                    || tablePair.Value.GetSeatCapacity() < minSeatCapacity
                    || IsTableBlockedForNewSeating(tablePair.Key))
                {
                    continue;
                }

                if (tablePair.Key >= bestTableId)
                {
                    continue;
                }

                bestTableId = tablePair.Key;
                tableId = tablePair.Key;
                table = tablePair.Value;
            }

            return table != null;
        }

        /// <summary>
        /// 处理生成顾客如果可行相关逻辑。新客一律先入队，由前台点单批次再软预留入座。
        /// </summary>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private bool SpawnCustomerIfPossible(bool allowVipSpawn = true, float vipChanceMultiplier = 1f)
        {
            if (!IsBusinessActive)
            {
                return false;
            }

            // 有待卸拉客时先卸完，避免常规刷客占位导致类型对不上。
            if (DataManager.Instance != null && DataManager.Instance.GetPendingPulledCustomerCount() > 0)
            {
                return false;
            }

            if (allowVipSpawn && TrySpawnGuaranteedVipCustomer())
            {
                return true;
            }

            if (allowVipSpawn && TrySpawnVisitHotGuaranteedVip())
            {
                return true;
            }

            if (isClosingBusiness)
            {
                return false;
            }

            if ((customerTemplates.Count == 0 && vipCustomerTemplates.Count == 0 && rareCustomerTemplates.Count == 0)
                || (customerSpawnPoint == null && customerEntryPoint == null))
            {
                return false;
            }

            if (activeCustomers.Count >= GetDynamicMaxActiveCustomers())
            {
                return false;
            }

            if (DataManager.Instance.GetUnlockedTableCount() == 0)
            {
                return false;
            }

            if (queuedCustomers.Count >= GetEffectiveMaxQueueSize())
            {
                return false;
            }

            if (!TryGetSpawnPosition(out var spawnPosition))
            {
                return false;
            }

            return TrySpawnCustomerIntoQueue(spawnPosition, allowVipSpawn, vipChanceMultiplier);
        }

        /// <summary>
        /// 按表掷贵客/稀客：命中则刷出。贵客与普通客同一入口进队，不插队。
        /// 常时固定间隔与高峰/低谷分批共用。
        /// </summary>
        private bool TrySpawnSpecialCustomerByChance(Vector3 spawnPosition, float vipChanceMultiplier = 1f)
        {
            if (VipCustomerService.TrySpawnVip(
                    vipCustomerTemplates.Count > 0,
                    GetEffectiveVipSpawnChance(vipChanceMultiplier)))
            {
                return TrySpawnVipCustomer(spawnPosition);
            }

            if (!RareCustomerService.TrySpawnRare(
                    rareCustomerTemplates.Count > 0,
                    GetEffectiveRareSpawnChance()))
            {
                return false;
            }

            var rare = SpawnCustomerRuntime(ResolveVipEntrySpawnPosition(spawnPosition), asRare: true);
            if (rare == null)
            {
                return false;
            }

            EnqueueSpawnedCustomer(rare);
            return true;
        }

        /// <summary>
        /// 刷客：只进队，随后尝试前台点单软预留。
        /// </summary>
        private bool TrySpawnCustomerIntoQueue(Vector3 spawnPosition, bool allowVipSpawn = true, float vipChanceMultiplier = 1f)
        {
            if (allowVipSpawn && TrySpawnSpecialCustomerByChance(spawnPosition, vipChanceMultiplier))
            {
                Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
                return true;
            }

            if (customerTemplates.Count == 0)
            {
                return false;
            }

            var groupSize = GetPreferredQueuedSpawnGroupSize();
            if (spawnGroupSizeCap > 0 && groupSize > 0)
            {
                groupSize = Mathf.Min(groupSize, spawnGroupSizeCap);
            }

            if (groupSize <= 0)
            {
                return false;
            }

            if (groupSize > 1)
            {
                if (!TryEnqueueCustomerGroup(groupSize, spawnPosition))
                {
                    return false;
                }

                Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
                return true;
            }

            var runtimeController = SpawnCustomerRuntime(spawnPosition);
            if (runtimeController == null)
            {
                return false;
            }

            EnqueueSpawnedCustomer(runtimeController);
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
            return true;
        }

        private void EnqueueSpawnedCustomer(TavernCustomerRuntimeController runtimeController)
        {
            if (runtimeController == null)
            {
                return;
            }

            customerFlowService.EnqueueCustomer(queuedCustomers, runtimeController);
            waitSatisfactionTracker.OnCustomerQueued(runtimeController.GetInstanceID());
            TryDesignateFrontCounterOrderOnEnter(runtimeController);
            UpdateQueuePositions();
        }

        private bool CanAttemptVipCustomerSpawn(out Vector3 spawnPosition)
        {
            spawnPosition = default;
            if (isClosingBusiness
                || vipCustomerTemplates.Count == 0
                || customerEntryPoint == null)
            {
                return false;
            }

            if (!CanSpawnShopVipCustomerNow())
            {
                return false;
            }

            if (activeCustomers.Count >= GetDynamicMaxActiveCustomers())
            {
                return false;
            }

            if (DataManager.Instance.GetUnlockedTableCount() == 0 || queuedCustomers.Count >= GetEffectiveMaxQueueSize())
            {
                return false;
            }

            return TryGetSpawnPosition(out spawnPosition);
        }

        /// <summary>
        /// 贵客科技解锁后保证刷出一名贵客；条件不足时保留 pending 等待后续刷客重试。
        /// </summary>
        private bool TrySpawnGuaranteedVipCustomer()
        {
            if (!pendingGuaranteedVipSpawn || !IsBusinessActive)
            {
                return false;
            }

            if (!CanAttemptVipCustomerSpawn(out var spawnPosition))
            {
                return false;
            }

            if (!TrySpawnVipCustomer(spawnPosition))
            {
                return false;
            }

            pendingGuaranteedVipSpawn = false;
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
            return true;
        }

        /// <summary>
        /// 拜访爆满店：店内没有贵客时补刷一名，保证始终有贵客可看见。
        /// </summary>
        private bool TrySpawnVisitHotGuaranteedVip()
        {
            if (!IsVisitSimulationRunning
                || DataManager.Instance == null
                || !DataManager.Instance.IsVisitingHotTavern)
            {
                return false;
            }

            if (!CanAttemptVipCustomerSpawn(out var spawnPosition))
            {
                return false;
            }

            if (!TrySpawnVipCustomer(spawnPosition))
            {
                return false;
            }

            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
            return true;
        }

        /// <summary>
        /// 正常刷出贵客：与普通客同一出生点（EnterStartPoint）进店，排到队尾，并挂包厢气泡。
        /// 卸客贵客仍走轿子点，不经过此方法。
        /// </summary>
        private bool TrySpawnVipCustomer(Vector3 spawnPosition)
        {
            if (!CanSpawnShopVipCustomerNow())
            {
                return false;
            }

            var runtimeController = SpawnCustomerRuntime(spawnPosition, asVip: true);
            if (runtimeController == null)
            {
                return false;
            }

            EnqueueSpawnedCustomer(runtimeController);
            ShowVipGuestActionBubble(runtimeController);
            return true;
        }

        /// <summary>
        /// 贵客气泡：二楼未开放/已有贵客→置灰包厢（点提示）并按大堂入队；
        /// 已开放且空→可点包厢上二楼。不在上述锁定态显示大堂按钮。
        /// </summary>
        private void ShowVipGuestActionBubble(TavernCustomerRuntimeController customer)
        {
            if (customer == null || !customer.IsVip)
            {
                return;
            }

            ClearVipGuestActionBubble(customer);

            var secondFloorOpen = DataManager.Instance != null && DataManager.Instance.IsStairsUnlocked();
            var privateRoomOccupied = IsSecondFloorPrivateRoomOccupied();

            // 二楼未开放：置灰包厢 + 提示「未建造二楼」。
            if (!secondFloorOpen)
            {
                ShowLockedPrivateRoomBubble(customer, "未建造二楼");
                return;
            }

            // 二楼已有贵客（含刚点包厢尚未上楼）：置灰包厢 + 提示「二楼已有贵客」。
            if (privateRoomOccupied)
            {
                ShowLockedPrivateRoomBubble(customer, "二楼已有贵客");
                return;
            }

            // 二楼有空位：可点包厢；反馈「大堂太吵了」，常驻到模型消失。
            HudOverlayService.ShowVipLobbyNoisyReviewTip(customer.transform);
            var root = HudOverlayService.ShowVipGuestAction(
                customer.transform,
                new Vector3(0f, VipGuestActionView.DefaultHeadOffsetY, 0f),
                usePrivateRoom: true,
                () => OnVipGuestActionClicked(customer, usePrivateRoom: true));
            if (root != null)
            {
                vipGuestActionRoots[customer] = root;
            }
        }

        /// <summary>
        /// 一楼当场贵客：楼梯刚建成（二楼开放）时，把置灰包厢气泡刷成可点进包厢。
        /// </summary>
        private void SyncVipPrivateRoomBubblesWithSecondFloor()
        {
            if (DataManager.Instance == null || DataManager.Instance.IsVisitingOtherTavern)
            {
                return;
            }

            var unlocked = DataManager.Instance.IsStairsUnlocked();
            if (!vipBubbleStairsUnlockSynced)
            {
                vipBubbleStairsUnlockSynced = true;
                vipBubbleSyncedStairsUnlocked = unlocked;
                return;
            }

            var justOpened = unlocked && !vipBubbleSyncedStairsUnlocked;
            vipBubbleSyncedStairsUnlocked = unlocked;
            if (!justOpened)
            {
                return;
            }

            RefreshFirstFloorVipPrivateRoomBubbles();
        }

        private void RefreshFirstFloorVipPrivateRoomBubbles()
        {
            if (SceneFlowCoordinator.IsOnTavernSecondFloor())
            {
                return;
            }

            for (var index = 0; index < activeCustomers.Count; index++)
            {
                var customer = activeCustomers[index];
                if (customer == null
                    || !customer.IsVip
                    || customer.IsSeated
                    || customer.IsLeavingTavern
                    || pendingSecondFloorVipCustomers.Contains(customer))
                {
                    continue;
                }

                ShowVipGuestActionBubble(customer);
            }
        }

        /// <summary>
        /// 二楼包厢是否已占（存档已有贵客，或已有贵客点了包厢正在上楼）。
        /// </summary>
        private bool IsSecondFloorPrivateRoomOccupied()
        {
            return TavernSecondFloorVipService.HasSecondFloorVipGuest()
                   || pendingSecondFloorVipCustomers.Count > 0;
        }

        /// <summary>
        /// 包厢不可用：贵客直接大堂入队，头顶挂置灰包厢供点 tips（不收起）。
        /// </summary>
        private void ShowLockedPrivateRoomBubble(TavernCustomerRuntimeController customer, string tipMessage)
        {
            if (customer == null)
            {
                return;
            }

            // 已在队列中（正常刷出）：只挂置灰气泡，不插队。
            // 未入队（卸客等）：仍插到队首。
            if (queuedCustomers.Contains(customer))
            {
                customer.SetAwaitingVipFloorChoice(false);
            }
            else
            {
                EnqueueVipCustomerAtFront(customer);
            }

            var tip = string.IsNullOrWhiteSpace(tipMessage) ? "未建造二楼" : tipMessage;
            var lockedRoot = HudOverlayService.ShowVipGuestAction(
                customer.transform,
                new Vector3(0f, VipGuestActionView.DefaultHeadOffsetY, 0f),
                usePrivateRoom: true,
                onClick: () => HudOverlayService.ShowFloatingWarning(tip),
                privateRoomLocked: true);
            if (lockedRoot != null)
            {
                vipGuestActionRoots[customer] = lockedRoot;
            }
        }

        private void ClearVipGuestActionBubble(TavernCustomerRuntimeController customer)
        {
            if (customer == null)
            {
                return;
            }

            // 同步收起「大堂太吵了」等反馈气泡。
            if (customer.transform != null)
            {
                HudOverlayService.ReleaseCustomerReviewTip(customer.transform);
            }

            if (!vipGuestActionRoots.TryGetValue(customer, out var root))
            {
                return;
            }

            vipGuestActionRoots.Remove(customer);
            if (root != null)
            {
                HudOverlayService.ReleaseWorldHudItem(root);
            }
        }

        private void ClearAllVipGuestActionBubbles()
        {
            foreach (var pair in vipGuestActionRoots)
            {
                if (pair.Key != null && pair.Key.transform != null)
                {
                    HudOverlayService.ReleaseCustomerReviewTip(pair.Key.transform);
                }

                if (pair.Value != null)
                {
                    HudOverlayService.ReleaseWorldHudItem(pair.Value);
                }
            }

            vipGuestActionRoots.Clear();
        }

        private void OnVipGuestActionClicked(TavernCustomerRuntimeController customer, bool usePrivateRoom)
        {
            if (customer == null)
            {
                return;
            }

            if (usePrivateRoom)
            {
                // 已占包厢：拦截二次成功，并立刻把其余贵客气泡刷成置灰。
                if (IsSecondFloorPrivateRoomOccupied())
                {
                    RefreshFirstFloorVipPrivateRoomBubbles();
                    return;
                }

                // 一点包厢立刻占位，避免其它贵客在上楼途中仍能点成功。
                pendingSecondFloorVipCustomers.Add(customer);
                TavernSecondFloorVipService.SetSecondFloorVipGuest(true);
                ClearVipGuestActionBubble(customer);
                customer.SetAwaitingVipFloorChoice(false);
                GameAudioManager.PlayVipArrival();
                BeginVipGoToSecondFloorPrivateRoom(customer);
                RefreshFirstFloorVipPrivateRoomBubbles();
                return;
            }

            // 成功点大堂后立刻收起头顶气泡（含反馈字）。
            ClearVipGuestActionBubble(customer);
            customer.SetAwaitingVipFloorChoice(false);

            // 点大堂：已在队列则保持原位；未入队才入队尾（不插队）。
            if (!queuedCustomers.Contains(customer))
            {
                EnqueueSpawnedCustomer(customer);
            }

            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        /// <summary>
        /// 大堂：插入排队队首并刷新站位。
        /// </summary>
        private void EnqueueVipCustomerAtFront(TavernCustomerRuntimeController customer)
        {
            if (customer == null)
            {
                return;
            }

            queuedCustomers.Remove(customer);
            queuedCustomers.Insert(0, customer);
            waitSatisfactionTracker.OnCustomerQueued(customer.GetInstanceID());
            TryDesignateFrontCounterOrderOnEnter(customer);
            UpdateQueuePositions();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        /// <summary>
        /// 包厢：走向楼梯节点后消失，并写入二楼贵客存档。
        /// </summary>
        private void BeginVipGoToSecondFloorPrivateRoom(TavernCustomerRuntimeController customer)
        {
            if (customer == null)
            {
                return;
            }

            // 切楼前再缓存一次贵客预制体，避免静态引用丢失导致二楼无法生成。
            if (vipCustomerTemplates.Count > 0)
            {
                TavernSecondFloorVipService.CacheVipPrefab(vipCustomerTemplates[0]);
            }

            var stairs = ResolveVipStairsApproachPoint();
            var exitPos = stairs != null ? stairs.position : GetExitPosition(customer.transform.position);
            if (TryGetNavMeshPosition(exitPos, out var navPos))
            {
                exitPos = navPos;
            }

            pendingSecondFloorVipCustomers.Add(customer);
            customer.SetExitPosition(exitPos);
            customer.LeaveTavern();
        }

        private Transform ResolveVipStairsApproachPoint()
        {
            // 上二楼寻路终点：优先 VipStopPoint，再回退楼梯相关挂点。
            return FindSceneTransformByName("VipStopPoint")
                   ?? FindSceneTransformByName("louti")
                   ?? FindSceneTransformByName("楼梯建造")
                   ?? FindSceneTransformByName("Stairs")
                   ?? FindSceneTransformByName("楼梯");
        }

        /// <summary>
        /// 贵客出生点略向门口外侧偏移，保证有可见入场行走段。
        /// </summary>
        private Vector3 ResolveVipEntrySpawnPosition(Vector3 baseSpawnPosition)
        {
            if (customerEntryPoint == null)
            {
                return baseSpawnPosition;
            }

            var forward = customerEntryPoint.forward.sqrMagnitude > 0.1f
                ? customerEntryPoint.forward.normalized
                : Vector3.back;
            var outsideCandidate = baseSpawnPosition - forward * 2.4f;
            if (TryGetNavMeshPosition(outsideCandidate, out var outsideSpawn)
                && IsVipEntrySpawnPositionValid(outsideSpawn))
            {
                return outsideSpawn;
            }

            var fallbackCandidate = baseSpawnPosition - forward * 1.2f;
            if (TryGetNavMeshPosition(fallbackCandidate, out var fallbackSpawn)
                && IsVipEntrySpawnPositionValid(fallbackSpawn))
            {
                return fallbackSpawn;
            }

            return IsVipEntrySpawnPositionValid(baseSpawnPosition)
                ? baseSpawnPosition
                : baseSpawnPosition - forward * 1.2f;
        }

        /// <summary>
        /// 贵客出生点需离最近桌位足够远，避免 NavMesh 吸附后同帧被判定为已到达座位。
        /// </summary>
        private bool IsVipEntrySpawnPositionValid(Vector3 spawnPosition)
        {
            const float minDistanceFromSeat = 1.35f;
            var minDistanceSqr = minDistanceFromSeat * minDistanceFromSeat;
            foreach (var tablePair in AllTables)
            {
                var table = tablePair.Value;
                if (table == null)
                {
                    continue;
                }

                var tableData = DataManager.Instance.GetTableData(tablePair.Key);
                if (tableData == null || !tableData.isUnlocked)
                {
                    continue;
                }

                var seatCapacity = table.GetSeatCapacity();
                for (var seatIndex = 0; seatIndex < seatCapacity; seatIndex++)
                {
                    if (!table.TryGetSeatPoseByIndex(seatIndex, out var seatPosition, out _))
                    {
                        continue;
                    }

                    var delta = spawnPosition - seatPosition;
                    delta.y = 0f;
                    if (delta.sqrMagnitude < minDistanceSqr)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 尝试处理分配空闲桌位。待升级桌位会被跳过，避免新顾客在升级期间入座。
        /// </summary>
        /// <param name="customer">顾客对象。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private bool TryAssignFreeTable(TavernCustomerRuntimeController customer)
        {
            return customerPlacementService.TryAssignSingleCustomerToFreeTable(
                AllTables,
                pendingUpgradeTableIds,
                customer,
                ResolveSeatApproach,
                IsTableBlockedForNewSeating,
                tableStateService,
                customerFlowService,
                tableCustomers,
                tableCustomerGroups);
        }

        /// <summary>
        /// 更新排队位置。
        /// </summary>
        private void UpdateQueuePositions()
        {
            // 清掉已离队但仍残留在列表中的客人，避免占住下标 0/1 导致后排无法补位。
            for (var index = queuedCustomers.Count - 1; index >= 0; index--)
            {
                var customer = queuedCustomers[index];
                if (customer == null || customer.IsLeavingTavern)
                {
                    queuedCustomers.RemoveAt(index);
                }
            }

            customerPlacementService.UpdateQueuePositions(queuedCustomers, GetQueueTarget);
            // 站位变化后，强制队首 2 人占前台名额，并补开点单（刚补位者可能已站定却错过到达事件）。
            if (!isClosingBusiness && IsBusinessActive)
            {
                TryPromoteFrontCounterOrderSlots();
                TryStartFrontCounterOrdersForArrivedCandidates();
            }
        }

        /// <summary>
        /// 顾客开始离队时立刻出列：释放前台名额绑定，并让后排补上前排排队点。
        /// </summary>
        public void NotifyCustomerLeftQueue(TavernCustomerRuntimeController customer)
        {
            if (customer == null)
            {
                return;
            }

            var wasCandidate = customer.IsFrontCounterOrderCandidate;
            if (wasCandidate)
            {
                customer.SetFrontCounterOrderCandidate(false);
            }

            RemoveCustomerFromFrontCounterBinding(customer);

            var wasQueued = queuedCustomers.Remove(customer);
            if (wasQueued)
            {
                waitSatisfactionTracker.OnCustomerLeftQueue(customer.GetInstanceID());
                UpdateQueuePositions();
            }

            if (wasCandidate)
            {
                NotifyFrontCounterOrderSlotFreed();
            }
        }

        /// <summary>
        /// 获取排队目标位姿。
        /// 站定朝向：前两名（下标 0/1）Y=90 面朝柜台，其后 Y=180。
        /// </summary>
        private TavernQueueTarget GetQueueTarget(int index)
        {
            var standingRotation = ResolveQueueStandingRotation(index);
            if (index >= 0 && index < queuePointAnchors.Count && queuePointAnchors[index] != null)
            {
                var queuePoint = queuePointAnchors[index];
                var queuePosition = TryGetNavMeshPosition(queuePoint.position, out var navMeshPosition)
                    ? navMeshPosition
                    : queuePoint.position;
                return new TavernQueueTarget(queuePosition, standingRotation);
            }

            return new TavernQueueTarget(GetQueuePosition(index), standingRotation);
        }

        /// <summary>
        /// 获取排队站位。
        /// </summary>
        /// <param name="index">参数值。</param>
        /// <returns>返回方法执行后的结果。</returns>
        private Vector3 GetQueuePosition(int index)
        {
            if (customerEntryPoint == null)
            {
                return Vector3.zero;
            }

            var forward = customerEntryPoint.forward.sqrMagnitude > 0.1f ? customerEntryPoint.forward.normalized : Vector3.back;
            var right = customerEntryPoint.right.sqrMagnitude > 0.1f ? customerEntryPoint.right.normalized : Vector3.right;
            var laneOffset = right * (((index % 2 == 0) ? -1 : 1) * spawnLaneSpacing);
            var depthOffset = -forward * queueSpacing * (index + 1);
            var candidate = customerEntryPoint.position + laneOffset + depthOffset;
            return TryGetNavMeshPosition(candidate, out var queuePosition) ? queuePosition : customerEntryPoint.position;
        }

        /// <summary>
        /// 排队站定朝向：前两名面朝柜台（Y=90），后面客人 Y=180。
        /// </summary>
        private static Quaternion ResolveQueueStandingRotation(int index)
        {
            return Quaternion.Euler(0f, index < 2 ? 90f : 180f, 0f);
        }

        /// <summary>
        /// 点击收账气泡：跳过小二读条，立即完成收账；不打断、不改派小二。
        /// </summary>
        private bool TryInstantCompleteTableCheckout(int tableId)
        {
            if (DataManager.Instance == null)
            {
                return false;
            }

            var tableData = DataManager.Instance.GetTableData(tableId);
            if (tableData == null || !AllTables.TryGetValue(tableId, out var table) || table == null)
            {
                return false;
            }

            if ((TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.Checkout)
            {
                return false;
            }

            if (table.linkedUI == null)
            {
                return false;
            }

            // 结账纯玩家行为：不打断小二；桌变 Cleaning 后立刻尝试派空闲小二清扫。
            CompleteCheckoutWithIncome(tableId, servingWaiter: null);
            TryHandleOneWaiterService();
            return true;
        }

        /// <summary>
        /// 处理完成结账相关逻辑。
        /// </summary>
        /// <param name="桌位编号">数据编号。</param>
        private void CompleteCheckoutWithIncome(int tableId, GameObject servingWaiter = null)
        {
            ClearCheckoutRuntimeTextOverride(tableId);
            var skipCoinFly = ConsumeCheckoutCoinFlyPreplayed(tableId);

            if (!TryPrepareCheckoutFinalization(tableId, out var table, out var customers, out var income, servingWaiter))
            {
                return;
            }

            var hasVip = TableHasVipCustomer(tableId);
            CoinDisplayRefreshCoordinator.DeferGoldRefreshUntilFlyComplete();

            if (hasVip)
            {
                GameAudioManager.PlayVipCheckoutCoins();
            }
            else
            {
                GameAudioManager.PlayCheckoutCoins();
            }
            if (!skipCoinFly)
            {
                TryPlayCoinFlyToTop(
                    table.linkedUI != null ? table.linkedUI.GetCoinFlySourceTransform() : null,
                    CoinDisplayRefreshCoordinator.NotifyFlyComplete);
            }
            else
            {
                CoinDisplayRefreshCoordinator.NotifyFlyComplete();
            }

            DataManager.Instance.ChangeCoinNum(income);
            DataManager.Instance.AddTableIncome(tableId, income);
            if (hasVip)
            {
                DataManager.Instance.RecordVipCheckout(income);
            }

            // 完成一桌发放声望（普客/贵客读 Config）。
            DataManager.Instance.AddPrestigeForCompletedTable(hasVip);
            // 开业任务 TakeMoney：有收入结账计 1 次（仅当前任务为该类时累计）。
            DataManager.Instance.RecordTakeMoneyCheckout();

            RecordTableVisitSatisfaction(tableId, customers);
            FinalizeCheckout(tableId, table, customers);
        }

        private void CompleteCheckoutWithoutIncome(int tableId)
        {
            ClearCheckoutRuntimeTextOverride(tableId);
            ClearCheckoutCoinFlyPreplayed(tableId);

            if (!TryPrepareCheckoutFinalization(tableId, out var table, out var customers, out _))
            {
                return;
            }

            DataManager.Instance.AddTableIncome(tableId, 0);
            RecordTableVisitSatisfaction(tableId, customers);
            FinalizeCheckout(tableId, table, customers);
        }

        private void RecordTableVisitSatisfaction(int tableId, List<TavernCustomerRuntimeController> customers)
        {
            var waits = waitSatisfactionTracker.ConsumeTable(tableId);
            var guestCount = customers != null ? Mathf.Max(1, customers.Count) : 1;
            DataManager.Instance?.RecordVisitSatisfactionFromWaits(
                waits.QueueSeconds,
                waits.OrderSeconds,
                waits.ServeSeconds,
                waits.CheckoutSeconds,
                guestCount);
        }

        private bool TryPrepareCheckoutFinalization(
            int tableId,
            out TableArea table,
            out List<TavernCustomerRuntimeController> customers,
            out int income,
            GameObject servingWaiter = null)
        {
            customers = null;
            income = 0;
            if (!AllTables.TryGetValue(tableId, out table) || table == null)
            {
                return false;
            }

            var groupSize = TryGetTableCustomerGroup(tableId, out customers) ? Mathf.Max(1, customers.Count) : 1;
            var unitPrice = ResolveTableCheckoutUnitPrice();
            var hasVip = false;
            if (customers != null)
            {
                for (var i = 0; i < customers.Count; i++)
                {
                    if (customers[i] != null && customers[i].IsVip)
                    {
                        hasVip = true;
                        break;
                    }
                }
            }

            // 普客按基础价；有贵客时仅贵客座位 × Config 倍率。
            var vipMultiplier = hasVip
                ? VipCustomerService.ResolveVipCheckoutMultiplier()
                : 1f;
            var seatIncome = VipCustomerService.ResolveCheckoutIncomeBySeats(
                unitPrice,
                customers,
                groupSize,
                vipMultiplier);
            income = ApplyPriceCoefficientToIncome(seatIncome, servingWaiter);

            return true;
        }

        /// <summary>
        /// 单人结账基础价：Config 按酒楼等级取单价，再 ±浮动。
        /// </summary>
        private int ResolveTableCheckoutUnitPrice()
        {
            var tavernLevel = DataManager.Instance != null ? DataManager.Instance.GetTavernLevel() : 1;
            var unitPrice = TbConfigRuntime.GetTableCheckoutIncomeForLevel(tavernLevel, tableCheckoutIncome);
            if (DataManager.Instance != null)
            {
                unitPrice = DataManager.Instance.ApplyActiveTavernMenuCheckoutUnitPrice(unitPrice);
            }
            var floatRange = TbConfigRuntime.GetTableCheckoutIncomeFloatRange(40);
            var floatedUnit = unitPrice;
            if (floatRange > 0)
            {
                floatedUnit = unitPrice + Random.Range(-floatRange, floatRange + 1);
            }

            return Mathf.Max(1, floatedUnit);
        }

        /// <summary>
        /// 单桌结账基础收入：单价 × 人数（无贵客加成时的兼容入口）。
        /// </summary>
        private int ResolveTableCheckoutBaseIncome(int groupSize)
        {
            return ResolveTableCheckoutUnitPrice() * Mathf.Max(1, groupSize);
        }

        private void FinalizeCheckout(int tableId, TableArea table, List<TavernCustomerRuntimeController> customers)
        {
            if (TableHasVipCustomer(tableId))
            {
                VipGuestDishGuessService.ClearTableSession(tableId);
            }

            tableStateService.SetCleaning(tableId, table, "等待清理", dispatchRuntimeChanged: false);
            TryRevealTableLv2UpgradeFeature();
            if (customers != null)
            {
                for (var index = 0; index < customers.Count; index++)
                {
                    if (customers[index] != null)
                    {
                        customers[index].LeaveTavern();
                    }
                }
            }

            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        private const int DefaultTableLv2UpgradeUnlockCheckoutCount = 4;

        /// <summary>
        /// 达成配置的累计结账次数后直接开放桌子升级功能，不再弹出解锁提示。
        /// </summary>
        private void TryRevealTableLv2UpgradeFeature()
        {
            if (DataManager.Instance == null || DataManager.Instance.IsTableLv2UpgradeUnlocked())
            {
                return;
            }

            var requiredCheckoutCount = TbConfigRuntime.GetTableLv2UpgradeUnlockCheckoutCount(
                DefaultTableLv2UpgradeUnlockCheckoutCount);
            if (DataManager.Instance.TavernData == null
                || DataManager.Instance.TavernData.totalServedCustomers < requiredCheckoutCount)
            {
                return;
            }

            DataManager.Instance.UnlockTableLv2Upgrade();
        }

        /// <summary>
        /// 处理桌位清扫完成流程。
        /// </summary>
        /// <param name="桌位编号">数据编号。</param>
        private void FinishCleaning(int tableId)
        {
            if (!AllTables.ContainsKey(tableId))
            {
                return;
            }

            GameAudioManager.StopWiping(tableId);
            StopAutoClean(tableId);
            if (activeCleanSmokeEffects.TryGetValue(tableId, out var smokeEffect))
            {
                activeCleanSmokeEffects.Remove(tableId);
                if (smokeEffect != null)
                {
                    Destroy(smokeEffect);
                }
            }

            customerFlowService.ClearTableAssignments(tableCustomers, tableCustomerGroups, tableId);
            CancelFrontCounterOrderRoutine(tableId);
            frontCounterOrderBindings.Remove(tableId);
            tableStateService.SetIdle(tableId, AllTables[tableId], dispatchRuntimeChanged: false);

            TryPrepareFrontCounterOrders();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

    

        /// <summary>
        /// 停止自动清扫流程。
        /// </summary>
        /// <param name="桌位编号">数据编号。</param>
        private void StopAutoClean(int tableId)
        {
            if (!autoCleanRoutines.TryGetValue(tableId, out var routine) || routine == null)
            {
                return;
            }

            StopCoroutine(routine);
            autoCleanRoutines.Remove(tableId);
        }


        /// <summary>
        /// 尝试给排队顾客做前台点单软预留（事件驱动入口，兼容旧调用名）。
        /// </summary>
        private void TryAssignQueuedCustomers()
        {
            TryPromoteFrontCounterOrderSlots();
            TryStartFrontCounterOrdersForArrivedCandidates();
        }

        /// <summary>
        /// 客人进店入队时占用前台点单名额（最多 2）：此时已决定是否为前 2，到排队位再开点单。
        /// </summary>
        private void TryDesignateFrontCounterOrderOnEnter(TavernCustomerRuntimeController customer)
        {
            if (customer == null || isClosingBusiness || !IsBusinessActive)
            {
                return;
            }

            if (customer.IsFrontCounterOrderCandidate || IsCustomerBoundToFrontCounterOrder(customer))
            {
                return;
            }

            if (CountFrontCounterOrderCandidates() >= FrontCounterOrderSlotCount)
            {
                return;
            }

            customer.SetFrontCounterOrderCandidate(true);
        }

        private int CountFrontCounterOrderCandidates()
        {
            var count = 0;
            for (var index = 0; index < queuedCustomers.Count; index++)
            {
                var customer = queuedCustomers[index];
                if (customer != null && customer.IsFrontCounterOrderCandidate)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 名额空出后：按当前排队顺序补标下一批前 2，并尝试为已站定者开点单。
        /// </summary>
        public void NotifyFrontCounterOrderSlotFreed()
        {
            TryPromoteFrontCounterOrderSlots();
            TryStartFrontCounterOrdersForArrivedCandidates();
        }

        /// <summary>
        /// 已标记前 2 的客人走到排队点位后通知前台点单（非 Update 判定）。
        /// </summary>
        public void NotifyCustomerReachedQueueSlotForFrontOrder(TavernCustomerRuntimeController customer)
        {
            if (customer == null || !customer.IsFrontCounterOrderCandidate)
            {
                return;
            }

            TryStartFrontCounterOrdersForArrivedCandidates();
        }

        /// <summary>
        /// 前台名额与队列下标对齐：始终让前 2 人（及仍在软预留中的人）占名额，其余取消。
        /// 设计是「队首 2 个客人」而非「整组齐人」。
        /// </summary>
        private void TryPromoteFrontCounterOrderSlots()
        {
            if (isClosingBusiness || !IsBusinessActive)
            {
                return;
            }

            for (var index = 0; index < queuedCustomers.Count; index++)
            {
                var customer = queuedCustomers[index];
                if (customer == null || customer.IsLeavingTavern)
                {
                    continue;
                }

                var shouldBeCandidate = index < FrontCounterOrderSlotCount
                                        || IsCustomerBoundToFrontCounterOrder(customer);
                if (shouldBeCandidate)
                {
                    if (!customer.IsFrontCounterOrderCandidate)
                    {
                        customer.SetFrontCounterOrderCandidate(true);
                    }
                }
                else if (customer.IsFrontCounterOrderCandidate)
                {
                    customer.SetFrontCounterOrderCandidate(false);
                }
            }
        }

        /// <summary>
        /// 为已站定的前台名额客人软预留空桌并开点单进度。
        /// </summary>
        private void TryStartFrontCounterOrdersForArrivedCandidates()
        {
            if (isClosingBusiness)
            {
                customerFlowService.ClearTrackingForClosing(queuedCustomers, null, null);
                ClearAllFrontCounterOrderRoutines();
                frontCounterOrderBindings.Clear();
                pendingVipMenuRejectAfterSeatTableIds.Clear();
                ClearFrontCounterOrderCandidates();
                return;
            }

            if (!IsBusinessActive || queuedCustomers.Count <= 0)
            {
                return;
            }

            PruneInvalidFrontCounterOrderBindings();

            var alreadyBound = CountFrontCounterBoundCustomers();
            var availableSlots = FrontCounterOrderSlotCount - alreadyBound;
            if (availableSlots <= 0)
            {
                return;
            }

            // 同一帧可能连续点多桌（最多补满前台名额）。
            for (var guard = 0; guard < FrontCounterOrderSlotCount; guard++)
            {
                alreadyBound = CountFrontCounterBoundCustomers();
                availableSlots = FrontCounterOrderSlotCount - alreadyBound;
                if (availableSlots <= 0)
                {
                    break;
                }

                var batch = BuildArrivedFrontCounterOrderBatch(availableSlots);
                if (batch.Count <= 0)
                {
                    break;
                }

                if (TrySoftReserveTableForFrontCounterOrder(batch))
                {
                    continue;
                }

                if (batch.Count > 1)
                {
                    var single = new List<TavernCustomerRuntimeController> { batch[0] };
                    if (TrySoftReserveTableForFrontCounterOrder(single))
                    {
                        continue;
                    }
                }

                break;
            }
        }

        /// <summary>
        /// 清理已失效的前台点单绑定：客人离队/销毁，或桌已回到 Idle 却仍占名额。
        /// </summary>
        private void PruneInvalidFrontCounterOrderBindings()
        {
            if (frontCounterOrderBindings.Count <= 0)
            {
                return;
            }

            var staleTableIds = new List<int>();
            foreach (var pair in frontCounterOrderBindings)
            {
                var tableId = pair.Key;
                var list = pair.Value;
                if (list == null)
                {
                    staleTableIds.Add(tableId);
                    continue;
                }

                list.RemoveAll(item => item == null || !queuedCustomers.Contains(item));
                if (list.Count <= 0)
                {
                    staleTableIds.Add(tableId);
                    continue;
                }

                var tableData = DataManager.Instance?.GetTableData(tableId);
                if (tableData == null
                    || (TavernTableRuntimeState)tableData.runtimeState == TavernTableRuntimeState.Idle)
                {
                    staleTableIds.Add(tableId);
                    continue;
                }

                // 绑定仍在但协程已死：名额被僵尸占满，整队永远不开单。
                if (!frontCounterOrderRoutines.ContainsKey(tableId)
                    && (TavernTableRuntimeState)tableData.runtimeState == TavernTableRuntimeState.WaitingOrder)
                {
                    staleTableIds.Add(tableId);
                }
            }

            for (var index = 0; index < staleTableIds.Count; index++)
            {
                var tableId = staleTableIds[index];
                var shouldReset = IsTableInState(tableId, TavernTableRuntimeState.WaitingOrder)
                                  || IsTableInState(tableId, TavernTableRuntimeState.Reserved);
                AbortFrontCounterOrderBinding(tableId, resetTableToIdle: shouldReset);
            }
        }

        /// <summary>
        /// 中止某桌前台点单绑定：释放名额，客人回到可再点单状态。
        /// </summary>
        private void AbortFrontCounterOrderBinding(int tableId, bool resetTableToIdle)
        {
            if (tableId <= 0)
            {
                return;
            }

            CancelFrontCounterOrderRoutine(tableId);
            if (frontCounterOrderBindings.TryGetValue(tableId, out var customers) && customers != null)
            {
                for (var i = 0; i < customers.Count; i++)
                {
                    customers[i]?.ClearFrontCounterOrderBind();
                }
            }

            frontCounterOrderBindings.Remove(tableId);
            tableCustomerGroups.Remove(tableId);
            tableCustomers.Remove(tableId);
            pendingVipMenuRejectAfterSeatTableIds.Remove(tableId);

            if (resetTableToIdle
                && AllTables.TryGetValue(tableId, out var table)
                && table != null
                && (IsTableInState(tableId, TavernTableRuntimeState.WaitingOrder)
                    || IsTableInState(tableId, TavernTableRuntimeState.Reserved)))
            {
                tableStateService.SetIdle(tableId, table, dispatchRuntimeChanged: false);
            }
        }

        /// <summary>
        /// 收集已站定、已占名额且未绑定的客人（按队列人头，不等同组齐人）。
        /// </summary>
        private List<TavernCustomerRuntimeController> BuildArrivedFrontCounterOrderBatch(int maxCount)
        {
            var batch = new List<TavernCustomerRuntimeController>(Mathf.Max(0, maxCount));
            if (maxCount <= 0)
            {
                return batch;
            }

            for (var index = 0; index < queuedCustomers.Count && batch.Count < maxCount; index++)
            {
                var customer = queuedCustomers[index];
                if (customer == null || IsCustomerBoundToFrontCounterOrder(customer))
                {
                    continue;
                }

                if (!customer.IsFrontCounterOrderCandidate)
                {
                    // 名额应落在队首；后面若尚未对齐则不再往后抢人。
                    break;
                }

                if (!customer.IsQueueSlotReady)
                {
                    // 名额客人尚未站定：等其到达事件，不跳过后排。
                    break;
                }

                batch.Add(customer);
            }

            return batch;
        }

        private void ClearFrontCounterOrderCandidates()
        {
            for (var index = 0; index < queuedCustomers.Count; index++)
            {
                queuedCustomers[index]?.SetFrontCounterOrderCandidate(false);
            }
        }

        /// <summary>
        /// 兼容旧调用：改为名额补给 + 已站定者开点单（禁止当作每帧轮询）。
        /// </summary>
        private void TryPrepareFrontCounterOrders()
        {
            TryPromoteFrontCounterOrderSlots();
            TryStartFrontCounterOrdersForArrivedCandidates();
        }

        /// <summary>
        /// 低频兜底：强制站定超时队首 + 清理僵尸绑定 + 再尝试开点单（不全量轮询，仅防事件链断裂）。
        /// </summary>
        private void TickFrontCounterOrderPreparation(float deltaTime)
        {
            if (isClosingBusiness || !IsBusinessActive || queuedCustomers.Count <= 0)
            {
                queuedCustomerAssignCooldown = 0f;
                return;
            }

            queuedCustomerAssignCooldown -= Mathf.Max(0f, deltaTime);
            if (queuedCustomerAssignCooldown > 0f)
            {
                return;
            }

            queuedCustomerAssignCooldown = 0.5f;
            ForceReadyStalledFrontCounterCandidates();
            PruneInvalidFrontCounterOrderBindings();
            TryPromoteFrontCounterOrderSlots();
            TryStartFrontCounterOrdersForArrivedCandidates();
        }

        /// <summary>
        /// 前台名额客人寻路异常时强制站定，避免 BuildArrived 在队首 break 堵死整队。
        /// </summary>
        private void ForceReadyStalledFrontCounterCandidates()
        {
            var checkedCount = 0;
            for (var index = 0; index < queuedCustomers.Count && checkedCount < FrontCounterOrderSlotCount; index++)
            {
                var customer = queuedCustomers[index];
                if (customer == null || customer.IsLeavingTavern)
                {
                    continue;
                }

                if (!customer.IsFrontCounterOrderCandidate)
                {
                    break;
                }

                checkedCount++;
                if (customer.IsQueueSlotReady || IsCustomerBoundToFrontCounterOrder(customer))
                {
                    continue;
                }

                customer.TryForceMarkQueueSlotReadyIfStalled();
            }
        }

        private int CountFrontCounterBoundCustomers()
        {
            var count = 0;
            foreach (var pair in frontCounterOrderBindings)
            {
                var list = pair.Value;
                if (list == null)
                {
                    continue;
                }

                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] != null)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private bool IsCustomerBoundToFrontCounterOrder(TavernCustomerRuntimeController customer)
        {
            if (customer == null)
            {
                return false;
            }

            foreach (var pair in frontCounterOrderBindings)
            {
                var list = pair.Value;
                if (list == null)
                {
                    continue;
                }

                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] == customer)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 为前台点单批次找空闲桌：SetReserved → 登记分组 → WaitingOrder，不 AssignToTable。
        /// </summary>
        private bool TrySoftReserveTableForFrontCounterOrder(List<TavernCustomerRuntimeController> batch)
        {
            if (batch == null || batch.Count <= 0)
            {
                return false;
            }

            var requiredSeats = batch.Count;
            foreach (var tablePair in AllTables)
            {
                var tableId = tablePair.Key;
                var table = tablePair.Value;
                var tableData = DataManager.Instance.GetTableData(tableId);
                if (tableData == null
                    || !tableData.isUnlocked
                    || (TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.Idle
                    || pendingUpgradeTableIds.Contains(tableId)
                    || IsTableBlockedForNewSeating(tableId)
                    || table == null
                    || table.GetSeatCapacity() < requiredSeats
                    || frontCounterOrderBindings.ContainsKey(tableId))
                {
                    continue;
                }

                var boundCustomers = new List<TavernCustomerRuntimeController>(requiredSeats);
                for (var seatIndex = 0; seatIndex < requiredSeats; seatIndex++)
                {
                    var customer = batch[seatIndex];
                    if (customer == null)
                    {
                        return false;
                    }

                    customer.BindTableForFrontCounterOrder(tableId, seatIndex);
                    boundCustomers.Add(customer);
                }

                tableStateService.SetReserved(tableId, table, dispatchRuntimeChanged: false);
                customerFlowService.RegisterTableGroup(
                    tableCustomers,
                    tableCustomerGroups,
                    tableId,
                    boundCustomers);
                frontCounterOrderBindings[tableId] = boundCustomers;
                tableStateService.SetWaitingOrder(tableId, table, dispatchRuntimeChanged: false);
                waitSatisfactionTracker.OnWaitingOrder(tableId);
                HideWaitingOrderBubbleForTable(tableId);
                StartFrontCounterOrderProcess(tableId);
                Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 启动前台点单：计时完成后直接建厨工单并入座，不占用小二。
        /// </summary>
        /// <param name="tableId">桌位编号。</param>
        /// <param name="durationOverride">指定剩余时长（秒）；&lt;=0 时用有效点单时长。</param>
        private void StartFrontCounterOrderProcess(int tableId, float durationOverride = -1f)
        {
            if (tableId <= 0)
            {
                return;
            }

            CancelFrontCounterOrderRoutine(tableId);
            // 前台软预留不走桌边猜菜点击；清掉残留交互，避免 RequiresPlayerOrderClick 永久空等。
            VipGuestDishGuessService.ClearOrderInteraction(tableId);

            var duration = durationOverride > 0f
                ? durationOverride
                : GetEffectiveWaiterOrderDuration();
            duration = Mathf.Max(0.05f, duration);
            BeginShopkeeperOrderProgress(tableId, duration);
            frontCounterOrderSessions[tableId] = new FrontCounterOrderSession
            {
                EndsAt = Time.time + duration
            };
            // 兼容旧查询：登记占位，不再启动 WaitForSeconds 协程。
            frontCounterOrderRoutines[tableId] = null;
        }

        private void CancelFrontCounterOrderRoutine(int tableId)
        {
            frontCounterOrderSessions.Remove(tableId);
            frontCounterOrderRoutines.Remove(tableId);
            EndShopkeeperOrderProgress(tableId);
        }

        private void ClearAllFrontCounterOrderRoutines()
        {
            frontCounterOrderSessions.Clear();
            frontCounterOrderRoutines.Clear();
            ClearShopkeeperOrderProgress();
        }

        /// <summary>
        /// 逐帧推进前台点单计时；到期后完成入座，全程不 StopCoroutine。
        /// </summary>
        private void TickFrontCounterOrderSessions()
        {
            if (frontCounterOrderSessions.Count <= 0)
            {
                return;
            }

            frontCounterOrderTickBuffer.Clear();
            foreach (var pair in frontCounterOrderSessions)
            {
                frontCounterOrderTickBuffer.Add(pair.Key);
            }

            for (var index = 0; index < frontCounterOrderTickBuffer.Count; index++)
            {
                var tableId = frontCounterOrderTickBuffer[index];
                if (!frontCounterOrderSessions.TryGetValue(tableId, out var session) || session == null)
                {
                    frontCounterOrderSessions.Remove(tableId);
                    frontCounterOrderRoutines.Remove(tableId);
                    continue;
                }

                if (Time.time < session.EndsAt)
                {
                    continue;
                }

                frontCounterOrderSessions.Remove(tableId);
                frontCounterOrderRoutines.Remove(tableId);
                EndShopkeeperOrderProgress(tableId);

                if (!IsBusinessActive || !IsTableInState(tableId, TavernTableRuntimeState.WaitingOrder))
                {
                    AbortFrontCounterOrderBinding(tableId, resetTableToIdle: true);
                    TryPromoteFrontCounterOrderSlots();
                    TryStartFrontCounterOrdersForArrivedCandidates();
                    continue;
                }

                CompleteFrontCounterOrder(tableId);
            }
        }

        /// <summary>
        /// 前台点单开始：掌柜头顶进度条 + FrontTableOrder「点单中」状态文案。
        /// </summary>
        private void BeginShopkeeperOrderProgress(int tableId, float duration)
        {
            if (tableId <= 0 || duration <= 0f)
            {
                return;
            }

            frontCounterOrderProgressStarts[tableId] = Time.time;
            frontCounterOrderProgressDurations[tableId] = duration;
            EnsureShopkeeperOrderProgressHud();
            RefreshFrontTableOrderStatusDisplay();
        }

        private void EndShopkeeperOrderProgress(int tableId)
        {
            if (tableId <= 0)
            {
                return;
            }

            frontCounterOrderProgressStarts.Remove(tableId);
            frontCounterOrderProgressDurations.Remove(tableId);
            if (frontCounterOrderProgressStarts.Count <= 0)
            {
                ReleaseShopkeeperOrderProgressHud();
            }

            RefreshFrontTableOrderStatusDisplay();
        }

        private void ClearShopkeeperOrderProgress()
        {
            frontCounterOrderProgressStarts.Clear();
            frontCounterOrderProgressDurations.Clear();
            ReleaseShopkeeperOrderProgressHud();
            RefreshFrontTableOrderStatusDisplay();
        }

        private void EnsureShopkeeperOrderProgressHud()
        {
            var shopkeeper = ResolveShopkeeperOrderProgressTarget();
            if (shopkeeper == null)
            {
                return;
            }

            if (shopkeeperOrderProgressHud != null)
            {
                return;
            }

            shopkeeperOrderProgressHud = HudOverlayService.ShowShopkeeperOrderProgress(
                shopkeeper.transform,
                GetShopkeeperOrderProgress01,
                new Vector3(0f, TavernWorldRuntimeHudLayout.ChefProgressHeightOffset, 0f),
                waiterOrderingIcon);
        }

        private void ReleaseShopkeeperOrderProgressHud()
        {
            if (shopkeeperOrderProgressHud == null)
            {
                return;
            }

            HudOverlayService.ReleaseWorldHudItem(shopkeeperOrderProgressHud);
            shopkeeperOrderProgressHud = null;
        }

        private GameObject ResolveShopkeeperOrderProgressTarget()
        {
            if (guideStaffVisuals.TryGetValue(GuideShopkeeperVisualKey, out var shopkeeper) && shopkeeper != null)
            {
                return shopkeeper;
            }

            return null;
        }

        /// <summary>
        /// 有前台点单进行中时，在 Objects/FrontTableOrder 显示「点单中」（类似桌位状态文案）。
        /// </summary>
        private void RefreshFrontTableOrderStatusDisplay()
        {
            var ordering = frontCounterOrderProgressStarts.Count > 0;
            if (!ordering)
            {
                if (frontTableOrderStatusLabel?.rectTransform != null)
                {
                    frontTableOrderStatusLabel.rectTransform.gameObject.SetActive(false);
                }

                return;
            }

            EnsureFrontTableOrderStatusLabel();
            if (frontTableOrderStatusLabel?.rectTransform == null)
            {
                return;
            }

            frontTableOrderStatusLabel.rectTransform.gameObject.SetActive(true);
            ApplyFrontTableOrderStatusText(frontTableOrderStatusLabel, FrontTableOrderStatusText);
        }

        private static void ApplyFrontTableOrderStatusText(GuideWorldLabel label, string content)
        {
            if (label == null)
            {
                return;
            }

            if (label.tmpText != null)
            {
                label.tmpText.text = content;
                label.tmpText.gameObject.SetActive(true);
                return;
            }

            if (label.text != null)
            {
                label.text.text = content;
                label.text.color = TableAreaUI.GetDefaultStateColor(TavernTableRuntimeState.WaitingOrder);
            }
        }

        private void EnsureFrontTableOrderStatusLabel()
        {
            if (frontTableOrderStatusLabel?.rectTransform != null)
            {
                return;
            }

            var anchor = ResolveFrontTableOrderAnchor();
            if (anchor == null)
            {
                Debug.LogWarning("[TavernSceneManager] 未找到场景节点 FrontTableOrder，无法显示「点单中」。");
                return;
            }

            frontTableOrderStatusLabel = CreateFrontTableOrderStatusLabel(
                FrontTableOrderStatusLabelName,
                anchor.transform,
                Vector3.zero,
                FrontTableOrderStatusText);
        }

        /// <summary>
        /// 从前台点单状态预制体创建跟随世界锚点的「点单中」标签（优先子节点 Text 的 TMP）。
        /// </summary>
        private GuideWorldLabel CreateFrontTableOrderStatusLabel(
            string name,
            Transform target,
            Vector3 worldOffset,
            string label)
        {
            if (canvasParent == null || target == null)
            {
                return null;
            }

            var prefab = GameplayResourceStore.LoadAsset<GameObject>(FrontTableOrderStatusPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[TavernSceneManager] 未找到预制体 {FrontTableOrderStatusPrefabPath}，回退 GuideWorldLabel。");
                return CreateGuideWorldLabel(name, target, worldOffset, label);
            }

            var instance = Instantiate(prefab, canvasParent, false);
            if (instance == null)
            {
                return null;
            }

            instance.name = name;

            var rectTransform = instance.GetComponent<RectTransform>();
            // 预制体结构：FrontTableOrderStatus / Label / Text（TMP）
            var tmpText = instance.transform.Find("Label/Text")?.GetComponent<TMP_Text>()
                          ?? instance.transform.Find("Text")?.GetComponent<TMP_Text>()
                          ?? instance.GetComponentInChildren<TMP_Text>(true);
            var uiText = tmpText == null ? instance.GetComponentInChildren<Text>(true) : null;
            if (rectTransform == null || (tmpText == null && uiText == null))
            {
                Debug.LogWarning("[TavernSceneManager] FrontTableOrderStatus 预制体缺少 Text/TMP 文本节点。");
                Destroy(instance);
                return null;
            }

            var guideLabel = new GuideWorldLabel
            {
                rectTransform = rectTransform,
                tmpText = tmpText,
                text = uiText,
                target = target,
                worldOffset = worldOffset
            };

            ApplyFrontTableOrderStatusText(guideLabel, label);
            guideWorldLabels.Add(guideLabel);
            return guideLabel;
        }

        private GameObject ResolveFrontTableOrderAnchor()
        {
            if (frontTableOrderAnchor == null)
            {
                frontTableOrderAnchor = FindGuideSceneObject(FrontTableOrderAnchorName)
                                       ?? FindGuideTargetObject(FrontTableOrderAnchorName)
                                       ?? FindSceneGameObjectByName(FrontTableOrderAnchorName);
            }

            return frontTableOrderAnchor;
        }

        private float GetShopkeeperOrderProgress01()
        {
            if (frontCounterOrderProgressStarts.Count <= 0)
            {
                return 1f;
            }

            // 多桌并行时取最小进度，保证任一桌未完成时条仍在走。
            var minProgress = 1f;
            foreach (var pair in frontCounterOrderProgressStarts)
            {
                if (!frontCounterOrderProgressDurations.TryGetValue(pair.Key, out var duration) || duration <= 0f)
                {
                    continue;
                }

                minProgress = Mathf.Min(minProgress, Mathf.Clamp01((Time.time - pair.Value) / duration));
            }

            return minProgress;
        }

        /// <summary>
        /// 前台完成点单：建厨工单、通知后厨、客人离队入座等菜。
        /// 贵客菜单下的普通客：照常点单入座，入座齐人后再抱怨离店。
        /// </summary>
        private void CompleteFrontCounterOrder(int tableId)
        {
            if (tableId <= 0 || !AllTables.TryGetValue(tableId, out var table) || table == null)
            {
                return;
            }

            if (!IsTableInState(tableId, TavernTableRuntimeState.WaitingOrder))
            {
                return;
            }

            var rejectAfterSeat = TryMarkVipMenuRejectAfterSeat(tableId);
            CancelFrontCounterOrderRoutine(tableId);
            CompleteTableOrderByWaiter(tableId, table);
            if (rejectAfterSeat)
            {
                // 入座后会离店：不通知后厨，避免白做菜。
                RemoveCookOrderTicket(tableId);
            }
            else
            {
                NotifyChefCookOrderTicket(tableId);
            }
        }

        /// <summary>
        /// 贵客菜单 + 全员普通客且尚未入座：标记本桌入座后再抱怨离店。
        /// 已入座普通客 / 贵客桌不标记（切换菜单后不受影响）。
        /// </summary>
        private bool TryMarkVipMenuRejectAfterSeat(int tableId)
        {
            pendingVipMenuRejectAfterSeatTableIds.Remove(tableId);
            var dataManager = DataManager.Instance;
            if (dataManager == null
                || dataManager.IsVisitingOtherTavern
                || !dataManager.IsTavernMenuEntryUnlocked()
                || !dataManager.IsVipMenuSelected())
            {
                return false;
            }

            if (!frontCounterOrderBindings.TryGetValue(tableId, out var customers) || customers == null)
            {
                if (!TryGetTableCustomerGroup(tableId, out customers) || customers == null)
                {
                    return false;
                }
            }

            customers.RemoveAll(item => item == null);
            if (customers.Count <= 0)
            {
                return false;
            }

            for (var index = 0; index < customers.Count; index++)
            {
                var customer = customers[index];
                if (customer.IsVip || customer.IsSeated)
                {
                    return false;
                }
            }

            pendingVipMenuRejectAfterSeatTableIds.Add(tableId);
            return true;
        }

        /// <summary>
        /// 本轮前台点单标记的普通客桌：全员入座后弹「菜品太贵了」再离店。
        /// </summary>
        private bool TryRejectVipMenuRegularGuestsAfterSeated(int tableId)
        {
            if (tableId <= 0 || !pendingVipMenuRejectAfterSeatTableIds.Remove(tableId))
            {
                return false;
            }

            if (!TryGetTableCustomerGroup(tableId, out var customers) || customers == null)
            {
                return false;
            }

            customers.RemoveAll(item => item == null);
            if (customers.Count <= 0)
            {
                return false;
            }

            for (var index = 0; index < customers.Count; index++)
            {
                var customer = customers[index];
                if (customer == null || customer.IsVip || !customer.IsSeated || customer.IsLeavingTavern)
                {
                    pendingVipMenuRejectAfterSeatTableIds.Add(tableId);
                    return false;
                }
            }

            StartCoroutine(RejectVipMenuAfterSeatedRoutine(tableId, customers));
            return true;
        }

        private IEnumerator RejectVipMenuAfterSeatedRoutine(
            int tableId,
            List<TavernCustomerRuntimeController> customers)
        {
            const string tooExpensiveLine = "菜品太贵了";
            const float tipSeconds = 2f;
            for (var index = 0; index < customers.Count; index++)
            {
                var customer = customers[index];
                if (customer == null || customer.IsLeavingTavern)
                {
                    continue;
                }

                HudOverlayService.ShowCustomerReviewTip(
                    customer.transform,
                    tooExpensiveLine,
                    durationSeconds: tipSeconds);
            }

            yield return new WaitForSeconds(tipSeconds);

            RemoveCookOrderTicket(tableId);
            TriggerTableWalkout(tableId, CustomerWalkoutReason.None);
        }

        /// <summary>
        /// 前台点单完成后：离队并走向已预留桌位入座。
        /// </summary>
        private void SeatFrontCounterOrderCustomers(int tableId)
        {
            if (tableId <= 0 || !AllTables.TryGetValue(tableId, out var table) || table == null)
            {
                frontCounterOrderBindings.Remove(tableId);
                return;
            }

            if (!frontCounterOrderBindings.TryGetValue(tableId, out var customers) || customers == null)
            {
                if (!TryGetTableCustomerGroup(tableId, out customers) || customers == null)
                {
                    return;
                }
            }

            frontCounterOrderBindings.Remove(tableId);
            customers.RemoveAll(item => item == null);
            if (customers.Count == 0)
            {
                return;
            }

            // 先解析全部接近点；失败用桌目标点兜底，禁止出队后静默 continue 导致「点了单不上桌」。
            var seatTargets = new Vector3[customers.Count];
            for (var seatIndex = 0; seatIndex < customers.Count; seatIndex++)
            {
                seatTargets[seatIndex] = ResolveSeatApproachOrFallback(table, seatIndex);
            }

            for (var index = 0; index < customers.Count; index++)
            {
                queuedCustomers.Remove(customers[index]);
            }

            for (var seatIndex = 0; seatIndex < customers.Count; seatIndex++)
            {
                customers[seatIndex].AssignToTable(tableId, seatTargets[seatIndex], seatIndex);
            }

            customerFlowService.RegisterTableGroup(
                tableCustomers,
                tableCustomerGroups,
                tableId,
                customers);
            UpdateQueuePositions();
            NotifyFrontCounterOrderSlotFreed();
        }

        /// <summary>
        /// 解析座位接近点；NavMesh 采样失败时回退到桌客目标点/桌位坐标，保证必有入座目标。
        /// </summary>
        private Vector3 ResolveSeatApproachOrFallback(TableArea table, int seatIndex)
        {
            var approach = ResolveSeatApproach(table, seatIndex);
            if (approach.success)
            {
                return approach.position;
            }

            if (table != null)
            {
                if (TryGetNavMeshPosition(table.GetCustomerTargetPosition(), out var tableTarget))
                {
                    return tableTarget;
                }

                if (table.TryGetSeatPoseByIndex(seatIndex, out var seatPosition, out _)
                    && TryGetNavMeshPosition(seatPosition, out var seatNav))
                {
                    return seatNav;
                }

                return table.GetCustomerTargetPosition();
            }

            return Vector3.zero;
        }

        /// <summary>
        /// 已废弃：前台点单改为进店定名额 + 到点事件。
        /// </summary>
        private void TickQueuedCustomerAssignment(float deltaTime)
        {
        }

        /// <summary>
        /// 根据当前桌位容量，决定这次刷新的顾客组人数。
        /// </summary>
        /// <returns>1~4 之间的顾客人数。</returns>
        private int ResolveSpawnGroupSize()
        {
            var idlePreferredGroupSize = GetPreferredIdleSpawnGroupSize();
            var size = idlePreferredGroupSize > 0
                ? idlePreferredGroupSize
                : GetPreferredQueuedSpawnGroupSize();
            if (spawnGroupSizeCap > 0 && size > 0)
            {
                size = Mathf.Min(size, spawnGroupSizeCap);
            }

            return size;
        }

        /// <summary>
        /// 根据当前空桌情况，选择本轮应直接入店的一组顾客人数。
        /// 开业起优先 2 人组，中后期再放开 4 人组。
        /// </summary>
        private int GetPreferredIdleSpawnGroupSize()
        {
            var maxGroup = ResolveCustomerFlowMaxGroupSize();
            if (maxGroup >= 4 && HasIdleTableWithSeatCapacity(4))
            {
                return 4;
            }

            if (maxGroup >= 2 && HasIdleTableWithSeatCapacity(2))
            {
                return 2;
            }

            if (maxGroup >= 1 && (HasIdleTableWithSeatCapacity(1) || HasIdleTableWithSeatCapacity(2) || HasIdleTableWithSeatCapacity(4)))
            {
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// 店满后排队刷新：默认每次只来 1 人，中后期才允许 2 人组，避免队列瞬间堆高。
        /// </summary>
        private int GetPreferredQueuedSpawnGroupSize()
        {
            var maxGroup = ResolveCustomerFlowMaxGroupSize();
            if (maxGroup >= 2 && HasUnlockedTableWithSeatCapacity(2))
            {
                return Mathf.Min(2, maxGroup);
            }

            if (HasUnlockedTableWithSeatCapacity(2) || HasUnlockedTableWithSeatCapacity(4) || HasUnlockedTableWithSeatCapacity(1))
            {
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// 按本次营业已过时间，限制单次刷客最大组规模：2 → 4。
        /// 开业即可两人一桌，避免一人占满整桌导致首波第三人排队。
        /// </summary>
        private int ResolveCustomerFlowMaxGroupSize()
        {
            var ramp = TbConfigRuntime.GetCustomerFlowRampSeconds(180f);
            var elapsed = Mathf.Max(0f, businessOpenElapsedSeconds);
            if (elapsed < ramp * 0.75f)
            {
                return 2;
            }

            return 4;
        }

        /// <summary>
        /// 判断当前是否存在可直接接待指定人数的空桌。
        /// </summary>
        /// <param name="groupSize">目标人数。</param>
        /// <returns>存在可用桌位时返回 true，否则返回 false。</returns>
        private bool HasIdleTableWithSeatCapacity(int groupSize)
        {
            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance.GetTableData(tablePair.Key);
                if (tableData == null
                    || !tableData.isUnlocked
                    || (TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.Idle
                    || pendingUpgradeTableIds.Contains(tablePair.Key)
                    || IsTableBlockedForNewSeating(tablePair.Key))
                {
                    continue;
                }

                if (tablePair.Value != null && tablePair.Value.GetSeatCapacity() >= groupSize)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 判断当前是否存在已经解锁、且容量满足指定人数的桌位。
        /// 用于店内坐满时，仍然按对应桌型刷新一组排队顾客。
        /// </summary>
        /// <param name="groupSize">目标人数。</param>
        /// <returns>存在匹配桌位时返回 true。</returns>
        private bool HasUnlockedTableWithSeatCapacity(int groupSize)
        {
            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance.GetTableData(tablePair.Key);
                if (tableData == null || !tableData.isUnlocked || tablePair.Value == null)
                {
                    continue;
                }

                if (tablePair.Value.GetSeatCapacity() >= groupSize)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 尝试把一组顾客直接分配到同一张空桌。
        /// </summary>
        /// <param name="groupSize">顾客人数。</param>
        /// <param name="spawnPosition">入口出生点。</param>
        /// <returns>成功创建并分配整组顾客时返回 true，否则返回 false。</returns>
        private bool TryAssignFreeTableGroup(int groupSize, Vector3 spawnPosition)
        {
            if (groupSize <= 1 || activeCustomers.Count + groupSize > GetDynamicMaxActiveCustomers())
            {
                return false;
            }

            var spawnedCustomers = customerSpawnService.SpawnCustomerGroup(
                groupSize,
                spawnPosition,
                GetGroupSpawnPosition,
                this,
                GetSpawnCustomerTemplates(),
                customerEntryPoint,
                GetExitPosition,
                PrepareSpawnedCustomer,
                customerFlowService,
                activeCustomers);
            if (spawnedCustomers == null)
            {
                return false;
            }

            AssignWaitHudGroupMembers(spawnedCustomers);

            if (customerPlacementService.TryAssignSpawnedGroupToFreeTable(
                    AllTables,
                    pendingUpgradeTableIds,
                    spawnedCustomers,
                    groupSize,
                    ResolveSeatApproach,
                    IsTableBlockedForNewSeating,
                    tableStateService,
                    customerFlowService,
                    tableCustomers,
                    tableCustomerGroups))
            {
                return true;
            }

            customerSpawnService.RollbackSpawnedCustomers(spawnedCustomers, activeCustomers);
            return false;
        }

        /// <summary>
        /// 当没有空桌可立即入座时，按目标桌位容量整组生成排队顾客，避免后续只拆出单人入座。
        /// </summary>
        /// <param name="groupSize">整组人数。</param>
        /// <param name="spawnPosition">入口出生点。</param>
        /// <returns>成功生成排队组时返回 true。</returns>
        private bool TryEnqueueCustomerGroup(int groupSize, Vector3 spawnPosition)
        {
            if (groupSize <= 1
                || queuedCustomers.Count + groupSize > GetEffectiveMaxQueueSize()
                || activeCustomers.Count + groupSize > GetDynamicMaxActiveCustomers())
            {
                return false;
            }

            var spawnedCustomers = customerSpawnService.SpawnCustomerGroup(
                groupSize,
                spawnPosition,
                GetGroupSpawnPosition,
                this,
                GetSpawnCustomerTemplates(),
                customerEntryPoint,
                GetExitPosition,
                PrepareSpawnedCustomer,
                customerFlowService,
                activeCustomers);
            if (spawnedCustomers == null)
            {
                return false;
            }

            for (var memberIndex = 0; memberIndex < spawnedCustomers.Count; memberIndex++)
            {
                var spawned = spawnedCustomers[memberIndex];
                customerFlowService.EnqueueCustomer(queuedCustomers, spawned);
                if (spawned != null)
                {
                    waitSatisfactionTracker.OnCustomerQueued(spawned.GetInstanceID());
                }
            }

            AssignWaitHudGroupMembers(spawnedCustomers);
            for (var memberIndex = 0; memberIndex < spawnedCustomers.Count; memberIndex++)
            {
                TryDesignateFrontCounterOrderOnEnter(spawnedCustomers[memberIndex]);
            }

            UpdateQueuePositions();
            return true;
        }

        /// <summary>
        /// 贵客必须先走到排队位站定，再参与空桌分配。
        /// </summary>
        private bool CanAssignLeadingQueuedCustomer()
        {
            if (queuedCustomers.Count <= 0)
            {
                return false;
            }

            var lead = queuedCustomers[0];
            return lead == null
                   || !lead.IsVip
                   || (lead.IsQueueSlotReady && !lead.IsAwaitingVipFloorChoice);
        }

        /// <summary>
        /// 从排队队列中按优先组规模入座（4 → 2 → 1），桌容足够即可，队首优先。
        /// </summary>
        /// <returns>成功分配任意一桌时返回 true。</returns>
        private bool TryAssignQueuedCustomerGroup()
        {
            return customerPlacementService.TryAssignQueuedGroupToFreeTable(
                queuedCustomers,
                new[] { 4, 2, 1 },
                AllTables,
                pendingUpgradeTableIds,
                ResolveSeatApproach,
                IsTableBlockedForNewSeating,
                tableStateService,
                customerFlowService,
                tableCustomers,
                tableCustomerGroups);
        }

        /// <summary>
        /// 把桌位接近点查询包装成可传给分配服务的委托。
        /// </summary>
        private (bool success, Vector3 position) ResolveSeatApproach(TableArea table, int seatIndex)
        {
            return TryGetTableSeatApproachPosition(table, seatIndex, out var tableTargetPosition)
                ? (true, tableTargetPosition)
                : (false, Vector3.zero);
        }

        /// <summary>
        /// 为指定座位计算一个“先走到附近、再坐下”的接近点，减少多人同时去同一张桌子时的相互挤压。
        /// </summary>
        /// <param name="table">目标桌位。</param>
        /// <param name="seatIndex">座位索引。</param>
        /// <param name="approachPosition">输出的接近点。</param>
        /// <returns>找到可寻路接近点时返回 true。</returns>
        private bool TryGetTableSeatApproachPosition(TableArea table, int seatIndex, out Vector3 approachPosition)
        {
            approachPosition = Vector3.zero;
            if (table == null)
            {
                return false;
            }

            if (!table.TryGetSeatPoseByIndex(seatIndex, out var seatPosition, out var lookAtPosition))
            {
                return TryGetNavMeshPosition(table.GetCustomerTargetPosition(), out approachPosition);
            }

            var awayFromTable = seatPosition - lookAtPosition;
            awayFromTable.y = 0f;
            if (awayFromTable.sqrMagnitude <= 0.0001f)
            {
                awayFromTable = seatPosition - table.transform.position;
                awayFromTable.y = 0f;
            }

            if (awayFromTable.sqrMagnitude <= 0.0001f)
            {
                awayFromTable = table.transform.right;
            }

            awayFromTable.Normalize();
            var side = Vector3.Cross(Vector3.up, awayFromTable).normalized;
            var sideOffset = ((seatIndex % 2 == 0) ? -1f : 1f) * 0.08f;
            var candidate = seatPosition + awayFromTable * 0.28f + side * sideOffset;
            return TryGetNavMeshPosition(candidate, out approachPosition)
                   || TryGetNavMeshPosition(seatPosition, out approachPosition);
        }

        /// <summary>
        /// 普通顾客固定只用 CustomerM2（与星级无关）。
        /// </summary>
        private List<GameObject> GetSpawnCustomerTemplates()
        {
            if (customerTemplates.Count == 0)
            {
                return customerTemplates;
            }

            levelFilteredCustomerTemplates.Clear();
            for (var index = 0; index < customerTemplates.Count; index++)
            {
                var template = customerTemplates[index];
                if (IsCustomerModelM2(template))
                {
                    levelFilteredCustomerTemplates.Add(template);
                }
            }

            // 未配置 M2 时回退全池，避免完全刷不出客。
            return levelFilteredCustomerTemplates.Count > 0
                ? levelFilteredCustomerTemplates
                : customerTemplates;
        }

        /// <summary>
        /// 判断模板是否为顾客 M2（兼容 CustomerM2 / _M2 / 以 M2 结尾的命名）。
        /// </summary>
        private static bool IsCustomerModelM2(GameObject template)
        {
            return IsCustomerModelByToken(template, "M2");
        }

        /// <summary>
        /// 判断模板是否为顾客 M5（贵客专用）。
        /// </summary>
        private static bool IsCustomerModelM5(GameObject template)
        {
            return IsCustomerModelByToken(template, "M5");
        }

        /// <summary>
        /// 判断模板是否为顾客 M6（稀客专用）。
        /// </summary>
        private static bool IsCustomerModelM6(GameObject template)
        {
            return IsCustomerModelByToken(template, "M6");
        }

        /// <summary>
        /// 按 CustomerM* / _M* / 以 M* 结尾匹配顾客模型名。
        /// </summary>
        private static bool IsCustomerModelByToken(GameObject template, string modelToken)
        {
            if (template == null || string.IsNullOrEmpty(template.name) || string.IsNullOrEmpty(modelToken))
            {
                return false;
            }

            var name = template.name;
            return name.IndexOf("Customer" + modelToken, System.StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("_" + modelToken, System.StringComparison.OrdinalIgnoreCase) >= 0
                   || name.EndsWith(modelToken, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 生成单个顾客运行时对象。
        /// </summary>
        /// <param name="spawnPosition">出生坐标。</param>
        /// <param name="asVip">是否贵客（CustomerM5）。</param>
        /// <param name="asRare">是否稀客（CustomerM6）。</param>
        /// <returns>成功创建时返回控制器，否则返回 null。</returns>
        private TavernCustomerRuntimeController SpawnCustomerRuntime(
            Vector3 spawnPosition,
            bool asVip = false,
            bool asRare = false)
        {
            List<GameObject> templates;
            if (asVip)
            {
                templates = vipCustomerTemplates;
            }
            else if (asRare)
            {
                templates = rareCustomerTemplates;
            }
            else
            {
                templates = GetSpawnCustomerTemplates();
            }

            var runtimeController = customerSpawnService.SpawnSingleCustomer(
                this,
                templates,
                customerEntryPoint,
                spawnPosition,
                GetExitPosition,
                PrepareSpawnedCustomer,
                customerFlowService,
                activeCustomers);
            if (asVip && runtimeController != null)
            {
                runtimeController.MarkAsVip();
            }
            else if (asRare && runtimeController != null)
            {
                runtimeController.MarkAsRare();
            }

            if (runtimeController != null)
            {
                AssignWaitHudGroupMember(runtimeController);
            }

            return runtimeController;
        }

        /// <summary>
        /// 根据当前已解锁桌位总座位数和排队上限，动态计算可存在的顾客总数。
        /// 避免固定上限 8 把后续整组顾客提前挡掉。
        /// </summary>
        /// <returns>当前允许的顾客总数上限。</returns>
        private int GetDynamicMaxActiveCustomers()
        {
            var seatCapacity = 0;
            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance.GetTableData(tablePair.Key);
                if (tableData == null || !tableData.isUnlocked || tablePair.Value == null)
                {
                    continue;
                }

                seatCapacity += Mathf.Max(0, tablePair.Value.GetSeatCapacity());
            }

            return Mathf.Max(maxActiveCustomers, seatCapacity + GetEffectiveMaxQueueSize());
        }

        /// <summary>
        /// 统计当前所有已解锁桌位的总座位数。
        /// </summary>
        /// <returns>已解锁总座位数。</returns>
        private int GetUnlockedSeatCapacity()
        {
            var seatCapacity = 0;
            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance.GetTableData(tablePair.Key);
                if (tableData == null || !tableData.isUnlocked || tablePair.Value == null)
                {
                    continue;
                }

                seatCapacity += Mathf.Max(0, tablePair.Value.GetSeatCapacity());
            }

            return seatCapacity;
        }

        /// <summary>
        /// 按整组人数把顾客出生点沿门口横向打散，避免多人刷在一起。
        /// </summary>
        /// <param name="baseSpawnPosition">组的基础出生点。</param>
        /// <param name="memberIndex">当前成员索引。</param>
        /// <param name="groupSize">整组人数。</param>
        /// <returns>当前成员的出生坐标。</returns>
        private Vector3 GetGroupSpawnPosition(Vector3 baseSpawnPosition, int memberIndex, int groupSize)
        {
            if (customerEntryPoint == null)
            {
                return baseSpawnPosition;
            }

            var right = customerEntryPoint.right.sqrMagnitude > 0.1f ? customerEntryPoint.right.normalized : Vector3.right;
            var centeredOffset = (memberIndex - (groupSize - 1) * 0.5f) * GroupSpawnSpacing;
            var candidate = baseSpawnPosition + right * centeredOffset;
            return TryGetNavMeshPosition(candidate, out var navMeshPosition) ? navMeshPosition : candidate;
        }

        /// <summary>
        /// 随机获取桌面菜品预制体。
        /// </summary>
        /// <returns>返回匹配到的对象引用。</returns>
        private GameObject GetRandomDishPrefab()
        {
            if (dishPrefabs.Count == 0)
            {
                return null;
            }

            return dishPrefabs[Random.Range(0, dishPrefabs.Count)];
        }

        /// <summary>
        /// 厨师完成做菜后，把餐盘与菜品摆到 FoodTable 上，供小二后续取餐。
        /// </summary>
        /// <param name="count">新增菜品数量。</param>
        private void AddPreparedDishesToFoodTable(int count)
        {
            if (count <= 0)
            {
                return;
            }

            for (var index = 0; index < count; index++)
            {
                var dishPrefab = GetRandomDishPrefab();
                if (dishPrefab == null)
                {
                    continue;
                }

                var slotIndex = AllocateFoodTableSlot(out var stackLayer);
                stagedDishEntries.Add(new StagedDishEntry
                {
                    rootObject = CreatePreparedDishInstance(dishPrefab),
                    dishPrefab = dishPrefab,
                    slotIndex = slotIndex,
                    stackLayer = stackLayer
                });
            }

            RefreshPreparedDishLayout();
            RefreshFoodTableServeBubble();
        }

        /// <summary>
        /// 从 FoodTable 取走一份已完成的菜品，并返回对应桌面菜品预制体。
        /// </summary>
        /// <returns>成功取到时返回对应桌面菜品预制体，否则返回 null。</returns>
        private GameObject TakePreparedDishPrefab()
        {
            stagedDishEntries.RemoveAll(entry => entry == null);
            if (stagedDishEntries.Count == 0)
            {
                RefreshFoodTableServeBubble();
                return null;
            }

            var entry = stagedDishEntries[0];
            stagedDishEntries.RemoveAt(0);
            if (entry.rootObject != null)
            {
                Destroy(entry.rootObject);
            }

            RefreshPreparedDishLayout();
            RefreshFoodTableServeBubble();
            return entry.dishPrefab;
        }

        /// <summary>
        /// 将尚未真正上桌的菜品退回 FoodTable 队列。
        /// </summary>
        /// <param name="dishPrefab">菜品预制体。</param>
        private void ReturnPreparedDishPrefab(GameObject dishPrefab)
        {
            if (dishPrefab == null)
            {
                return;
            }

            var slotIndex = AllocateFoodTableSlot(out var stackLayer);
            stagedDishEntries.Insert(0, new StagedDishEntry
            {
                rootObject = CreatePreparedDishInstance(dishPrefab),
                dishPrefab = dishPrefab,
                slotIndex = slotIndex,
                stackLayer = stackLayer
            });

            RefreshPreparedDishLayout();
            RefreshFoodTableServeBubble();
        }

        /// <summary>
        /// 获取当前成品菜队列数量（含无 FoodTable 时仅保留 dishPrefab 的逻辑项）。
        /// </summary>
        private int GetPreparedDishQueueCount()
        {
            stagedDishEntries.RemoveAll(entry => entry == null || entry.dishPrefab == null);
            return stagedDishEntries.Count;
        }

        /// <summary>
        /// 清空 FoodTable 上当前所有待取菜品。
        /// </summary>
        private void ClearPreparedDishQueue()
        {
            for (var index = 0; index < stagedDishEntries.Count; index++)
            {
                var entry = stagedDishEntries[index];
                if (entry?.rootObject != null)
                {
                    Destroy(entry.rootObject);
                }
            }

            stagedDishEntries.Clear();
            ClearFoodTableServeBubble();
        }

        /// <summary>
        /// 营业结束时清空出餐台备菜队列、小二手上挂盘及列表外残留 PreparedPlate。
        /// </summary>
        private void ClearPreparedDishesForBusinessEnd()
        {
            ClearAllWaiterCarryPlatesForBusinessEnd();
            ClearPreparedDishQueue();
            DestroyOrphanPreparedPlatesOnFoodTable();
            reservedServeDishCount = 0;
        }

        private void ClearAllWaiterCarryPlatesForBusinessEnd()
        {
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            for (var index = 0; index < waiters.Length; index++)
            {
                ClearWaiterCarryPlate(waiters[index]);
            }
        }

        private void DestroyOrphanPreparedPlatesOnFoodTable()
        {
            if (foodTableObject == null)
            {
                return;
            }

            var foodTableTransform = foodTableObject.transform;
            for (var index = foodTableTransform.childCount - 1; index >= 0; index--)
            {
                var child = foodTableTransform.GetChild(index);
                if (child != null && child.name.StartsWith("PreparedPlate_", System.StringComparison.Ordinal))
                {
                    Destroy(child.gameObject);
                }
            }
        }

        /// <summary>
        /// 菜好后在出餐台菜品上方显示上菜气泡（不挂在顾客桌上）。
        /// </summary>
        private void RefreshFoodTableServeBubble()
        {
            stagedDishEntries.RemoveAll(entry => entry == null || entry.dishPrefab == null);
            // 全体小二都会上菜后走自动派工，不再挂出餐台点击气泡。
            if (!RequiresPlayerClickForServe()
                || stagedDishEntries.Count <= 0
                || !TryGetNextServeableTableId(out _))
            {
                ClearFoodTableServeBubble();
                return;
            }

            if (!TryGetFoodTableServeBubbleAnchor(out var followTarget, out var bubbleWorldOffset))
            {
                ClearFoodTableServeBubble();
                return;
            }

            if (foodTableServeBubble != null)
            {
                var itemView = foodTableServeBubble.GetComponent<WorldFollowOrderButtonView>();
                if (itemView != null)
                {
                    itemView.BindTarget(followTarget, bubbleWorldOffset);
                    itemView.RefreshServeIcon(ResolveFoodTableServeBubbleIcon());
                    return;
                }

                ClearFoodTableServeBubble();
            }

            foodTableServeBubble = HudOverlayService.ShowFoodTableServeBubble(
                followTarget,
                ResolveFoodTableServeBubbleIcon(),
                OnFoodTableServeBubbleClicked,
                bubbleWorldOffset);
        }

        private void ClearFoodTableServeBubble()
        {
            if (foodTableServeBubble == null)
            {
                return;
            }

            HudOverlayService.ReleaseWorldHudItem(foodTableServeBubble);
            foodTableServeBubble = null;
        }

        /// <summary>
        /// 上菜气泡锚点：取出餐台所有待取菜位置的中心。
        /// </summary>
        private bool TryGetFoodTableServeBubbleAnchor(out Transform anchor, out Vector3 worldOffset)
        {
            anchor = null;
            worldOffset = Vector3.zero;
            if (foodTableObject == null || !foodTableObject.activeInHierarchy || stagedDishEntries.Count <= 0)
            {
                return false;
            }

            anchor = foodTableObject.transform;
            var sumLocal = Vector3.zero;
            var maxLocalY = float.MinValue;
            var validCount = 0;
            for (var index = 0; index < stagedDishEntries.Count; index++)
            {
                var entry = stagedDishEntries[index];
                if (entry?.rootObject == null)
                {
                    continue;
                }

                var dishLocal = anchor.InverseTransformPoint(entry.rootObject.transform.position);
                sumLocal += dishLocal;
                if (dishLocal.y > maxLocalY)
                {
                    maxLocalY = dishLocal.y;
                }

                validCount++;
            }

            if (validCount <= 0)
            {
                return false;
            }

            var centerLocal = sumLocal / validCount;
            var bubbleLocal = new Vector3(
                centerLocal.x,
                maxLocalY + FoodTableServeBubbleLocalYOffset,
                centerLocal.z);
            worldOffset = anchor.TransformPoint(bubbleLocal) - anchor.position;
            return true;
        }

        private bool TryGetNextServeableTableId(out int tableId)
        {
            return TryResolveLongestWaitingServeTableId(out tableId);
        }

        /// <summary>
        /// 在可上菜桌位中选取等待最久的一桌（与自动派单同一套 waitTracker 计时）。
        /// </summary>
        private bool TryResolveLongestWaitingServeTableId(out int tableId)
        {
            tableId = 0;
            var bestWait = -1f;
            foreach (var tablePair in AllTables)
            {
                if (!IsServeDispatchEligible(tablePair.Key))
                {
                    continue;
                }

                var wait = waiterTaskWaitTracker.GetWaitDuration(tablePair.Key);
                if (tableId <= 0
                    || wait > bestWait
                    || (Mathf.Approximately(wait, bestWait) && tablePair.Key < tableId))
                {
                    bestWait = wait;
                    tableId = tablePair.Key;
                }
            }

            return tableId > 0;
        }

        private bool IsServeDispatchEligible(int tableId)
        {
            var data = DataManager.Instance != null
                ? DataManager.Instance.GetTableData(tableId)
                : null;
            if (data == null || !data.isUnlocked)
            {
                return false;
            }

            if ((TavernTableRuntimeState)data.runtimeState != TavernTableRuntimeState.WaitingServe)
            {
                return false;
            }

            if (DataManager.Instance.TavernData.availableDishes <= 0)
            {
                return false;
            }

            if (assignedServeTableIds.Contains(tableId))
            {
                return false;
            }

            return HasAvailablePreparedDishForServe(tableId);
        }

        private void OnFoodTableServeBubbleClicked()
        {
            if (!TryGetNextServeableTableId(out var tableId))
            {
                RefreshFoodTableServeBubble();
                return;
            }

            if (!TryStartWaiterServeTask(tableId, playerDirected: true))
            {
                HudOverlayService.ShowFloatingWarning("暂无空闲小二可上菜");
            }

            RefreshFoodTableServeBubble();
        }

        private Sprite ResolveFoodTableServeBubbleIcon()
        {
            if (foodTableServeBubbleIcon != null)
            {
                return foodTableServeBubbleIcon;
            }

            foodTableServeBubbleIcon = GameplayResourceStore.LoadAsset<Sprite>(
                "Assets/Res/Resources/Textures/UI/Icons 1/红烧肉.png");
            return foodTableServeBubbleIcon;
        }

        /// <summary>
        /// 创建一份摆在 FoodTable 上的餐盘与菜品组合。
        /// </summary>
        /// <param name="dishPrefab">菜品预制体。</param>
        /// <returns>组合根对象；创建失败时返回 null。</returns>
        private GameObject CreatePreparedDishInstance(GameObject dishPrefab)
        {
            if (foodTableObject == null || !foodTableObject.activeInHierarchy || platePrefab == null || dishPrefab == null)
            {
                return null;
            }

            var plateInstance = Instantiate(platePrefab, foodTableObject.transform, false);
            plateInstance.name = $"PreparedPlate_{stagedDishEntries.Count + 1}";
            plateInstance.transform.localRotation = Quaternion.Euler(0f, FoodTablePlateLocalYawDegrees, 0f);
            plateInstance.transform.localScale = Vector3.one;

            var dishInstance = Instantiate(dishPrefab, plateInstance.transform, false);
            dishInstance.name = dishPrefab.name;
            dishInstance.transform.localPosition = Vector3.up * DishOnPlateYOffset;
            dishInstance.transform.localRotation = Quaternion.identity;
            dishInstance.transform.localScale = Vector3.one;
            return plateInstance;
        }

        /// <summary>
        /// 按 6 个固定槽位（2×3）排布 FoodTable 上的待取菜品；槽位满后同槽叠放。
        /// </summary>
        private void RefreshPreparedDishLayout()
        {
            stagedDishEntries.RemoveAll(entry => entry == null || entry.dishPrefab == null);
            if (foodTableObject == null)
            {
                return;
            }

            // 高度仍贴台面；平面位置用固定本地坐标，不再按 bounds 推算。
            if (!TryGetFoodTableTopBounds(
                    out _,
                    out _,
                    out var topLocalY,
                    out _,
                    out _))
            {
                topLocalY = 0f;
            }

            var slotBuckets = new List<StagedDishEntry>[FoodTablePlateSlotCount];
            for (var slotIndex = 0; slotIndex < FoodTablePlateSlotCount; slotIndex++)
            {
                slotBuckets[slotIndex] = new List<StagedDishEntry>();
            }

            for (var index = 0; index < stagedDishEntries.Count; index++)
            {
                var entry = stagedDishEntries[index];
                if (entry.rootObject == null)
                {
                    continue;
                }

                if (entry.slotIndex < 0 || entry.slotIndex >= FoodTablePlateSlotCount)
                {
                    entry.slotIndex = index % FoodTablePlateSlotCount;
                }

                slotBuckets[entry.slotIndex].Add(entry);
            }

            var plateRotation = Quaternion.Euler(0f, FoodTablePlateLocalYawDegrees, 0f);
            for (var slotIndex = 0; slotIndex < FoodTablePlateSlotCount; slotIndex++)
            {
                var bucket = slotBuckets[slotIndex];
                for (var layer = 0; layer < bucket.Count; layer++)
                {
                    var entry = bucket[layer];
                    entry.stackLayer = layer;
                    entry.rootObject.transform.localPosition = GetFoodTableSlotLocalPosition(
                        slotIndex,
                        layer,
                        topLocalY);
                    entry.rootObject.transform.localRotation = plateRotation;
                    entry.rootObject.transform.localScale = Vector3.one;
                }
            }
        }

        /// <summary>
        /// 优先占用空槽；6 槽均满时在占用最少的槽位上叠放。
        /// </summary>
        private int AllocateFoodTableSlot(out int stackLayer)
        {
            stackLayer = 0;
            var slotCounts = new int[FoodTablePlateSlotCount];
            for (var index = 0; index < stagedDishEntries.Count; index++)
            {
                var entry = stagedDishEntries[index];
                if (entry == null || entry.dishPrefab == null)
                {
                    continue;
                }

                if (entry.slotIndex < 0 || entry.slotIndex >= FoodTablePlateSlotCount)
                {
                    continue;
                }

                slotCounts[entry.slotIndex]++;
            }

            for (var slotIndex = 0; slotIndex < FoodTablePlateSlotCount; slotIndex++)
            {
                if (slotCounts[slotIndex] == 0)
                {
                    return slotIndex;
                }
            }

            var bestSlot = 0;
            var minCount = slotCounts[0];
            for (var slotIndex = 1; slotIndex < FoodTablePlateSlotCount; slotIndex++)
            {
                if (slotCounts[slotIndex] >= minCount)
                {
                    continue;
                }

                minCount = slotCounts[slotIndex];
                bestSlot = slotIndex;
            }

            stackLayer = slotCounts[bestSlot];
            return bestSlot;
        }

        /// <summary>
        /// 2 行 × 3 列固定本地坐标：(row,col) 中 col 走 X、row 走 Z。
        /// (0,0)=(-0.14,-0.4)，(0,1)=(0.16,-0.4)，(1,0)=(-0.14,0)。
        /// </summary>
        private static Vector3 GetFoodTableSlotLocalPosition(
            int slotIndex,
            int stackLayer,
            float topLocalY)
        {
            var column = slotIndex % FoodTablePlateColumnCount;
            var row = slotIndex / FoodTablePlateColumnCount;
            var localX = FoodTablePlateStartLocalX + column * FoodTablePlateSpacingX;
            var localZ = FoodTablePlateStartLocalZ + row * FoodTablePlateSpacingZ;
            var localY = topLocalY
                         + FoodTablePlateSurfaceYOffset
                         + stackLayer * FoodTablePlateOverlapStackYOffset;
            return new Vector3(localX, localY, localZ);
        }

        /// <summary>
        /// 计算 FoodTable 顶面的本地坐标与可摆放范围，用于把餐盘压到桌面上。
        /// </summary>
        private bool TryGetFoodTableTopBounds(
            out float centerLocalX,
            out float centerLocalZ,
            out float topLocalY,
            out float halfWidth,
            out float halfDepth)
        {
            centerLocalX = 0f;
            centerLocalZ = 0f;
            topLocalY = 0f;
            halfWidth = 0.4f;
            halfDepth = 0.35f;
            if (foodTableObject == null)
            {
                return false;
            }

            var renderers = foodTableObject.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            Bounds bounds = default;
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || IsPreparedDishRenderer(renderer))
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
                return false;
            }

            var topWorld = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            var localTop = foodTableObject.transform.InverseTransformPoint(topWorld);
            centerLocalX = localTop.x;
            centerLocalZ = localTop.z;
            topLocalY = localTop.y;
            halfWidth = Mathf.Max(0.25f, bounds.extents.x);
            halfDepth = Mathf.Max(0.2f, bounds.extents.z);
            return true;
        }

        /// <summary>
        /// 出餐台顶面只统计桌子本体 mesh，排除已摆上的 PreparedPlate 子物体。
        /// </summary>
        private bool IsPreparedDishRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            var current = renderer.transform;
            while (current != null && current != foodTableObject.transform)
            {
                if (current.name.StartsWith("PreparedPlate_", System.StringComparison.Ordinal))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        /// <summary>
        /// 准备刚生成顾客的运行状态。
        /// </summary>
        /// <param name="customerObj">顾客对象。</param>
        /// <param name="spawnPosition">坐标。</param>
        private void PrepareSpawnedCustomer(GameObject customerObj, Vector3 spawnPosition)
        {
            EnsureCustomerMovementComponents(customerObj);

            var rootAgent = customerObj.GetComponent<NavMeshAgent>();
            foreach (var navMeshAgent in customerObj.GetComponentsInChildren<NavMeshAgent>(true))
            {
                navMeshAgent.enabled = false;
            }

            customerObj.transform.position = spawnPosition;
            customerObj.SetActive(true);

            if (rootAgent != null)
            {
                rootAgent.enabled = false;
            }
        }

        /// <summary>
        /// 保证根节点具备寻路与碰撞组件（贵客 FBX 包装体可能缺组件）。
        /// </summary>
        private static void EnsureCustomerMovementComponents(GameObject customerObj)
        {
            if (customerObj == null)
            {
                return;
            }

            if (customerObj.GetComponent<NavMeshAgent>() == null)
            {
                var agent = customerObj.AddComponent<NavMeshAgent>();
                agent.radius = 0.15f;
                agent.speed = 0.85f;
                agent.acceleration = 2f;
                agent.angularSpeed = 9000f;
                agent.height = 1f;
                agent.baseOffset = 0f;
                agent.autoBraking = true;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            }

            if (customerObj.GetComponent<CapsuleCollider>() == null)
            {
                var capsule = customerObj.AddComponent<CapsuleCollider>();
                capsule.isTrigger = true;
                capsule.radius = 0.5f;
                capsule.height = 2f;
                capsule.center = new Vector3(0f, 1f, 0f);
                capsule.direction = 1;
            }

            if (customerObj.GetComponent<AudioSource>() is { } customerAudio)
            {
                customerAudio.playOnAwake = false;
                customerAudio.loop = false;
                customerAudio.volume = 0f;
                customerAudio.enabled = false;
            }
        }

        /// <summary>
        /// 尝试获取顾客生成位置。
        /// </summary>
        /// <param name="spawnPosition">坐标。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private bool TryGetSpawnPosition(out Vector3 spawnPosition)
        {
            var spawnRoot = customerSpawnPoint != null ? customerSpawnPoint : customerEntryPoint;
            if (spawnRoot == null)
            {
                spawnPosition = Vector3.zero;
                return false;
            }

            var right = spawnRoot.right.sqrMagnitude > 0.1f ? spawnRoot.right.normalized : Vector3.right;
            var forward = spawnRoot.forward.sqrMagnitude > 0.1f ? spawnRoot.forward.normalized : Vector3.back;

            for (var attempt = 0; attempt < 4; attempt++)
            {
                var laneIndex = (activeCustomers.Count + attempt) % 3 - 1;
                var candidate = spawnRoot.position + right * (laneIndex * (spawnLaneSpacing + 0.15f)) + forward * 0.75f;
                if (TryGetNavMeshPosition(candidate, out spawnPosition))
                {
                    return true;
                }
            }

            return TryGetNavMeshPosition(spawnRoot.position, out spawnPosition);
        }

        /// <summary>
        /// 获取出口位置。
        /// </summary>
        /// <param name="fallbackPosition">坐标。</param>
        /// <returns>返回方法执行后的结果。</returns>
        private Vector3 GetExitPosition(Vector3 fallbackPosition)
        {
            return customerExitPoint != null && TryGetNavMeshPosition(customerExitPoint.position, out var exitPosition)
                ? exitPosition
                : fallbackPosition;
        }
    }
}
