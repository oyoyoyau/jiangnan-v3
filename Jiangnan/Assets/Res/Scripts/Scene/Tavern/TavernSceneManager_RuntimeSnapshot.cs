using System.Collections.Generic;
using JN.Client;
using JN.Client.Manager;
using JN.Client.Model;
using QFramework;
using UnityEngine;

namespace JN.Client.Scene
{
    /// <summary>
    /// 自家酒楼运行时快照：Capture（离店/暂停）与 Restore（回店续跑）。
    /// </summary>
    public partial class TavernSceneManager
    {
        /// <summary>用餐阶段开始时间（Time.time），用于 Capture stateElapsed。</summary>
        private readonly Dictionary<int, float> tableDiningStartedAt = new();
        /// <summary>用餐阶段总时长。</summary>
        private readonly Dictionary<int, float> tableDiningDurations = new();

        /// <summary>
        /// 离开自家营业中酒楼前写入快照。拜访会话 / 拜访模拟中直接跳过。
        /// </summary>
        public void CaptureOwnTavernRuntimeSnapshot()
        {
            // 静态拜访标记优先：DataManager 重建后实例属性也可能短暂不一致。
            if (DataManager.IsInOtherTavernVisitSession || visitSimulationActive)
            {
                return;
            }

            if (DataManager.Instance == null || DataManager.Instance.IsVisitingOtherTavern)
            {
                return;
            }

            var tavernData = DataManager.Instance.TavernData;
            if (tavernData == null || !tavernData.isOpen || isClosingBusiness)
            {
                return;
            }

            // 真正离店时应由 PrepareTavernSceneLeave 先 Halt；此处不 Halt，
            // 避免 OnApplicationPause 采快照后回前台营业循环被永久停死。
            var snapshot = BuildOwnTavernRuntimeSnapshot();
            if (snapshot == null)
            {
                return;
            }

            DataManager.Instance.SaveTavernRuntimeSnapshot(snapshot);
        }

        /// <summary>
        /// 离店 / 写快照前：中断本管理器与顾客身上全部协程，并清空句柄（保留桌态/顾客供快照采样）。
        /// </summary>
        public void HaltAllRuntimeCoroutinesForSceneLeave()
        {
            if (runtimeCoroutinesHaltedForLeave)
            {
                return;
            }

            runtimeCoroutinesHaltedForLeave = true;
            customerSpawnLoopActive = false;
            visitSimulationActive = false;

            // 一次停掉本组件上所有协程（含未单独登记的延后协程）。
            StopAllCoroutines();
            ClearTrackedCoroutineHandlesOnly();
            HaltAllCustomerRuntimeCoroutines();
        }

        /// <summary>
        /// 仅清空协程句柄与登记表，不重置小二/厨师业务占用（快照仍需这些状态）。
        /// </summary>
        private void ClearTrackedCoroutineHandlesOnly()
        {
            chefServiceRoutine = null;
            waiterServiceRoutine = null;
            waiterTaskRoutine = null;
            closeBusinessRoutine = null;
            softClosingDismissRoutine = null;
            postCloseCleanupRoutine = null;

            frontCounterOrderRoutines.Clear();
            frontCounterOrderSessions.Clear();
            autoCleanRoutines.Clear();
            chefTaskRoutines.Clear();
            chefCookSessions.Clear();
            vipOrderInteractionTimeoutRoutines.Clear();
            orderBubbleAutoHideRoutines.Clear();
            waiterTaskRoutines.Clear();
            waiterHomeReturnRoutines.Clear();
            waiterWakeRoutines.Clear();
            waiterWakeBoostSmokeRoutines.Clear();
        }

        private void HaltAllCustomerRuntimeCoroutines()
        {
            for (var index = 0; index < activeCustomers.Count; index++)
            {
                var customer = activeCustomers[index];
                if (customer == null)
                {
                    continue;
                }

                customer.HaltRuntimeCoroutinesForSceneLeave();
            }
        }

        private TavernRuntimeSnapshotSaveData BuildOwnTavernRuntimeSnapshot()
        {
            var businessTotal = Mathf.Max(1f, BusinessHours);
            var remaining = Mathf.Max(0f, businessTotal - businessOpenElapsedSeconds);
            var snapshot = new TavernRuntimeSnapshotSaveData
            {
                valid = true,
                nextCustomerSpawnRemaining = nextCustomerSpawnRemaining,
                businessOpenElapsedSeconds = businessOpenElapsedSeconds,
                businessRemainingSeconds = remaining,
                tables = new List<TavernTablePhaseSnapshot>(),
                frontOrders = new List<TavernFrontOrderSnapshot>(),
                unseatedQueue = new List<TavernUnseatedCustomerSnapshot>()
            };

            foreach (var tablePair in AllTables)
            {
                var tableId = tablePair.Key;
                var table = tablePair.Value;
                var tableData = DataManager.Instance.GetTableData(tableId);
                if (tableData == null || !tableData.isUnlocked)
                {
                    continue;
                }

                var state = (TavernTableRuntimeState)tableData.runtimeState;
                if (state == TavernTableRuntimeState.Idle || state == TavernTableRuntimeState.Locked)
                {
                    continue;
                }

                var phase = new TavernTablePhaseSnapshot
                {
                    tableId = tableId,
                    runtimeState = tableData.runtimeState
                };

                CaptureTableCustomerCounts(tableId, table, phase);
                // 用餐/待上菜/结账必须有人；无人则不写入占用态，避免恢复成「有菜无人」。
                if (IsCustomerOccupiedTableState(state) && phase.seatedCount <= 0 && phase.queuedBoundCount <= 0)
                {
                    continue;
                }

                CaptureTablePhaseTiming(tableId, state, phase);
                CaptureCookTicket(tableId, phase);
                snapshot.tables.Add(phase);
            }

            CaptureUnseatedQueueCustomers(snapshot);
            snapshot.peakSpawnRemainingGuests = Mathf.Max(0, peakSpawnRemainingGuests);
            snapshot.peakSpawnBatchActive = peakSpawnBatchActive;
            snapshot.peakSpawnBatchCooldown = Mathf.Max(0f, peakSpawnBatchCooldown);
            snapshot.peakSpawnActiveCapacityOverride = Mathf.Max(0, peakSpawnActiveCapacityOverride);

            foreach (var pair in frontCounterOrderProgressStarts)
            {
                var tableId = pair.Key;
                if (!frontCounterOrderProgressDurations.TryGetValue(tableId, out var duration) || duration <= 0f)
                {
                    continue;
                }

                var boundCount = 0;
                if (frontCounterOrderBindings.TryGetValue(tableId, out var bound) && bound != null)
                {
                    boundCount = bound.Count;
                }

                snapshot.frontOrders.Add(new TavernFrontOrderSnapshot
                {
                    tableId = tableId,
                    orderElapsed = Mathf.Max(0f, Time.time - pair.Value),
                    orderDuration = duration,
                    boundCount = boundCount
                });
            }

            return snapshot;
        }

        private void CaptureTableCustomerCounts(int tableId, TableArea table, TavernTablePhaseSnapshot phase)
        {
            var seatCap = table != null ? Mathf.Max(0, table.GetSeatCapacity()) : 0;
            if (frontCounterOrderBindings.TryGetValue(tableId, out var bound) && bound != null)
            {
                phase.queuedBoundCount = Mathf.Clamp(bound.Count, 0, seatCap > 0 ? seatCap : bound.Count);
                phase.seatedCount = 0;
                return;
            }

            if (!TryGetTableCustomerGroup(tableId, out var group) || group == null)
            {
                phase.seatedCount = 0;
                phase.queuedBoundCount = 0;
                return;
            }

            var seated = 0;
            for (var index = 0; index < group.Count; index++)
            {
                var customer = group[index];
                if (customer == null)
                {
                    continue;
                }

                // 已入座，或正在走向座位：恢复时都按入座重建。
                if (customer.IsSeated || customer.TableId == tableId)
                {
                    seated++;
                }
            }

            phase.seatedCount = seatCap > 0 ? Mathf.Min(seated, seatCap) : seated;
            phase.queuedBoundCount = 0;
        }

        /// <summary>
        /// 采集进店未入座、且未绑前台点单桌的客人（高峰排队主路径）。
        /// </summary>
        private void CaptureUnseatedQueueCustomers(TavernRuntimeSnapshotSaveData snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            snapshot.unseatedQueue ??= new List<TavernUnseatedCustomerSnapshot>();
            snapshot.unseatedQueue.Clear();

            var boundCustomers = CollectFrontCounterBoundCustomers();
            var captured = new HashSet<TavernCustomerRuntimeController>();
            CaptureUnseatedFromList(queuedCustomers, boundCustomers, captured, snapshot.unseatedQueue);
            CaptureUnseatedFromList(activeCustomers, boundCustomers, captured, snapshot.unseatedQueue);
        }

        private void CaptureUnseatedFromList(
            List<TavernCustomerRuntimeController> source,
            HashSet<TavernCustomerRuntimeController> boundCustomers,
            HashSet<TavernCustomerRuntimeController> captured,
            List<TavernUnseatedCustomerSnapshot> output)
        {
            if (source == null || output == null)
            {
                return;
            }

            for (var index = 0; index < source.Count; index++)
            {
                var customer = source[index];
                if (customer == null
                    || !captured.Add(customer)
                    || customer.IsLeavingTavern
                    || customer.IsSeated
                    || customer.TableId > 0
                    || (boundCustomers != null && boundCustomers.Contains(customer)))
                {
                    continue;
                }

                output.Add(new TavernUnseatedCustomerSnapshot
                {
                    kind = ResolveSnapshotCustomerKind(customer),
                    awaitingVipFloorChoice = customer.IsAwaitingVipFloorChoice
                });
            }
        }

        private HashSet<TavernCustomerRuntimeController> CollectFrontCounterBoundCustomers()
        {
            var bound = new HashSet<TavernCustomerRuntimeController>();
            foreach (var pair in frontCounterOrderBindings)
            {
                var list = pair.Value;
                if (list == null)
                {
                    continue;
                }

                for (var index = 0; index < list.Count; index++)
                {
                    if (list[index] != null)
                    {
                        bound.Add(list[index]);
                    }
                }
            }

            return bound;
        }

        private static int ResolveSnapshotCustomerKind(TavernCustomerRuntimeController customer)
        {
            if (customer == null)
            {
                return DataManager.PulledCustomerKindNormal;
            }

            if (customer.IsVip)
            {
                return DataManager.PulledCustomerKindVip;
            }

            return customer.IsRare
                ? DataManager.PulledCustomerKindRare
                : DataManager.PulledCustomerKindNormal;
        }

        private void CaptureTablePhaseTiming(int tableId, TavernTableRuntimeState state, TavernTablePhaseSnapshot phase)
        {
            if (state == TavernTableRuntimeState.Dining
                && tableDiningStartedAt.TryGetValue(tableId, out var diningStart)
                && tableDiningDurations.TryGetValue(tableId, out var diningDuration)
                && diningDuration > 0f)
            {
                phase.stateDuration = diningDuration;
                phase.stateElapsed = Mathf.Clamp(Time.time - diningStart, 0f, diningDuration);
                return;
            }

            if ((state == TavernTableRuntimeState.WaitingOrder || state == TavernTableRuntimeState.Reserved)
                && frontCounterOrderProgressStarts.TryGetValue(tableId, out var orderStart)
                && frontCounterOrderProgressDurations.TryGetValue(tableId, out var orderDuration)
                && orderDuration > 0f)
            {
                phase.stateDuration = orderDuration;
                phase.stateElapsed = Mathf.Clamp(Time.time - orderStart, 0f, orderDuration);
            }
        }

        private void CaptureCookTicket(int tableId, TavernTablePhaseSnapshot phase)
        {
            if (!tableCookOrderTickets.TryGetValue(tableId, out var ticket) || ticket == null || ticket.isCompleted)
            {
                return;
            }

            phase.hasCookTicket = true;
            phase.cookDuration = Mathf.Max(0f, ticket.cookDuration);
            phase.isChefNotified = ticket.isChefNotified;
            phase.isCooking = ticket.isCooking;
            if (ticket.isCooking && ticket.cookStartedAt > 0f && phase.cookDuration > 0f)
            {
                phase.cookElapsed = Mathf.Clamp(Time.time - ticket.cookStartedAt, 0f, phase.cookDuration);
            }
            else
            {
                phase.cookElapsed = 0f;
            }
        }

        /// <summary>
        /// 按快照重建客人 / 厨工单 / 前台点单 / 全局计时（2A 冻结：剩余 = duration - elapsed）。
        /// </summary>
        public void RestoreOwnTavernRuntimeSnapshot()
        {
            if (DataManager.Instance == null || DataManager.Instance.IsVisitingOtherTavern)
            {
                return;
            }

            if (!DataManager.Instance.HasValidTavernRuntimeSnapshot())
            {
                return;
            }

            var snapshot = DataManager.Instance.GetTavernRuntimeSnapshot();
            if (snapshot == null || !snapshot.valid)
            {
                return;
            }

            nextCustomerSpawnRemaining = snapshot.nextCustomerSpawnRemaining;
            ApplyRestoredBusinessCountdown(snapshot);

            if (snapshot.tables != null)
            {
                for (var index = 0; index < snapshot.tables.Count; index++)
                {
                    RestoreTablePhaseSnapshot(snapshot.tables[index]);
                }
            }

            // frontOrders 在桌阶段 WaitingOrder 恢复时已按剩余时长重启；此处补漏未覆盖的进度条。
            if (snapshot.frontOrders != null)
            {
                for (var index = 0; index < snapshot.frontOrders.Count; index++)
                {
                    RestoreFrontOrderSnapshotIfNeeded(snapshot.frontOrders[index]);
                }
            }

            RestoreUnseatedQueueSnapshot(snapshot);
            RestorePeakSpawnBatchSnapshot(snapshot);

            RefreshAllTableRuntimeState();
            // 兜底：占用态无人（含待上菜无菜无人）、或有菜无人 → 回 Idle。
            SanitizeOccupiedTablesWithoutCustomers();
            // 补派：待上菜但当时无空闲厨师的桌，在员工视觉就绪后再试一次。
            EnsureChefsCookingForRestoredWaitingServeTables();
            // 离线/回城再进店：小二全体立刻打盹；上下楼切场景跳过。
            if (!SceneFlowCoordinator.ConsumeSkipForceWaiterNapOnNextSnapshotRestore())
            {
                ForceAllWaitersNapAfterSnapshotRestore();
            }

            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
            // 通知顶栏倒计时按恢复后的剩余时间重启。
            Signals.Get<TavernBusinessStateSignal>().Dispatch(true);
        }

        /// <summary>
        /// 快照恢复后清理非法桌态：待上菜/用餐/结账无人，或桌上有菜/盘但无入座客人 → 默认 Idle。
        /// 待上菜通常尚无菜品视觉，仅靠 HasDishVisual 会漏掉「等待上菜却没人」。
        /// </summary>
        private void SanitizeOccupiedTablesWithoutCustomers()
        {
            foreach (var tablePair in AllTables)
            {
                var tableId = tablePair.Key;
                var table = tablePair.Value;
                if (table == null)
                {
                    continue;
                }

                if (TableHasSeatedCustomers(tableId))
                {
                    continue;
                }

                var tableData = DataManager.Instance != null
                    ? DataManager.Instance.GetTableData(tableId)
                    : null;
                var state = tableData != null
                    ? (TavernTableRuntimeState)tableData.runtimeState
                    : TavernTableRuntimeState.Idle;
                var needsReset = IsCustomerOccupiedTableState(state) || table.HasDishVisual;
                if (!needsReset)
                {
                    continue;
                }

                ClearTableDiningTiming(tableId);
                tableCookOrderTickets.Remove(tableId);
                tableStateService.SetIdle(tableId, table, clearDishVisual: true, dispatchRuntimeChanged: false);
            }
        }

        private bool TableHasSeatedCustomers(int tableId)
        {
            if (!TryGetTableCustomerGroup(tableId, out var group) || group == null || group.Count <= 0)
            {
                return false;
            }

            for (var index = 0; index < group.Count; index++)
            {
                var customer = group[index];
                if (customer != null && !customer.IsLeavingTavern)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCustomerOccupiedTableState(TavernTableRuntimeState state)
        {
            return state is TavernTableRuntimeState.WaitingServe
                or TavernTableRuntimeState.Dining
                or TavernTableRuntimeState.Checkout;
        }

        /// <summary>
        /// 遍历待上菜桌：保证有已通知工单，并尽量立刻派厨师做菜。
        /// </summary>
        private void EnsureChefsCookingForRestoredWaitingServeTables()
        {
            foreach (var tablePair in AllTables)
            {
                var tableId = tablePair.Key;
                var tableData = DataManager.Instance != null
                    ? DataManager.Instance.GetTableData(tableId)
                    : null;
                if (tableData == null
                    || !tableData.isUnlocked
                    || (TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.WaitingServe)
                {
                    continue;
                }

                if (!tableCookOrderTickets.TryGetValue(tableId, out var ticket) || ticket == null || ticket.isCompleted)
                {
                    tableCookOrderTickets[tableId] = new CookOrderTicket
                    {
                        tableId = tableId,
                        icon = ResolveDefaultOrderIcon(),
                        cookStartedAt = 0f,
                        cookDuration = GetEffectiveDishCookDuration(),
                        isChefNotified = true,
                        isCooking = false,
                        isCompleted = false
                    };
                }
                else
                {
                    ticket.isChefNotified = true;
                }

                TryAssignChefCookingAfterSnapshotRestore(tableId);
            }
        }

        /// <summary>
        /// 用快照中的经营剩余时间写回 businessOpenElapsed（优先 remaining，兼容旧档仅有 elapsed）。
        /// </summary>
        private void ApplyRestoredBusinessCountdown(TavernRuntimeSnapshotSaveData snapshot)
        {
            RefreshTimingConfig();
            var total = Mathf.Max(1f, BusinessHours);
            if (snapshot.businessRemainingSeconds >= 0f)
            {
                var remaining = Mathf.Clamp(snapshot.businessRemainingSeconds, 0f, total);
                businessOpenElapsedSeconds = Mathf.Clamp(total - remaining, 0f, total);
                return;
            }

            businessOpenElapsedSeconds = Mathf.Clamp(snapshot.businessOpenElapsedSeconds, 0f, total);
        }

        private void RestoreTablePhaseSnapshot(TavernTablePhaseSnapshot phase)
        {
            if (phase == null || phase.tableId <= 0)
            {
                return;
            }

            if (!AllTables.TryGetValue(phase.tableId, out var table) || table == null)
            {
                return;
            }

            var state = (TavernTableRuntimeState)phase.runtimeState;
            switch (state)
            {
                case TavernTableRuntimeState.WaitingServe:
                case TavernTableRuntimeState.Dining:
                case TavernTableRuntimeState.Checkout:
                    RestoreSeatedTablePhase(phase, table, state);
                    break;
                case TavernTableRuntimeState.WaitingOrder:
                case TavernTableRuntimeState.Reserved:
                    RestoreQueuedBoundTablePhase(phase, table, state);
                    break;
                case TavernTableRuntimeState.Cleaning:
                    tableStateService.SetCleaning(phase.tableId, table, "等待清理", dispatchRuntimeChanged: false);
                    break;
            }
        }

        private void RestoreSeatedTablePhase(
            TavernTablePhaseSnapshot phase,
            TableArea table,
            TavernTableRuntimeState state)
        {
            var seatCap = Mathf.Max(0, table.GetSeatCapacity());
            var seatedCount = Mathf.Clamp(phase.seatedCount, 0, seatCap > 0 ? seatCap : phase.seatedCount);
            var seated = SpawnInstantSeatedCustomers(phase.tableId, table, seatedCount);
            // 用餐/待上菜/结账必须有人；刷不出人则回默认空桌（无菜无人）。
            if (seated.Count <= 0)
            {
                ClearTableDiningTiming(phase.tableId);
                tableCookOrderTickets.Remove(phase.tableId);
                tableStateService.SetIdle(phase.tableId, table, clearDishVisual: true, dispatchRuntimeChanged: false);
                return;
            }

            customerFlowService.RegisterTableGroup(
                tableCustomers,
                tableCustomerGroups,
                phase.tableId,
                seated);

            switch (state)
            {
                case TavernTableRuntimeState.WaitingServe:
                    tableStateService.SetWaitingServe(phase.tableId, table, "待上菜", dispatchRuntimeChanged: false);
                    RestoreCookTicketFromPhase(phase);
                    break;
                case TavernTableRuntimeState.Dining:
                {
                    var remaining = ResolveRemainingSeconds(phase.stateElapsed, phase.stateDuration, dishEatDuration);
                    MarkTableDiningTiming(phase.tableId, remaining, elapsedAlready: 0f);
                    tableStateService.SetDining(
                        phase.tableId,
                        table,
                        remaining,
                        ResolveRestoreDiningDishPrefab(),
                        seated,
                        dispatchRuntimeChanged: false);
                    break;
                }
                case TavernTableRuntimeState.Checkout:
                    tableStateService.SetCheckout(
                        phase.tableId,
                        table,
                        "待结账",
                        showEmptyPlateVisual: true,
                        dispatchRuntimeChanged: false);
                    for (var index = 0; index < seated.Count; index++)
                    {
                        seated[index]?.MarkReadyCheckoutForRestore();
                    }

                    break;
            }
        }

        private void RestoreQueuedBoundTablePhase(
            TavernTablePhaseSnapshot phase,
            TableArea table,
            TavernTableRuntimeState state)
        {
            var seatCap = Mathf.Max(0, table.GetSeatCapacity());
            var boundCount = Mathf.Clamp(phase.queuedBoundCount, 0, seatCap > 0 ? seatCap : phase.queuedBoundCount);
            if (boundCount <= 0 && phase.seatedCount > 0)
            {
                // 桌边点单残留：按入座恢复 WaitingOrder。
                var seated = SpawnInstantSeatedCustomers(phase.tableId, table, Mathf.Min(phase.seatedCount, seatCap));
                if (seated.Count > 0)
                {
                    customerFlowService.RegisterTableGroup(
                        tableCustomers,
                        tableCustomerGroups,
                        phase.tableId,
                        seated);
                }

                tableStateService.SetWaitingOrder(phase.tableId, table, dispatchRuntimeChanged: false);
                return;
            }

            if (boundCount <= 0)
            {
                if (state == TavernTableRuntimeState.Reserved)
                {
                    tableStateService.SetReserved(phase.tableId, table, dispatchRuntimeChanged: false);
                }
                else
                {
                    tableStateService.SetWaitingOrder(phase.tableId, table, dispatchRuntimeChanged: false);
                }

                return;
            }

            var spawnPosition = customerEntryPoint != null ? customerEntryPoint.position : table.GetCustomerTargetPosition();
            var boundCustomers = new List<TavernCustomerRuntimeController>(boundCount);
            for (var index = 0; index < boundCount; index++)
            {
                var customer = SpawnCustomerRuntime(spawnPosition);
                if (customer == null)
                {
                    break;
                }

                customerFlowService.EnqueueCustomer(queuedCustomers, customer);
                customer.BindTableForFrontCounterOrder(phase.tableId, index);
                customer.SetFrontCounterOrderCandidate(true);
                boundCustomers.Add(customer);
            }

            if (boundCustomers.Count <= 0)
            {
                return;
            }

            UpdateQueuePositions();
            tableStateService.SetReserved(phase.tableId, table, dispatchRuntimeChanged: false);
            customerFlowService.RegisterTableGroup(
                tableCustomers,
                tableCustomerGroups,
                phase.tableId,
                boundCustomers);
            frontCounterOrderBindings[phase.tableId] = boundCustomers;
            tableStateService.SetWaitingOrder(phase.tableId, table, dispatchRuntimeChanged: false);
            waitSatisfactionTracker.OnWaitingOrder(phase.tableId);
            HideWaitingOrderBubbleForTable(phase.tableId);

            var orderRemaining = ResolveRemainingSeconds(
                phase.stateElapsed,
                phase.stateDuration,
                GetEffectiveWaiterOrderDuration());
            StartFrontCounterOrderProcess(phase.tableId, orderRemaining);
        }

        private void RestoreFrontOrderSnapshotIfNeeded(TavernFrontOrderSnapshot frontOrder)
        {
            if (frontOrder == null || frontOrder.tableId <= 0)
            {
                return;
            }

            if (frontCounterOrderSessions.ContainsKey(frontOrder.tableId)
                || frontCounterOrderRoutines.ContainsKey(frontOrder.tableId))
            {
                return;
            }

            if (!IsTableInState(frontOrder.tableId, TavernTableRuntimeState.WaitingOrder))
            {
                return;
            }

            var remaining = ResolveRemainingSeconds(
                frontOrder.orderElapsed,
                frontOrder.orderDuration,
                GetEffectiveWaiterOrderDuration());
            StartFrontCounterOrderProcess(frontOrder.tableId, remaining);
        }

        /// <summary>
        /// 按快照重建未入座排队客（含高峰排队、门口等楼层选择的贵客）。
        /// </summary>
        private void RestoreUnseatedQueueSnapshot(TavernRuntimeSnapshotSaveData snapshot)
        {
            if (snapshot?.unseatedQueue == null || snapshot.unseatedQueue.Count <= 0)
            {
                return;
            }

            if (!TryGetSpawnPosition(out var spawnPosition))
            {
                spawnPosition = customerEntryPoint != null
                    ? customerEntryPoint.position
                    : Vector3.zero;
            }

            for (var index = 0; index < snapshot.unseatedQueue.Count; index++)
            {
                var entry = snapshot.unseatedQueue[index];
                if (entry == null)
                {
                    continue;
                }

                var asVip = entry.kind == DataManager.PulledCustomerKindVip;
                var asRare = entry.kind == DataManager.PulledCustomerKindRare;
                var customer = SpawnCustomerRuntime(spawnPosition, asVip: asVip, asRare: asRare);
                if (customer == null && (asVip || asRare))
                {
                    customer = SpawnCustomerRuntime(spawnPosition);
                }

                if (customer == null)
                {
                    continue;
                }

                if (customer.IsVip)
                {
                    EnqueueSpawnedCustomer(customer);
                    ShowVipGuestActionBubble(customer);
                    continue;
                }

                customerFlowService.EnqueueCustomer(queuedCustomers, customer);
                waitSatisfactionTracker.OnCustomerQueued(customer.GetInstanceID());
            }

            UpdateQueuePositions();
        }

        /// <summary>
        /// 续跑离店时未刷完的高峰分批，避免回店后高峰队列断档。
        /// </summary>
        private void RestorePeakSpawnBatchSnapshot(TavernRuntimeSnapshotSaveData snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            peakSpawnRemainingGuests = Mathf.Max(0, snapshot.peakSpawnRemainingGuests);
            peakSpawnBatchActive = snapshot.peakSpawnBatchActive && peakSpawnRemainingGuests > 0;
            peakSpawnBatchCooldown = Mathf.Max(0f, snapshot.peakSpawnBatchCooldown);
            var restoredOverride = Mathf.Max(0, snapshot.peakSpawnActiveCapacityOverride);
            peakSpawnActiveCapacityOverride = Mathf.Max(
                restoredOverride,
                activeCustomers.Count + peakSpawnRemainingGuests);

            if (!peakSpawnBatchActive)
            {
                if (peakSpawnRemainingGuests <= 0)
                {
                    peakSpawnActiveCapacityOverride = 0;
                    peakSpawnBatchCooldown = 0f;
                }

                return;
            }

            spawnGroupSizeCap = 0;
        }

        private void RestoreCookTicketFromPhase(TavernTablePhaseSnapshot phase)
        {
            if (phase == null || phase.tableId <= 0)
            {
                return;
            }

            var cookDuration = phase.hasCookTicket && phase.cookDuration > 0f
                ? phase.cookDuration
                : GetEffectiveDishCookDuration();
            var resumeCooking = phase.hasCookTicket
                                && phase.isCooking
                                && cookDuration > 0f
                                && phase.cookElapsed < cookDuration;
            var cookElapsed = resumeCooking
                ? Mathf.Clamp(phase.cookElapsed, 0f, cookDuration)
                : 0f;

            tableCookOrderTickets[phase.tableId] = new CookOrderTicket
            {
                tableId = phase.tableId,
                icon = ResolveDefaultOrderIcon(),
                cookStartedAt = resumeCooking ? Time.time - cookElapsed : 0f,
                cookDuration = cookDuration,
                // 离线回来：待上菜视为已通知后厨，不再等小二跑一趟。
                isChefNotified = true,
                isCooking = resumeCooking,
                isCompleted = false
            };

            TryAssignChefCookingAfterSnapshotRestore(phase.tableId);
        }

        /// <summary>
        /// 快照恢复后立刻给待上菜桌派厨师；无空闲厨师时留给 ChefServiceLoop。
        /// </summary>
        private void TryAssignChefCookingAfterSnapshotRestore(int tableId)
        {
            if (tableId <= 0 || assignedCookTableIds.Contains(tableId))
            {
                return;
            }

            if (!tableCookOrderTickets.TryGetValue(tableId, out var ticket)
                || ticket == null
                || ticket.isCompleted
                || !ticket.isChefNotified)
            {
                return;
            }

            var task = new CookDishTask(tableId);
            var chef = GetAvailableChefForTask(task);
            if (chef == null)
            {
                return;
            }

            // TryStartChefTask：已 cooking 则保留剩余时长，否则立刻 StartCookOrderTicket。
            TryStartChefTask(chef, task, new ChefCookingState());
        }

        private List<TavernCustomerRuntimeController> SpawnInstantSeatedCustomers(
            int tableId,
            TableArea table,
            int seatedCount)
        {
            var result = new List<TavernCustomerRuntimeController>();
            if (table == null || seatedCount <= 0 || customerTemplates.Count == 0)
            {
                return result;
            }

            for (var seatIndex = 0; seatIndex < seatedCount; seatIndex++)
            {
                var spawnPosition = table.TryGetSeatPoseByIndex(seatIndex, out var seatPosition, out _)
                    ? seatPosition
                    : table.GetCustomerTargetPosition();
                var customer = SpawnCustomerRuntime(spawnPosition);
                if (customer == null)
                {
                    break;
                }

                customer.InstantSeatAtTable(tableId, seatIndex);
                result.Add(customer);
            }

            return result;
        }

        private static float ResolveRemainingSeconds(float elapsed, float duration, float fallbackDuration)
        {
            var total = duration > 0f ? duration : Mathf.Max(0.1f, fallbackDuration);
            return Mathf.Max(0.1f, total - Mathf.Max(0f, elapsed));
        }

        private GameObject ResolveRestoreDiningDishPrefab()
        {
            if (dishPrefabs != null && dishPrefabs.Count > 0)
            {
                return dishPrefabs[0];
            }

            return platePrefab;
        }

        private void MarkTableDiningTiming(int tableId, float duration, float elapsedAlready)
        {
            if (tableId <= 0 || duration <= 0f)
            {
                return;
            }

            tableDiningDurations[tableId] = duration;
            tableDiningStartedAt[tableId] = Time.time - Mathf.Max(0f, elapsedAlready);
        }

        private void ClearTableDiningTiming(int tableId)
        {
            tableDiningStartedAt.Remove(tableId);
            tableDiningDurations.Remove(tableId);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                CaptureOwnTavernRuntimeSnapshot();
            }
        }

        private void OnApplicationQuit()
        {
            CaptureOwnTavernRuntimeSnapshot();
        }
    }
}
