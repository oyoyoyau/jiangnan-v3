using System;
using System.Collections.Generic;

namespace JN.Client.UI
{
    /// <summary>
    /// 打开指定 PopUI 时降低 BGM，关闭后恢复。集中登记，避免各面板重复实现。
    /// </summary>
    internal static class BgmOverlayDuckRegistry
    {
        private static readonly HashSet<Type> DuckPanelTypes = new()
        {
            typeof(StaffHireSelectPanelController),
            typeof(RecruitConfirmPanelController),
            typeof(StaffInfoPanelController),
            typeof(AchievementCatalogPanelController),
            typeof(TavernTechTreePanelController),
            typeof(TableUpgradePanelController),
        };

        public static bool ShouldDuck(Type panelType)
        {
            return panelType != null && DuckPanelTypes.Contains(panelType);
        }
    }
}
