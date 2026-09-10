using System;
using System.Collections.Generic;
using System.IO;
using JN.Client.Config;
using JN.Client.Messages;
using JN.Client.Model;
using Newtonsoft.Json;
using UnityEngine;

namespace JN.Client.Manager
{
    /// <summary>
    /// 负责数据相关的运行时逻辑。
    /// </summary>
    public partial class DataManager
    {
        /// <summary>
        /// 保存游戏。
        /// </summary>
        public void SaveGame()
        {
            if (SaveData == null)
            {
                return;
            }

            RunSaveGameWithVisitUnlockGuard(WriteSaveGameToDisk);
        }

        /// <summary>
        /// 实际写盘；拜访中由 RunSaveGameWithVisitUnlockGuard 先还原桌解锁再调用。
        /// </summary>
        private void WriteSaveGameToDisk()
        {
            SaveData.version = SaveVersion;
            SaveData.lastSavedUtcTicks = DateTime.UtcNow.Ticks;

            var serialized = JsonConvert.SerializeObject(SaveData, Formatting.Indented);
            File.WriteAllText(SavePath, serialized);

            if (HasCreatedPlayer)
            {
                Directory.CreateDirectory(SaveDirectoryPath);
                File.WriteAllText(GetNamedSavePath(PlayerData.playerName), serialized);
            }
        }

        /// <summary>
        /// 加载或创建存档。
        /// </summary>
        private void LoadOrCreateSave()
        {
            // 重开游戏：清空他人酒楼拉客行程快照（静态缓存不落盘）。
            ClearAllOtherTavernVisitSnapshots();

            if (File.Exists(SavePath) && TryLoadSaveFromPath(SavePath, out var localSave))
            {
                SaveData = localSave;
            }

            SaveData ??= CreateDefaultSave();
            SaveData.player ??= new PlayerModel();
            SaveData.gameplay ??= new LocalGameplaySaveData();
            SaveData.town ??= new TownSaveData();
            SaveData.tavern ??= new TavernSaveData();
            PlayerData = SaveData.player;
            PlayerData.playerName = NormalizePlayerName(PlayerData.playerName) ?? string.Empty;

            if (!HasCreatedPlayer)
            {
                SaveData.lastSceneName = string.Empty;
            }

            EnsureTownBuildingDefaults();
            EnsureTavernDefaults();
            EnsureGameplayDefaults();
            EnsureAchievementCompletionToastMigration();
            SanitizeResearchedTechIds();
            StaffConfigUtility.RefreshAllOwnedStaffSkillsFromTech();
            tableNum = GetUnlockedTableCount();
            SaveGame();
        }

        /// <summary>
        /// 尝试从指定路径读取存档。
        /// </summary>
        /// <param name="path">资源路径。</param>
        /// <param name="saveData">数据。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private static bool TryLoadSaveFromPath(string path, out GameSaveData saveData)
        {
            saveData = null;
            try
            {
                saveData = JsonConvert.DeserializeObject<GameSaveData>(File.ReadAllText(path));
                return saveData != null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DataManager] 读取存档失败，已忽略该存档。\n路径: {path}\n{exception}");
                return false;
            }
        }

        /// <summary>
        /// 创建默认存档。
        /// </summary>
        /// <returns>返回方法执行后的结果。</returns>
        private GameSaveData CreateDefaultSave()
        {
            return new GameSaveData
            {
                version = SaveVersion,
                player = new PlayerModel(),
                gameplay = new LocalGameplaySaveData(),
                town = new TownSaveData(),
                tavern = new TavernSaveData()
            };
        }

        /// <summary>
        /// 确保大地图建筑默认s。
        /// </summary>
        private void EnsureTownBuildingDefaults()
        {
            EnsureInitializedCore();
            SaveData.town.buildingInfos ??= new List<BuildingInfo>();
            SaveData.town.buildingInfos.RemoveAll(info => info == null || info.tileId < 1 || info.tileId > TownTileCount);

            for (var tileId = 1; tileId <= TownTileCount; tileId++)
            {
                if (SaveData.town.buildingInfos.Exists(info => info.tileId == tileId))
                {
                    continue;
                }

                SaveData.town.buildingInfos.Add(new BuildingInfo
                {
                    tileId = tileId,
                    name = $"地块 {tileId}",
                    playerId = 0,
                    status = 0,
                    buildingLevel = 0,
                    buildingTime = 0,
                    buildingId = 0
                });
            }

            EnsureOtherPlayerShopsSeeded();
            EnsureOtherPlayerShopTitleAssignments();
        }

        /// <summary>
        /// 确保酒楼默认s。
        /// </summary>
        private void EnsureTavernDefaults()
        {
            EnsureInitializedCore();
            SaveData.tavern.tables ??= new List<TavernTableSaveData>();
            SaveData.tavern.achievementStats ??= new TavernAchievementSaveData();
            SaveData.tavern.achievementStats.unlockedStaffTalentIds ??= new List<int>();
            SaveData.tavern.achievementStats.taskScopedProgress ??= new List<AchievementTaskScopedProgress>();
            if (SaveData.tavern.runtimeSnapshot != null)
            {
                SaveData.tavern.runtimeSnapshot.tables ??= new List<TavernTablePhaseSnapshot>();
                SaveData.tavern.runtimeSnapshot.frontOrders ??= new List<TavernFrontOrderSnapshot>();
            }

            for (var tableId = 1; tableId <= DefaultTableCount; tableId++)
            {
                if (SaveData.tavern.tables.Exists(table => table.tableId == tableId))
                {
                    continue;
                }

                SaveData.tavern.tables.Add(new TavernTableSaveData
                {
                    tableId = tableId,
                    isUnlocked = false,
                    level = 1,
                    runtimeState = (int)TavernTableRuntimeState.Locked
                });
            }

            if (SaveData.tavern.tavernLevel < 0)
            {
                SaveData.tavern.tavernLevel = 0;
            }
            else if (SaveData.tavern.tavernLevel > MaxTavernLevel)
            {
                SaveData.tavern.tavernLevel = MaxTavernLevel;
            }

            if (SaveData.tavern.tavernPrestige < 0)
            {
                SaveData.tavern.tavernPrestige = 0;
            }

            // 三星及以上默认已完成墙体扩建。
            if (SaveData.tavern.tavernLevel >= 3)
            {
                SaveData.tavern.interiorWallExpanded = true;
            }
        }

        /// <summary>
        /// 确保玩法默认s。
        /// </summary>
        private void EnsureGameplayDefaults()
        {
            EnsureInitializedCore();
            SaveData.gameplay ??= new LocalGameplaySaveData();
            SaveData.gameplay.purchasedLandSlots ??= new List<byte>();
            SaveData.gameplay.hiredStaffIds ??= new List<int>();
            SaveData.gameplay.ownedShops ??= new List<LocalShopSaveData>();
            SaveData.gameplay.ownedEquipment ??= new List<LocalEquipmentSaveData>();
            SaveData.gameplay.ownedStaff ??= new List<LocalStaffSaveData>();
            SaveData.gameplay.researchedTechIds ??= new List<int>();
            SaveData.gameplay.claimedAchievementIds ??= new List<int>();
            SaveData.gameplay.achievementCompletionToastShownIds ??= new List<int>();
            SaveData.gameplay.sessionDissatisfactionReasons ??= new List<ClosingDissatisfactionEntry>();
            SaveData.gameplay.recentClosings ??= new List<ClosingSessionRecord>();
            SaveData.gameplay.pendingPulledCustomerKinds ??= new List<int>();
            SaveData.gameplay.pendingPulledCustomerCapacities ??= new List<int>();
            SaveData.gameplay.triggeredValleyWaveIndices ??= new List<int>();
            // 旧档兼容：已开过业但未记次数时，至少记为 1 次（解锁员工入口）
            if (SaveData.gameplay.businessOpenCount <= 0
                && SaveData.gameplay.gameplayGuide != null
                && SaveData.gameplay.gameplayGuide.onboardingCompleted)
            {
                SaveData.gameplay.businessOpenCount = 1;
            }
            for (var index = 0; index < SaveData.gameplay.ownedStaff.Count; index++)
            {
                StaffConfigUtility.EnsureSaveSkillsInitialized(SaveData.gameplay.ownedStaff[index]);
            }

            SaveData.gameplay.gameplayGuide ??= new GameplayGuideSaveData();

            if (SaveData.gameplay.unlockedFeatures == null || SaveData.gameplay.unlockedFeatures.Length == 0)
            {
                SaveData.gameplay.unlockedFeatures = new[] { false, false, false, false, false };
            }

            var ownedBuilding = GetOwnedTownBuilding(ResolveCurrentPlayerId());
            SaveData.gameplay.ownedShops.RemoveAll(shop => shop == null);
            if (ownedBuilding == null || ownedBuilding.status != 2 || ownedBuilding.buildingLevel <= 0)
            {
                SaveData.gameplay.ownedShops.Clear();
                SaveData.gameplay.activeShopId = 0;
            }
            else
            {
                SaveData.gameplay.activeShopId = (byte)Mathf.Clamp(ownedBuilding.tileId, 0, byte.MaxValue);
                SaveData.gameplay.ownedShops.Clear();
                SaveData.gameplay.ownedShops.Add(new LocalShopSaveData
                {
                    mapSlotIndex = (byte)Mathf.Clamp(ownedBuilding.tileId, 0, byte.MaxValue),
                    shopTypeId = 1,
                    shopLevel = (byte)Mathf.Clamp(ownedBuilding.buildingLevel, 1, byte.MaxValue)
                });
            }

            SaveData.gameplay.shopOpened = SaveData.tavern.isOpen;
            SaveData.gameplay.loanCount = Mathf.Clamp(SaveData.gameplay.loanCount, 0, MaxLoanCount);
            SaveData.gameplay.openingLoanClaimed = SaveData.gameplay.openingLoanClaimed || SaveData.gameplay.loanCount > 0;
            SyncGameplayGuideProgress();

            // 已走过开场的存档补发默认掌柜（放在 Sync 后，避免与 Ensure 递归）。
            if (!isGrantingStarterShopkeeper
                && (SaveData.gameplay.openingLoanClaimed || SaveData.gameplay.businessOpenCount > 0))
            {
                EnsureStarterShopkeeperOwned();
            }
        }

        private bool isGrantingStarterShopkeeper;

        /// <summary>
        /// 处理复制建筑信息相关逻辑。
        /// </summary>
        /// <param name="source">来源对象。</param>
        /// <returns>返回方法执行后的结果。</returns>
        private static BuildingInfo CloneBuildingInfo(BuildingInfo source)
        {
            return new BuildingInfo
            {
                playerId = source.playerId,
                name = source.name,
                tileId = source.tileId,
                buildingId = source.buildingId,
                buildingLevel = source.buildingLevel,
                buildingTime = source.buildingTime,
                status = source.status,
                value = source.value,
                celebrationTime = source.celebrationTime,
                displayedAchievementId = source.displayedAchievementId
            };
        }
    }
}
