using System.Collections.Generic;
using cfg;
using JN.Client;
using JN.Client.Config;
using JN.Client.Model;
using JN.Client.Scene;
using JN.Client.UI;
using QFramework;
using UnityEngine;

namespace JN.Client.Manager
{
    /// <summary>
    /// 负责数据相关的运行时逻辑。
    /// </summary>
    public partial class DataManager
    {
        /// <summary>
        /// 尝试处理领取贷款。
        /// </summary>
        /// <param name="loan金额">参数值。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        public bool TryTakeLoan(out int loanAmount)
        {
            EnsureInitialized();
            loanAmount = 0;

            if (GetRemainingLoanCount() <= 0)
            {
                return false;
            }

            loanAmount = GetNextLoanAmount();
            GameplayData.openingLoanClaimed = true;
            GameplayData.loanCount += 1;
            GameplayData.pendingLoanAmount = 0;
            GameplayData.waitingForLoanApproval = false;
            ChangeCoinNum(loanAmount);
            SaveGame();
            return true;
        }

        /// <summary>
        /// 跳过开局「建造资金」贷款窗：不发放铜钱，标记已处理，并免费领取默认地块。
        /// </summary>
        public bool SkipOpeningLoanAndClaimStarterTile(out int tileId, out string message)
        {
            EnsureInitialized();
            EnsureTownBuildingDefaults();
            tileId = 0;
            message = string.Empty;

            GameplayData.openingLoanClaimed = true;
            GameplayData.pendingLoanAmount = 0;
            GameplayData.waitingForLoanApproval = false;

            var selfPlayerId = ResolveCurrentPlayerId();
            if (selfPlayerId > 0 && HasOwnedTownBuilding(selfPlayerId))
            {
                var owned = GetOwnedTownBuilding(selfPlayerId);
                tileId = owned != null ? owned.tileId : 0;
                SaveGame();
                message = "已拥有地块";
                return true;
            }

            tileId = ResolveDefaultPurchasableTownTileId();
            if (tileId <= 0)
            {
                SaveGame();
                message = "未找到可领取的默认地块";
                return false;
            }

            if (!TryPurchaseTownLand(tileId, out message))
            {
                SaveGame();
                return false;
            }

            return true;
        }

        /// <summary>
        /// 解析新手开店用的默认空闲地块：优先 Config.selfBuildingFieldId。
        /// </summary>
        public int ResolveDefaultPurchasableTownTileId()
        {
            EnsureTownBuildingDefaults();
            var selfFieldId = GetSelfBuildingFieldId();
            if (selfFieldId > 0)
            {
                var selfField = SaveData.town.buildingInfos.Find(info => info != null && info.tileId == selfFieldId);
                if (selfField != null && selfField.playerId == 0)
                {
                    return selfFieldId;
                }
            }

            var bestTileId = 0;
            for (var index = 0; index < SaveData.town.buildingInfos.Count; index++)
            {
                var info = SaveData.town.buildingInfos[index];
                if (info == null || info.playerId != 0 || !IsSelfTownBuildingField(info.tileId))
                {
                    continue;
                }

                if (bestTileId == 0 || info.tileId < bestTileId)
                {
                    bestTileId = info.tileId;
                }
            }

            return bestTileId;
        }

        /// <summary>
        /// 修改可用菜品。
        /// </summary>
        /// <param name="delta">参数值。</param>
        public void ChangeAvailableDishes(int delta)
        {
            EnsureTavernDefaults();
            SaveData.tavern.availableDishes = Mathf.Max(0, SaveData.tavern.availableDishes + delta);
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
            SaveGame();
        }

        /// <summary>
        /// 重置临时酒楼状态。
        /// </summary>
        public void ResetTransientTavernState()
        {
            EnsureTavernDefaults();
            SaveData.tavern.availableDishes = 0;
            foreach (var table in SaveData.tavern.tables)
            {
                table.runtimeState = table.isUnlocked
                    ? (int)TavernTableRuntimeState.Idle
                    : (int)TavernTableRuntimeState.Locked;
            }

            // 正式打烊 / 开业重置：清掉营业中离店快照，避免下次开业误恢复旧客人。
            ClearTavernRuntimeSnapshot(save: false);

            tableNum = GetUnlockedTableCount();
            Signals.Get<TableNumSignal>().Dispatch();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
            SaveGame();
        }

        /// <summary>
        /// 满 10 桌后的累计开业菜价倍率（1 = 无加成，每开业一次 +20%）。
        /// </summary>
        public float GetPostTenTableBusinessPriceMultiplier()
        {
            EnsureTavernDefaults();
            if (GetUnlockedTableCount() < PostTenTablePriceGrowthTableCount)
            {
                return 1f;
            }

            var openCount = Mathf.Max(0, SaveData.gameplay.postTenTableBusinessOpenCount);
            return 1f + PostTenTablePriceGrowthPerOpen * openCount;
        }

        /// <summary>
        /// 掌柜等级：默认 1 级，生涯累计营收每满 10000 铜钱升 1 级。
        /// </summary>
        public int GetShopkeeperLevel()
        {
            EnsureTavernDefaults();
            return 1 + Mathf.Max(0, SaveData.tavern.totalIncome) / ShopkeeperLevelIncomePerLevel;
        }

        public const int ShopkeeperLevelIncomePerLevel = 10000;

        /// <summary>
        /// 本账号是否已触发过指定下标的低谷期。
        /// </summary>
        public bool HasTriggeredValleyWave(int valleyIndex)
        {
            EnsureGameplayDefaults();
            var list = SaveData.gameplay.triggeredValleyWaveIndices;
            return list != null && list.Contains(valleyIndex);
        }

        /// <summary>
        /// 记录本账号已触发指定下标的低谷期（终身不重复）。
        /// </summary>
        public void MarkValleyWaveTriggered(int valleyIndex)
        {
            EnsureGameplayDefaults();
            var list = SaveData.gameplay.triggeredValleyWaveIndices;
            if (list == null)
            {
                list = new List<int>();
                SaveData.gameplay.triggeredValleyWaveIndices = list;
            }

            if (list.Contains(valleyIndex))
            {
                return;
            }

            list.Add(valleyIndex);
            SaveGame();
        }

        /// <summary>当前选用菜单：默认大众菜单。</summary>
        public TavernMenuType GetTavernMenuType()
        {
            EnsureTavernDefaults();
            return SaveData.tavern.tavernMenuType == (int)TavernMenuType.Vip
                ? TavernMenuType.Vip
                : TavernMenuType.Popular;
        }

        /// <summary>是否选用贵客菜单。</summary>
        public bool IsVipMenuSelected()
        {
            return GetTavernMenuType() == TavernMenuType.Vip;
        }

        /// <summary>二星及以上自家酒楼，且首次上二楼解锁后才显示菜单入口。</summary>
        public bool ShouldShowTavernMenuEntry()
        {
            MigrateMenuEntryUnlockIfNeeded();
            return !IsVisitingOtherTavern
                   && GetTavernLevel() >= 2
                   && IsTavernMenuEntryUnlocked();
        }

        /// <summary>底部菜单是否已在首次上二楼后解锁。</summary>
        public bool IsTavernMenuEntryUnlocked()
        {
            EnsureGameplayDefaults();
            MigrateMenuEntryUnlockIfNeeded();
            return GameplayGuideData != null && GameplayGuideData.menuEntryUnlocked;
        }

        /// <summary>首次上二楼对话结束后解锁菜单入口并刷新 HUD。</summary>
        public void UnlockTavernMenuEntry()
        {
            EnsureGameplayDefaults();
            if (GameplayGuideData == null)
            {
                return;
            }

            var changed = false;
            if (!GameplayGuideData.menuEntryUnlocked)
            {
                GameplayGuideData.menuEntryUnlocked = true;
                changed = true;
            }

            if (!GameplayGuideData.dialogUnlockMenuShown)
            {
                GameplayGuideData.dialogUnlockMenuShown = true;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            SaveGame();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
            TavernTopStatusPanelController.RefreshOpenedMenuStatusUi();
            UIKit.GetPanel<TavernBottomNavPanelController>()?.RefreshPanel();
        }

        /// <summary>
        /// 旧档兼容：已有二楼建造进度视为已解锁菜单，避免重复弹 UnlockMenu。
        /// </summary>
        private void MigrateMenuEntryUnlockIfNeeded()
        {
            EnsureGameplayDefaults();
            if (GameplayGuideData == null || GameplayGuideData.menuEntryUnlockMigrated)
            {
                return;
            }

            if (!GameplayGuideData.menuEntryUnlocked && IsStairsUnlocked())
            {
                var hasSecondFloorProgress = GameplayGuideData.purchasedSecondFloorTable
                                             || (GameplayGuideData.purchasedSecondFloorDecorations != null
                                                 && GameplayGuideData.purchasedSecondFloorDecorations.Count > 0);
                if (hasSecondFloorProgress)
                {
                    GameplayGuideData.menuEntryUnlocked = true;
                    GameplayGuideData.dialogUnlockMenuShown = true;
                }
            }

            GameplayGuideData.menuEntryUnlockMigrated = true;
            SaveGame();
        }

        /// <summary>
        /// 切换当前菜单并立刻存档；未变化时返回 false。
        /// </summary>
        public bool TrySetTavernMenuType(TavernMenuType menuType)
        {
            EnsureTavernDefaults();
            var normalized = menuType == TavernMenuType.Vip
                ? (int)TavernMenuType.Vip
                : (int)TavernMenuType.Popular;
            if (SaveData.tavern.tavernMenuType == normalized)
            {
                return false;
            }

            SaveData.tavern.tavernMenuType = normalized;
            SaveGame();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
            TavernSceneManager.Instance?.RefreshCustomerSpawnInterval();
            // 二楼没有 TavernSceneManager 时，点单按钮仍要立刻换图。
            TavernSecondFloorVipSessionController.RefreshVisibleOrderBubble();
            TavernTopStatusPanelController.RefreshOpenedMenuStatusUi();
            return true;
        }

        /// <summary>
        /// 菜单切换成功后开启冷却（真实 UTC 时间）。
        /// </summary>
        public void StartMenuSwitchCooldown()
        {
            EnsureTavernDefaults();
            var duration = TbConfigRuntime.GetMenuSwitchCooldownSeconds(30f);
            if (duration <= 0f)
            {
                SaveData.tavern.menuSwitchCooldownEndUnixTime = 0d;
                SaveGame();
                Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
                return;
            }

            SaveData.tavern.menuSwitchCooldownEndUnixTime = GetUtcNowSeconds() + duration;
            SaveGame();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        /// <summary>菜单切换冷却剩余秒数；0 表示可切换。</summary>
        public float GetMenuSwitchCooldownRemainingSeconds()
        {
            EnsureTavernDefaults();
            var end = SaveData.tavern.menuSwitchCooldownEndUnixTime;
            if (end <= 0d)
            {
                return 0f;
            }

            var remaining = end - GetUtcNowSeconds();
            if (remaining <= 0d)
            {
                if (SaveData.tavern.menuSwitchCooldownEndUnixTime != 0d)
                {
                    SaveData.tavern.menuSwitchCooldownEndUnixTime = 0d;
                    SaveGame();
                }

                return 0f;
            }

            return (float)remaining;
        }

        /// <summary>菜单切换冷却是否已结束。</summary>
        public bool IsMenuSwitchCooldownReady()
        {
            return GetMenuSwitchCooldownRemainingSeconds() <= 0f;
        }

        /// <summary>
        /// 大众菜单对常时刷客间隔的倍率（未满二星、拜访他人店或贵客菜单时为 1）。
        /// </summary>
        public float GetActiveTavernMenuCustomerRefreshMul()
        {
            if (IsVisitingOtherTavern || GetTavernLevel() < 2 || IsVipMenuSelected())
            {
                return 1f;
            }

            return TbConfigRuntime.GetPopularMenuCustomerRefreshMul();
        }

        /// <summary>
        /// 贵客菜单对结账基础单价的加成；拜访他人店、未满二星或大众菜单时原样返回。
        /// </summary>
        public int ApplyActiveTavernMenuCheckoutUnitPrice(int unitPrice)
        {
            unitPrice = Mathf.Max(1, unitPrice);
            if (IsVisitingOtherTavern || GetTavernLevel() < 2 || !IsVipMenuSelected())
            {
                return unitPrice;
            }

            return Mathf.Max(1, Mathf.RoundToInt(unitPrice * TbConfigRuntime.GetVipMenuCheckoutUnitPriceMul()));
        }

        /// <summary>
        /// 一键跳到三星酒楼，并解锁一楼桌 1~6、二楼楼梯/贵客桌/戏台/全部装饰；
        /// 补齐厨房引导设施、招满员工与开业进度，保证铜钱与声望门槛，刷新城镇外观。
        /// </summary>
        public bool DebugSkipToThreeStarFullyBuilt(out string message)
        {
            message = string.Empty;
            EnsureInitialized();
            EnsureTavernDefaults();
            EnsureGameplayDefaults();
            EnsureTownBuildingDefaults();

            if (IsVisitingOtherTavern)
            {
                message = "拜访他人酒楼时不可跳关";
                return false;
            }

            if (SaveData?.tavern == null || GameplayGuideData == null || PlayerData == null)
            {
                message = "存档未就绪";
                return false;
            }

            const int targetLevel = 3;
            const int minCoinGrant = 50000;
            const int firstFloorTableCount = 6;

            // 星级 + 墙体扩建 + 声望至少达到升到三星的门槛。
            SaveData.tavern.tavernLevel = Mathf.Max(SaveData.tavern.tavernLevel, targetLevel);
            SaveData.tavern.interiorWallExpanded = true;
            var prestigeForThreeStar = TbConfigRuntime.GetUpgradeLevelPrestige(
                targetLevel,
                ResolveDefaultUpgradePrestige(targetLevel));
            SaveData.tavern.tavernPrestige = Mathf.Max(SaveData.tavern.tavernPrestige, prestigeForThreeStar);
            SaveData.tavern.tableLv2UpgradeUnlocked = true;
            SaveData.tavern.achievementEntryUnlocked = true;
            SaveData.tavern.techEntryUnlocked = true;
            SaveData.tavern.featureEntryUnlockMigrated = true;

            // 一楼桌 1~6 全解锁（不走 UnlockTable，避免多次存档/声望飘字）。
            for (var tableId = 1; tableId <= firstFloorTableCount; tableId++)
            {
                var tableData = GetTableData(tableId);
                if (tableData == null)
                {
                    continue;
                }

                tableData.isUnlocked = true;
                if (tableData.runtimeState == (int)TavernTableRuntimeState.Locked)
                {
                    tableData.runtimeState = (int)TavernTableRuntimeState.Idle;
                }
            }

            tableNum = GetUnlockedTableCount();

            var guide = GameplayGuideData;

            // 一楼厨房/柜台等引导设施 + 二楼全建成。
            guide.purchasedCounter = true;
            SetGuideKitchenItemPurchased(GuideKitchenStove, true);
            SetGuideKitchenItemPurchased(GuideKitchenFurnace, true);
            SetGuideKitchenItemPurchased(GuideKitchenCabinet, true);
            SetGuideKitchenItemPurchased(GuideKitchenCabinet2, true);
            SetGuideKitchenItemPurchased(GuideKitchenWineCabinet, true);
            SetGuideKitchenItemPurchased(GuideKitchenWaterJarPile, true);
            SetGuideKitchenItemPurchased(GuideKitchenTable1, true);
            SetGuideKitchenItemPurchased(GuideKitchenTable2, true);
            SetGuideKitchenItemPurchased(GuideJiaozi, true);
            SetGuideKitchenItemPurchased(GuideStairs, true);
            SetGuideKitchenItemPurchased(GuideSecondFloorTable, true);
            SetGuideKitchenItemPurchased(GuideXitai, true);
            SetSecondFloorDecorationPurchased(GuideDecoration1, true);
            SetSecondFloorDecorationPurchased(GuideDecoration2, true);
            SetSecondFloorDecorationPurchased(GuideDecoration3, true);
            SetSecondFloorDecorationPurchased(GuideDecoration4, true);
            SetSecondFloorDecorationPurchased(GuideDecoration5, true);
            SetSecondFloorDecorationPurchased(GuideDecoration6, true);

            guide.purchasedTableCount = Mathf.Max(guide.purchasedTableCount, firstFloorTableCount);
            guide.purchasedStairs = true;
            guide.purchasedSecondFloorTable = true;
            guide.purchasedXitai = true;
            guide.purchasedJiaozi = true;
            guide.recruitmentUnlocked = true;
            guide.openingUnlocked = true;
            guide.onboardingCompleted = true;
            guide.currentStage = GameplayGuideStage.Running;
            guide.menuEntryUnlocked = true;
            guide.dialogUnlockMenuShown = true;
            guide.menuEntryUnlockMigrated = true;
            guide.dialogFirstEnterShown = true;
            guide.dialogEmployShown = true;
            guide.dialogUpdateShown = true;
            guide.dialogOpeningShown = true;
            guide.dialogHireStaffEnterShown = true;
            guide.recruitmentUnlockToastShown = true;

            // 开业标记（不走 SetTavernOpen，避免重置当日营业统计）。
            SaveData.tavern.isOpen = true;
            SaveData.gameplay.shopOpened = true;
            if (SaveData.gameplay.businessOpenCount <= 0)
            {
                SaveData.gameplay.businessOpenCount = 1;
            }

            // 三星名额下招满掌柜 + 厨师/小二固定槽（免费）。
            DebugHireAllStaffForSkip();

            // 铜钱补足到最低额度。
            var coinNeed = minCoinGrant - PlayerData.coinNum;
            if (coinNeed > 0)
            {
                // ChangeCoinNum 会存档并发信号；此处先直接加，末尾统一 Save/Dispatch。
                PlayerData.coinNum += coinNeed;
                Signals.Get<UpdateCoinNumSignal>().Dispatch(coinNeed);
            }

            TryGrantJiaoziUnlockedByProgress(dispatchSignals: false);
            TrySyncOwnTownExteriorBuildingLevel();
            // 跳过可能残留的搬运落位 pending，避免卡在引导配送。
            ClearGuideBuildPlacementPending();
            SyncGameplayGuideProgress();
            // Sync 可能按雇佣状态改写 stage；强制保持已开业跳关结果。
            guide.currentStage = GameplayGuideStage.Running;
            guide.onboardingCompleted = true;
            guide.openingUnlocked = true;
            guide.purchasedStairs = true;
            guide.purchasedSecondFloorTable = true;
            guide.hiredShopkeeper = true;
            guide.hiredChef = true;
            guide.hiredWaiter = true;
            guide.skipToThreeStarUsed = true;

            UnlockTavernMenuEntry();
            SaveGame();
            Signals.Get<TableNumSignal>().Dispatch();
            Signals.Get<TavernPrestigeChangedSignal>().Dispatch();
            Signals.Get<GameplayGuideProgressSignal>().Dispatch();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
            Signals.Get<TavernBusinessStateSignal>().Dispatch(true);
            TavernTopStatusPanelController.RefreshOpenedMenuStatusUi();
            UIKit.GetPanel<TavernBottomNavPanelController>()?.RefreshPanel();

            message =
                $"已跳至 {SaveData.tavern.tavernLevel} 星：一楼桌 1~{firstFloorTableCount}、二楼全建成、员工招满，铜钱≥{minCoinGrant}";
            return true;
        }

        /// <summary>
        /// 调试跳关：按当前星级招满掌柜与厨师/小二固定槽（免费、跳过不足金币检查路径用 overrideCost=0）。
        /// </summary>
        private void DebugHireAllStaffForSkip()
        {
            EnsureGameplayDefaults();
            if (IsVisitingOtherTavern)
            {
                return;
            }

            if (CountHiredByPosition(StaffPosition.Shopkeeper) <= 0)
            {
                TryHireConfiguredStaff(StaffConfigUtility.DefaultShopkeeperId, out _, out _, overrideCost: 0);
            }

            var slotCap = Mathf.Max(1, GetTavernLevelStaffHireSlotCap());
            HireFixedSlotStaffForSkip(StaffPosition.Chef, slotCap);
            HireFixedSlotStaffForSkip(StaffPosition.Waiter, slotCap);
        }

        private void HireFixedSlotStaffForSkip(StaffPosition position, int maxSlots)
        {
            var slots = StaffConfigUtility.GetFixedHireSlotStaffs(position, maxSlots);
            for (var index = 0; index < slots.Count; index++)
            {
                var staff = slots[index];
                if (staff == null)
                {
                    continue;
                }

                if (StaffConfigUtility.FindOwnedStaffSave(staff.Id, preferNonTemporary: true) is { temporary: false })
                {
                    continue;
                }

                TryHireConfiguredStaff(staff.Id, out _, out _, overrideCost: 0);
            }
        }
    }
}
