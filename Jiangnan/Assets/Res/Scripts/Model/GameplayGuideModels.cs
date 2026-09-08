using System;
using System.Collections.Generic;

namespace JN.Client.Model
{
    /// <summary>
    /// 定义玩法引导阶段枚举。
    /// </summary>
    public enum GameplayGuideStage
    {
        Build = 0,
        Recruit = 1,
        ReadyToOpen = 2,
        Running = 3
    }

    /// <summary>
    /// 定义玩法引导任务Id可用的枚举类型。
    /// </summary>
    public enum GameplayGuideTaskId
    {
        BuyCounter = 0,
        BuyTables = 1,
        BuyStove = 2,
        HireShopkeeper = 3,
        HireChef = 4,
        HireWaiter = 5,
        BuyCabinet = 6,
        BuyWineCabinet = 7,
        BuyKitchenTable1 = 8,
        BuyKitchenTable2 = 9,
        BuyFurnace = 10
    }

    /// <summary>
    /// 负责玩法引导存档数据相关的运行时逻辑。
    /// </summary>
    [Serializable]
    public class GameplayGuideSaveData
    {
        public GameplayGuideStage currentStage;
        public bool purchasedCounter;
        public int purchasedTableCount;
        public bool purchasedStove;
        public bool purchasedFurnace;
        public bool purchasedWineCabinet;
        public bool purchasedCabinet;
        public bool purchasedCabinet2;
        public bool purchasedWaterJarPile;
        public bool purchasedJiaozi;
        public bool purchasedStairs;
        /// <summary>二楼包厢厨房桌子（Shop_Interior/桌子，随二楼解锁默认建成）。</summary>
        public bool purchasedSecondFloorTable;
        /// <summary>二楼戏台（Shop_Interior/xitai）。</summary>
        public bool purchasedXitai;
        /// <summary>二楼装饰（Decoration_1~6，guideKey 列表）。</summary>
        public List<string> purchasedSecondFloorDecorations = new();
        public bool purchasedKitchenTable1;
        public bool purchasedKitchenTable2;
        public bool recruitmentUnlocked;
        public bool recruitmentUnlockToastShown;
        public bool hiredShopkeeper;
        public bool hiredChef;
        public bool hiredWaiter;
        public bool openingUnlocked;
        public bool onboardingCompleted;
        /// <summary>first_enter 对话是否已播过（一次性）。</summary>
        public bool dialogFirstEnterShown;
        /// <summary>employ 对话是否已播过（一次性）。</summary>
        public bool dialogEmployShown;
        /// <summary>update 对话是否已播过（一次性）。</summary>
        public bool dialogUpdateShown;
        /// <summary>opening 对话是否已播过（一次性）。</summary>
        public bool dialogOpeningShown;
        /// <summary>HireStaff_enter 对话是否已播过（一次性）。</summary>
        public bool dialogHireStaffEnterShown;
        /// <summary>首次上二楼后解锁底部菜单入口。</summary>
        public bool menuEntryUnlocked;
        /// <summary>UnlockMenu 对话是否已播过（一次性）。</summary>
        public bool dialogUnlockMenuShown;
        /// <summary>菜单解锁旧档迁移是否已完成。</summary>
        public bool menuEntryUnlockMigrated;
        /// <summary>是否已使用「跳去三星酒楼」按钮（用过后不再显示）。</summary>
        public bool skipToThreeStarUsed;
    }

    /// <summary>
    /// 负责玩法引导任务进度相关的运行时逻辑。
    /// </summary>
    public sealed class GameplayGuideTaskProgress
    {
        public GameplayGuideTaskProgress(GameplayGuideTaskId taskId, string title, int current, int target)
        {
            TaskId = taskId;
            Title = title;
            Current = current;
            Target = target;
        }

        public GameplayGuideTaskId TaskId { get; }
        public string Title { get; }
        public int Current { get; }
        public int Target { get; }
        public bool IsCompleted => Current >= Target;
    }

    /// <summary>
    /// 负责玩法引导快照相关的运行时逻辑。
    /// </summary>
    public sealed class GameplayGuideSnapshot
    {
        public GameplayGuideStage Stage { get; set; }
        public bool RecruitmentUnlocked { get; set; }
        public bool CanOpenBusiness { get; set; }
        public bool OnboardingCompleted { get; set; }
        public List<GameplayGuideTaskProgress> ActiveTasks { get; set; } = new();
    }
}
