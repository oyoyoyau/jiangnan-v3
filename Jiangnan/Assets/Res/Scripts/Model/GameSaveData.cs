using System;
using System.Collections.Generic;
using JN.Client.Messages;

namespace JN.Client.Model
{
    [Serializable]
    /// <summary>
    /// 负责游戏存档数据相关的运行时逻辑。
    /// </summary>
    public class GameSaveData
    {
        public int version = 1;
        public string lastSceneName = "Town";
        public long lastSavedUtcTicks;
        public PlayerModel player = new();
        public LocalGameplaySaveData gameplay = new();
        public TownSaveData town = new();
        public TavernSaveData tavern = new();
    }

    [Serializable]
    /// <summary>
    /// 负责大地图存档数据相关的运行时逻辑。
    /// </summary>
    public class TownSaveData
    {
        /// <summary>
        /// 是否已生成并持久化其他玩家占位店铺（首次进入 Town 后固定）。
        /// </summary>
        public bool otherPlayerShopsSeeded;

        /// <summary>
        /// 旧档迁移：董政与笛子店铺展示成就已互换。
        /// </summary>
        public bool otherPlayerShopTitleAssignmentsApplied;

        /// <summary>他人酒楼拉客剩余次数（按 tileId 独立）。</summary>
        public List<OtherTavernPullSaveEntry> otherTavernPullEntries = new();

        public List<BuildingInfo> buildingInfos = new();
    }

    [Serializable]
    /// <summary>
    /// 负责酒楼存档数据相关的运行时逻辑。
    /// </summary>
    public class TavernSaveData
    {
        public bool isOpen;
        public int availableDishes;
        public int totalServedCustomers;
        public int totalIncome;
        /// <summary>
        /// 生涯累计做出的菜品数（出菜完成时 +1）。
        /// </summary>
        public int totalCookedDishes;
        public bool tableLv2UpgradeUnlocked;
        /// <summary>成就底栏入口已通过解锁弹窗开放。</summary>
        public bool achievementEntryUnlocked;
        /// <summary>成就入口解锁弹窗已入队/展示中，避免重复弹出。</summary>
        public bool achievementEntryRevealPending;
        /// <summary>科技底栏入口已通过解锁弹窗开放。</summary>
        public bool techEntryUnlocked;
        /// <summary>科技入口解锁弹窗已入队/展示中，避免重复弹出。</summary>
        public bool techEntryRevealPending;
        /// <summary>成就入口刚解锁时显示红点，直到玩家打开成就面板。</summary>
        public bool achievementEntryAttentionPending;
        /// <summary>旧档迁移：避免已对老玩家重复弹出成就/科技解锁提示。</summary>
        public bool featureEntryUnlockMigrated;
        public TavernAchievementSaveData achievementStats = new();
        public List<TavernTableSaveData> tables = new();

        /// <summary>
        /// 酒店星级：0=无星，1~3=对应星数（仅由声望升级提升）。
        /// </summary>
        public int tavernLevel;

        /// <summary>
        /// 兼容旧存档字段；新逻辑请读 <see cref="interiorWallExpanded"/>。
        /// </summary>
        public int tavernExpandLevel = 1;

        /// <summary>
        /// 左侧 wall01 是否已完成付费扩建（三星存档默认 true）。
        /// </summary>
        public bool interiorWallExpanded;

        /// <summary>
        /// 当前累计声望（成就/完成一桌获得）。
        /// </summary>
        public int tavernPrestige;

        /// <summary>
        /// 自家营业中离店时的运行时快照（桌阶段/厨工单/前台点单/刷客计时）；拜访他人店不读写。
        /// </summary>
        public TavernRuntimeSnapshotSaveData runtimeSnapshot;

        /// <summary>
        /// 二楼包厢是否已有贵客（进店气泡据此显示大堂/包厢）。
        /// </summary>
        public bool hasSecondFloorVipGuest;

        /// <summary>二楼贵客是否已坐下（切回二楼可直接落座）。</summary>
        public bool secondFloorVipSeated;

        /// <summary>二楼贵客桌上已上菜道数（含已吃空盘，0~6）。</summary>
        public int secondFloorVipServedDishCount;

        /// <summary>二楼贵客已吃完道数（0~6）。</summary>
        public int secondFloorVipEatenDishCount;

        /// <summary>二楼贵客已结账道数（0~6）。</summary>
        public int secondFloorVipCheckoutDoneCount;

        /// <summary>
        /// 当前选用菜单：0=大众菜单（默认），1=贵客菜单。
        /// </summary>
        public int tavernMenuType;

        /// <summary>
        /// 菜单切换冷却结束 UTC Unix 秒；0 表示无冷却。
        /// </summary>
        public double menuSwitchCooldownEndUnixTime;
    }

    /// <summary>
    /// 酒楼菜单类型。
    /// </summary>
    public enum TavernMenuType
    {
        Popular = 0,
        Vip = 1
    }

    [Serializable]
    /// <summary>
    /// 负责酒楼桌位存档数据相关的运行时逻辑。
    /// </summary>
    public class TavernTableSaveData
    {
        public int tableId;
        public bool isUnlocked;
        public int level = 1;
        public int runtimeState = (int)TavernTableRuntimeState.Locked;
        public int totalServedCustomers;
        public int totalIncome;
    }

    /// <summary>
    /// 定义酒楼桌位运行时状态可用的枚举类型。
    /// </summary>
    public enum TavernTableRuntimeState
    {
        Locked = 0,
        Idle = 1,
        Reserved = 2,
        WaitingServe = 3,
        Dining = 4,
        Checkout = 5,
        Cleaning = 6,
        WaitingOrder = 7
    }
}
