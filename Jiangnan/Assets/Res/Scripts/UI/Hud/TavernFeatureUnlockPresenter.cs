using JN.Client.Manager;

namespace JN.Client.UI
{
    /// <summary>
    /// 协调成就/科技等底栏入口解锁：满足条件后直接解锁，不再弹功能解锁提示面板。
    /// </summary>
    public static class TavernFeatureUnlockPresenter
    {
        public static void TryRevealAchievementEntry()
        {
            DataManager.Instance?.TryUnlockAchievementEntryDirectly();
        }

        public static void TryRevealTechEntry()
        {
            DataManager.Instance?.TryUnlockTechEntryDirectly();
        }
    }
}
