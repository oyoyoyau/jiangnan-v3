using System.Collections.Generic;
using cfg;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.UI;
using UnityEngine;

namespace JN.Client.Scene
{
    /// <summary>
    /// 桌位「客人被拉走」提示：回自家/拜访他人时按桌独立概率触发。
    /// </summary>
    public partial class TavernSceneManager
    {
        private readonly HashSet<int> tablesWithPulledTip = new();
        /// <summary>被拉客提示文案/头像，Capture 他人店桌快照时读取。</summary>
        private readonly Dictionary<int, PulledTipRuntimeMeta> pulledTipMetas = new();

        private sealed class PulledTipRuntimeMeta
        {
            public bool isSelf;
            public string pullerName;
            public int headIconId;
        }

        /// <summary>
        /// 回自家酒楼：若从城镇进入则按配置概率每桌独立判定。
        /// </summary>
        private void TryRollOwnTavernPulledTipsAfterEnter()
        {
            if (!SceneFlowCoordinator.ConsumeOwnTavernPulledTipRoll())
            {
                return;
            }

            RollAndApplyPulledTips(TbConfigRuntime.GetOwnTavernPulledTipChance(0.2f));
        }

        /// <summary>
        /// 拜访他人酒楼开场：按他人概率每桌独立判定。
        /// </summary>
        private void TryRollVisitTavernPulledTips()
        {
            if (DataManager.Instance == null || !DataManager.Instance.IsVisitingOtherTavern)
            {
                return;
            }

            RollAndApplyPulledTips(TbConfigRuntime.GetOtherTavernPulledTipChance(0.25f));
        }

        private void RollAndApplyPulledTips(float chance)
        {
            chance = Mathf.Clamp01(chance);
            if (chance <= 0f || DataManager.Instance == null)
            {
                return;
            }

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

                if (!AllTables.TryGetValue(tableId, out var table) || table == null)
                {
                    continue;
                }

                // 有贵客的桌不可被拉走，避免爆满店保底贵客被清空。
                if (TableHasVipCustomer(tableId))
                {
                    continue;
                }

                if (Random.value > chance)
                {
                    continue;
                }

                // 触发桌必须清空入座客人后再挂提示。
                EvictTableCustomersSilentlyForPulledTip(tableId);
                ShowPulledTipOnTable(tableId, table);
            }
        }

        private void ShowPulledTipOnTable(int tableId, TableArea table)
        {
            if (table == null || !TryPickRandomTownBuildingPuller(out var pullerName, out var headIconId))
            {
                return;
            }

            ClearPulledTipOnTable(tableId);
            var visiting = DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern;
            var view = HudOverlayService.ShowTablePulledTip(
                table.transform,
                tableId,
                headIconId,
                pullerName,
                onClick: visiting ? null : () => OnPulledTipClicked(tableId),
                clickEnabled: !visiting);
            if (view == null)
            {
                return;
            }

            RememberPulledTipMeta(tableId, isSelf: false, pullerName, headIconId);
            tablesWithPulledTip.Add(tableId);
        }

        /// <summary>
        /// 按已有 meta 挂被拉客提示（拜访快照还原，不再随机拉客方）。
        /// </summary>
        private void ShowPulledTipOnTableFromSnapshot(
            int tableId,
            TableArea table,
            bool isSelf,
            string pullerName,
            int headIconId)
        {
            if (table == null)
            {
                return;
            }

            if (isSelf)
            {
                TryShowSelfVisitPullTipOnTable(tableId);
                return;
            }

            ClearPulledTipOnTable(tableId);
            var visiting = DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern;
            var view = HudOverlayService.ShowTablePulledTip(
                table.transform,
                tableId,
                headIconId,
                pullerName,
                onClick: visiting ? null : () => OnPulledTipClicked(tableId),
                clickEnabled: !visiting);
            if (view == null)
            {
                return;
            }

            RememberPulledTipMeta(tableId, isSelf: false, pullerName, headIconId);
            tablesWithPulledTip.Add(tableId);
        }

        /// <summary>
        /// 拜访中自己拉客：该桌尚无提示时挂「客人已被我拉走」+ 自家头像；已有提示则不重复弹出。
        /// </summary>
        private void TryShowSelfVisitPullTipOnTable(int tableId)
        {
            if (tableId <= 0 || tablesWithPulledTip.Contains(tableId))
            {
                return;
            }

            // 桌上仍有贵客时不挂「被拉走」标记。
            if (TableHasVipCustomer(tableId))
            {
                return;
            }

            if (!AllTables.TryGetValue(tableId, out var table) || table == null)
            {
                return;
            }

            // 拜访他人：自己拉客提示可见但不可点。
            var view = HudOverlayService.ShowTablePulledTip(
                table.transform,
                tableId,
                headIconId: 0,
                pullerName: null,
                onClick: null,
                displayCaption: "客人已被我拉走",
                useSelfHeadIcon: true,
                clickEnabled: false);
            if (view == null)
            {
                return;
            }

            RememberPulledTipMeta(tableId, isSelf: true, pullerName: null, headIconId: 0);
            tablesWithPulledTip.Add(tableId);
        }

        private void RememberPulledTipMeta(int tableId, bool isSelf, string pullerName, int headIconId)
        {
            if (tableId <= 0)
            {
                return;
            }

            pulledTipMetas[tableId] = new PulledTipRuntimeMeta
            {
                isSelf = isSelf,
                pullerName = pullerName,
                headIconId = headIconId
            };
        }

        private bool TryGetPulledTipMeta(
            int tableId,
            out bool isSelf,
            out string pullerName,
            out int headIconId)
        {
            isSelf = false;
            pullerName = null;
            headIconId = 0;
            if (!pulledTipMetas.TryGetValue(tableId, out var meta) || meta == null)
            {
                return false;
            }

            isSelf = meta.isSelf;
            pullerName = meta.pullerName;
            headIconId = meta.headIconId;
            return true;
        }

        /// <summary>
        /// 拜访中带被拉客提示的桌：本趟不再派座，避免入座立刻清掉提示。
        /// </summary>
        private bool IsVisitPulledTipSeatingBlocked(int tableId)
        {
            return DataManager.IsInOtherTavernVisitSession && tablesWithPulledTip.Contains(tableId);
        }

        /// <summary>
        /// 新客人派座/软预留时不可用：小二打盹占用，或拜访中带被拉客提示的桌。
        /// </summary>
        private bool IsTableBlockedForNewSeating(int tableId)
        {
            return IsTableBlockedByWaiterNap(tableId) || IsVisitPulledTipSeatingBlocked(tableId);
        }

        /// <summary>
        /// 点击被拉客提示：拜访不可点；自家关闭并 tips「本桌收益损失X元」。
        /// </summary>
        private void OnPulledTipClicked(int tableId)
        {
            if (tableId <= 0 || !tablesWithPulledTip.Contains(tableId))
            {
                return;
            }

            var dataManager = DataManager.Instance;
            if (dataManager != null && dataManager.IsVisitingOtherTavern)
            {
                return;
            }

            var loss = EstimatePulledTipTableIncomeLoss(tableId);
            ClearPulledTipOnTable(tableId);
            HudOverlayService.ShowFloatingWarning($"本桌收益损失{loss}元");
        }

        /// <summary>
        /// 预估本桌结账收益：当前酒楼等级单价 × 座位数（不含浮动与贵客倍率）。
        /// </summary>
        private int EstimatePulledTipTableIncomeLoss(int tableId)
        {
            var tavernLevel = DataManager.Instance != null ? DataManager.Instance.GetTavernLevel() : 1;
            var unitPrice = Mathf.Max(1, TbConfigRuntime.GetTableCheckoutIncomeForLevel(tavernLevel, tableCheckoutIncome));
            if (DataManager.Instance != null)
            {
                unitPrice = DataManager.Instance.ApplyActiveTavernMenuCheckoutUnitPrice(unitPrice);
            }
            var seats = 1;
            if (AllTables.TryGetValue(tableId, out var table) && table != null)
            {
                seats = Mathf.Max(1, table.GetSeatCapacity());
            }

            return unitPrice * seats;
        }

        /// <summary>新客人入座后收起该桌被拉客提示。</summary>
        private void ClearPulledTipOnTable(int tableId)
        {
            if (tableId <= 0)
            {
                return;
            }

            tablesWithPulledTip.Remove(tableId);
            pulledTipMetas.Remove(tableId);
            HudOverlayService.ReleaseTablePulledTip(tableId);
        }

        private void ClearAllPulledTipsLocal()
        {
            if (tablesWithPulledTip.Count == 0)
            {
                pulledTipMetas.Clear();
                return;
            }

            var snapshot = new List<int>(tablesWithPulledTip);
            tablesWithPulledTip.Clear();
            pulledTipMetas.Clear();
            for (var index = 0; index < snapshot.Count; index++)
            {
                HudOverlayService.ReleaseTablePulledTip(snapshot[index]);
            }
        }

        private void EvictTableCustomersSilentlyForPulledTip(int tableId)
        {
            if (tableId <= 0)
            {
                return;
            }

            List<TavernCustomerRuntimeController> snapshot = null;
            if (TryGetTableCustomerGroup(tableId, out var customers) && customers != null && customers.Count > 0)
            {
                snapshot = new List<TavernCustomerRuntimeController>(customers);
            }

            AbandonTableForWalkout(tableId);

            if (snapshot == null)
            {
                return;
            }

            for (var index = 0; index < snapshot.Count; index++)
            {
                var customer = snapshot[index];
                if (customer == null)
                {
                    continue;
                }

                ClearVipGuestActionBubble(customer);
                customerWaitHudService.ReleaseCustomer(customer);
                activeCustomers.Remove(customer);
                queuedCustomers.Remove(customer);
                ReleaseCustomerContext(customer);
                if (customer.gameObject != null)
                {
                    Destroy(customer.gameObject);
                }
            }
        }

        private static bool TryPickRandomTownBuildingPuller(out string pullerName, out int headIconId)
        {
            pullerName = "他人";
            headIconId = 1;
            var rows = TownBuildingConfigUtility.GetAll();
            if (rows == null || rows.Count == 0)
            {
                return false;
            }

            // 被拉客提示的拉客方必须是他人酒楼配置，绝不用自家 tx.png / 玩家名。
            var excludeFieldId = 0;
            var dataManager = DataManager.Instance;
            if (dataManager != null)
            {
                var owned = dataManager.GetOwnedTownBuilding();
                if (owned != null)
                {
                    excludeFieldId = owned.tileId;
                }
            }

            var candidates = new List<TownBuilding>(rows.Count);
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row == null)
                {
                    continue;
                }

                if (excludeFieldId > 0 && row.FieldId == excludeFieldId)
                {
                    continue;
                }

                candidates.Add(row);
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            var pick = candidates[Random.Range(0, candidates.Count)];
            pullerName = string.IsNullOrWhiteSpace(pick.Name) ? "他人" : pick.Name;
            headIconId = pick.HeadIconId >= 1 && pick.HeadIconId <= 8 ? pick.HeadIconId : 1;
            return true;
        }
    }
}
