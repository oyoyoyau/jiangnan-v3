using System.Collections.Generic;
using JN.Client.Manager;
using JN.Client.Model;
using UnityEngine;

namespace JN.Client.Scene
{
    public partial class TavernSceneManager
    {
        private const float AchievementSampleInterval = 0.5f;

        private float achievementSampleTimer;

        public bool IsBusinessOpenForWalkout()
        {
            // 耐心条/离场不依赖刷客循环是否激活，避免接客暂停时 HUD 被整表 Clear。
            return DataManager.Instance != null
                   && DataManager.Instance.TavernData != null
                   && DataManager.Instance.TavernData.isOpen
                   && !isClosingBusiness;
        }

        public IReadOnlyList<TavernCustomerRuntimeController> GetQueuedCustomersSnapshot()
        {
            return queuedCustomers;
        }

        public float GetCustomerQueueWaitSeconds(TavernCustomerRuntimeController customer)
        {
            if (customer == null)
            {
                return 0f;
            }

            return waitSatisfactionTracker.PeekQueueWait(customer.GetInstanceID());
        }

        public float GetTableServeWaitSeconds(int tableId)
        {
            return waitSatisfactionTracker.PeekIncomplete(tableId).ServeSeconds;
        }

        public List<int> GetWaitingServeTableIdsSnapshot()
        {
            var result = new List<int>();
            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance?.GetTableData(tablePair.Key);
                if (tableData == null
                    || !tableData.isUnlocked
                    || (TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.WaitingServe)
                {
                    continue;
                }

                result.Add(tablePair.Key);
            }

            return result;
        }

        public bool TryGetWalkoutCustomerForTable(int tableId, out TavernCustomerRuntimeController customer)
        {
            customer = null;
            if (!TryGetTableCustomerGroup(tableId, out var customers) || customers == null || customers.Count == 0)
            {
                return false;
            }

            customer = customers[0];
            return customer != null;
        }

        public void TriggerCustomerWalkout(TavernCustomerRuntimeController customer, CustomerWalkoutReason reason)
        {
            if (customer == null || reason == CustomerWalkoutReason.None)
            {
                return;
            }

            if (customer.TableId > 0)
            {
                TriggerTableWalkout(customer.TableId, reason);
                return;
            }

            DataManager.Instance?.RecordCustomerWalkout(reason, customer.IsVip);
            customer.LeaveTavern(reason);
        }

        private void TriggerTableWalkout(int tableId, CustomerWalkoutReason reason)
        {
            if (!TryGetTableCustomerGroup(tableId, out var customers) || customers == null)
            {
                return;
            }

            var snapshot = new List<TavernCustomerRuntimeController>(customers);
            AbandonTableForWalkout(tableId);
            for (var index = 0; index < snapshot.Count; index++)
            {
                var customer = snapshot[index];
                if (customer == null)
                {
                    continue;
                }

                DataManager.Instance?.RecordCustomerWalkout(reason, customer.IsVip);
                customer.LeaveTavern(reason);
            }
        }

        private void AbandonTableForWalkout(int tableId)
        {
            waitSatisfactionTracker.ClearTable(tableId);
            VipGuestDishGuessService.ClearTableSession(tableId);
            CancelFrontCounterOrderRoutine(tableId);
            frontCounterOrderBindings.Remove(tableId);
            pendingVipMenuRejectAfterSeatTableIds.Remove(tableId);
            tableCustomerGroups.Remove(tableId);
            tableCustomers.Remove(tableId);
            if (AllTables.TryGetValue(tableId, out var table) && table != null)
            {
                tableStateService.SetIdle(tableId, table);
            }
        }

        public int GetPreparedDishQueueCountPublic()
        {
            return GetPreparedDishQueueCount();
        }

        private int CountTablesInRuntimeState(TavernTableRuntimeState targetState)
        {
            var count = 0;
            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance?.GetTableData(tablePair.Key);
                if (tableData == null
                    || !tableData.isUnlocked
                    || (TavernTableRuntimeState)tableData.runtimeState != targetState)
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private int CountActiveVipCustomers()
        {
            var count = 0;
            for (var index = 0; index < activeCustomers.Count; index++)
            {
                var customer = activeCustomers[index];
                if (customer != null && customer.IsVip)
                {
                    count++;
                }
            }

            return count;
        }

        private void SampleAchievementPeaks()
        {
            if (!IsBusinessOpenForWalkout())
            {
                return;
            }

            DataManager.Instance?.UpdateAchievementPeakSamples(
                GetQueueCustomerCount(),
                GetPreparedDishQueueCountPublic(),
                CountTablesInRuntimeState(TavernTableRuntimeState.Checkout),
                CountTablesInRuntimeState(TavernTableRuntimeState.Cleaning),
                CountActiveVipCustomers());
        }

        private void TickAchievementSystems(float deltaTime)
        {
            if (!IsBusinessOpenForWalkout())
            {
                achievementSampleTimer = 0f;
                ClearCustomerWaitHud();
                return;
            }

            achievementSampleTimer += deltaTime;
            if (achievementSampleTimer >= AchievementSampleInterval)
            {
                achievementSampleTimer = 0f;
                SampleAchievementPeaks();
            }

            TickCustomerWaitHud(deltaTime);
        }

        private void HandleCustomerExitedAchievement(TavernCustomerRuntimeController customer)
        {
            if (customer == null)
            {
                return;
            }

            SampleAchievementPeaks();
        }

        [Header("Customer Wait HUD Icons (optional dedicated sprites)")]
        [SerializeField] private Sprite customerQueueWaitIcon;
        [SerializeField] private Sprite customerOrderWaitIcon;
        [SerializeField] private Sprite customerServeWaitIcon;
        [SerializeField] private Sprite customerCheckoutWaitIcon;

        private readonly TavernCustomerWaitHudService customerWaitHudService = new();
        private int nextWaitHudGroupId = 1;

        public bool TryGetTableCustomerGroupForWaitHud(int tableId, out List<TavernCustomerRuntimeController> customers)
        {
            return customerFlowService.TryGetTableCustomerGroup(tableCustomerGroups, tableId, out customers);
        }

        public float GetTableOrderWaitSeconds(int tableId)
        {
            return waitSatisfactionTracker.PeekIncomplete(tableId).OrderSeconds;
        }

        public float GetTableCheckoutWaitSeconds(int tableId)
        {
            return waitSatisfactionTracker.PeekIncomplete(tableId).CheckoutSeconds;
        }

        public Sprite ResolveCustomerWaitHudFallbackIcon(CustomerWaitHudState state)
        {
            return state switch
            {
                CustomerWaitHudState.Queue => customerQueueWaitIcon != null
                    ? customerQueueWaitIcon
                    : waiterIdleIcon != null ? waiterIdleIcon : waiterOrderingIcon,
                CustomerWaitHudState.WaitingOrder => customerOrderWaitIcon != null ? customerOrderWaitIcon : waiterOrderingIcon,
                CustomerWaitHudState.WaitingServe => customerServeWaitIcon != null ? customerServeWaitIcon : waiterServingIcon,
                CustomerWaitHudState.WaitingCheckout => customerCheckoutWaitIcon != null
                    ? customerCheckoutWaitIcon
                    : ResolveWaiterCheckoutIcon(),
                _ => null,
            };
        }

        public int AllocateWaitHudGroupId()
        {
            return nextWaitHudGroupId++;
        }

        public void AssignWaitHudGroupMembers(IReadOnlyList<TavernCustomerRuntimeController> members)
        {
            if (members == null || members.Count == 0)
            {
                return;
            }

            var groupId = AllocateWaitHudGroupId();
            for (var index = 0; index < members.Count; index++)
            {
                members[index]?.SetWaitHudGroupId(groupId);
            }
        }

        public void AssignWaitHudGroupMember(TavernCustomerRuntimeController customer)
        {
            customer?.SetWaitHudGroupId(AllocateWaitHudGroupId());
        }

        private void TickCustomerWaitHud(float deltaTime)
        {
            // 仅：已站定排队位 + 无可坐空桌 + 未点单 的前两名。
            var eligible = GetQueuePatienceEligibleCustomers(FrontCounterOrderSlotCount);
            var eligibleIds = new List<int>(eligible.Count);
            for (var index = 0; index < eligible.Count; index++)
            {
                eligibleIds.Add(eligible[index].GetInstanceID());
            }

            waitSatisfactionTracker.SyncQueuePatienceForEligible(eligibleIds);
            customerWaitHudService.Tick(this, deltaTime);
        }

        /// <summary>
        /// 排队耐心：进店走路不显示；须已站定排队位、无可坐空桌、且未进入前台点单。
        /// </summary>
        public bool IsQueueCustomerEligibleForPatience(TavernCustomerRuntimeController customer)
        {
            if (customer == null || customer.IsLeavingTavern)
            {
                return false;
            }

            // 未点单（含软预留绑定视为已点单）。
            if (IsCustomerBoundToFrontCounterOrder(customer))
            {
                return false;
            }

            // 须已排到位，进店途中不出现。
            if (!customer.IsQueueSlotReady)
            {
                return false;
            }

            var queueIndex = queuedCustomers.IndexOf(customer);
            if (queueIndex < 0 || queueIndex >= FrontCounterOrderSlotCount)
            {
                return false;
            }

            // 有可坐空桌时应点单，不显示排队耐心。
            return !HasIdleTableAvailableForFrontCounterOrder();
        }

        /// <summary>
        /// 取排队列表前 maxCount 位中符合耐心显示条件的客人（不下探队尾）。
        /// </summary>
        public List<TavernCustomerRuntimeController> GetQueuePatienceEligibleCustomers(int maxCount)
        {
            var result = new List<TavernCustomerRuntimeController>(Mathf.Max(0, maxCount));
            if (maxCount <= 0)
            {
                return result;
            }

            var scanCount = Mathf.Min(queuedCustomers.Count, maxCount);
            for (var index = 0; index < scanCount; index++)
            {
                var customer = queuedCustomers[index];
                if (IsQueueCustomerEligibleForPatience(customer))
                {
                    result.Add(customer);
                }
            }

            return result;
        }

        /// <summary>
        /// 是否存在可供前台点单软预留的空闲桌（有则可坐，排队耐心应隐藏）。
        /// </summary>
        public bool HasIdleTableAvailableForFrontCounterOrder()
        {
            foreach (var tablePair in AllTables)
            {
                var tableId = tablePair.Key;
                var table = tablePair.Value;
                var tableData = DataManager.Instance != null ? DataManager.Instance.GetTableData(tableId) : null;
                if (tableData == null
                    || !tableData.isUnlocked
                    || (TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.Idle
                    || pendingUpgradeTableIds.Contains(tableId)
                    || IsTableBlockedForNewSeating(tableId)
                    || table == null
                    || table.GetSeatCapacity() < 1
                    || frontCounterOrderBindings.ContainsKey(tableId))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private void ClearCustomerWaitHud()
        {
            customerWaitHudService.Clear();
        }

        public void ReleaseCustomerWaitHudForCustomer(TavernCustomerRuntimeController customer)
        {
            customerWaitHudService.ReleaseCustomer(customer);
        }

        /// <summary>
        /// 小二已接单/上菜/结账时，立刻隐藏对应桌位的等待气泡与「等待上菜」等桌边文案。
        /// </summary>
        internal void SuppressTableCustomerWaitHud(int tableId, CustomerWaitHudState state)
        {
            if (tableId <= 0 || state == CustomerWaitHudState.None)
            {
                return;
            }

            customerWaitHudService.ReleaseTableWaitState(tableId, state);
            if (AllTables.TryGetValue(tableId, out var table))
            {
                table.linkedUI?.SetCustomerWaitHudActive(false);
            }
        }
    }
}
