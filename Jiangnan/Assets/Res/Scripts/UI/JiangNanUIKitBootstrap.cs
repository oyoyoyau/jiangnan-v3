using System;
using System.Collections.Generic;
using AkiFramework.UI;
using QFramework;
using UnityEngine;

namespace JN.Client.UI
{
    /// <summary>
    /// 负责江南界面启动器相关的运行时逻辑。
    /// </summary>
    public static class JiangNanUIKitBootstrap
    {
        private static bool initialized;

        /// <summary>
        /// 在场景加载前自动初始化模块。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitializeBeforeSceneLoad()
        {
            Initialize();
        }

        /// <summary>
        /// 注入运行时依赖并刷新初始显示。
        /// </summary>
        public static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            // 这里集中定义 界面 分辨率与面板地址解析规则，避免散落在业务代码里。
            AddressablesUIKit.Initialize(new AddressablesUIKitConfig
            {
                ReferenceWidth = 1080,
                ReferenceHeight = 1920,
                MatchWidthOrHeight = 0.5f,
                UseScreenSpaceOverlay = true,
                AddressResolver = panelSearchKeys => !string.IsNullOrWhiteSpace(panelSearchKeys.GameObjName)
                    ? panelSearchKeys.GameObjName
                    : panelSearchKeys.PanelType.Name
            });

            initialized = true;
        }
    }

    /// <summary>
    /// 集中维护项目内各类 Panel 的默认层级，避免层级规则分散在业务调用处。
    /// </summary>
    public static class JiangNanUIPanelLayerConfig
    {
        private static readonly Dictionary<Type, UILevel> PanelLevels = new()
        {
            { typeof(LoginPanelController), UILevel.Common },
            { typeof(CreatePlayerPanelController), UILevel.Common },
            { typeof(TownStatusBarPanelController), UILevel.Common },
            { typeof(TownTopStatusPanelController), UILevel.Common },
            { typeof(TownBottomNavPanelController), UILevel.Common },
            { typeof(TavernStatusBarPanelController), UILevel.Common },
            { typeof(TavernTopStatusPanelController), UILevel.Common },
            { typeof(TavernGuidePanelController), UILevel.Common },
            { typeof(TavernBusinessBoostPanelController), UILevel.Common },
            { typeof(TavernTempEmployPanelController), UILevel.Common },
            { typeof(TavernBottomNavPanelController), UILevel.Common },
            { typeof(TavernBusinessFlowPanelController), UILevel.Common },
            { typeof(TavernWorldRuntimeHudPanelController), UILevel.Common },
            { typeof(BuildingItemSceneController), UILevel.Common },
            { typeof(RuntimeInfoPanelController), UILevel.PopUI },
            { typeof(FloatingWarningPanelController), UILevel.PopUI },
            { typeof(PeakTimeWarningPanelController), UILevel.PopUI },
            { typeof(SwitchMenuTipsPanelController), UILevel.PopUI },
            { typeof(RecruitListPanelController), UILevel.PopUI },
            { typeof(RecruitConfirmPanelController), UILevel.PopUI },
            { typeof(StaffHireSelectPanelController), UILevel.PopUI },
            { typeof(VipGuestDishGuessPanelController), UILevel.PopUI },
            { typeof(StaffInfoPanelController), UILevel.PopUI },
            { typeof(TavernTechTreePanelController), UILevel.PopUI },
            { typeof(AchievementCatalogPanelController), UILevel.PopUI },
            { typeof(GetAchievementPanelController), UILevel.PopUI },
            { typeof(TableUpgradePanelController), UILevel.PopUI },
            { typeof(UpgradeTavernPanelController), UILevel.PopUI },
            { typeof(UpgradeTavernPopPanelController), UILevel.PopUI },
            { typeof(NewFunctionUnlockPanelController), UILevel.PopUI },
            { typeof(MenuSwitchPanelController), UILevel.PopUI },
            { typeof(DialogPanelController), UILevel.PopUI },
            { typeof(NewFeatureOpenToastPanelController), UILevel.PopUI },
            { typeof(NewFeatureOpenTableLv2PanelController), UILevel.PopUI },
            { typeof(SuccessPanelController), UILevel.PopUI },
            { typeof(LoanWindowController), UILevel.PopUI },
            { typeof(NewBuildingWindowController), UILevel.PopUI },
            { typeof(StartOpeningWindowController), UILevel.PopUI },
            { typeof(VideoWindowController), UILevel.PopUI }
        };

        public static UILevel Resolve<T>(UILevel fallback = UILevel.Common) where T : UIPanel
        {
            return Resolve(typeof(T), fallback);
        }

        public static UILevel Resolve(Type panelType, UILevel fallback = UILevel.Common)
        {
            if (panelType == null)
            {
                return fallback;
            }

            return PanelLevels.TryGetValue(panelType, out var level) ? level : fallback;
        }

        /// <summary>
        /// 在显示前确保 panel 位于期望层级，并置顶到当前层最后一个兄弟节点。
        /// </summary>
        public static void Apply(UIPanel panel, UILevel? level = null, bool bringToFront = true)
        {
            if (panel == null)
            {
                return;
            }

            var resolvedLevel = level ?? Resolve(panel.GetType(), panel.Info?.Level ?? UILevel.Common);
            var expectedParent = ResolveExpectedParent(panel, resolvedLevel);
            var needsReparent = panel.Info == null
                || panel.Info.Level != resolvedLevel
                || panel.transform.parent != expectedParent;

            if (needsReparent)
            {
                UIKit.Root.SetLevelOfPanel(resolvedLevel, panel);
            }

            if (bringToFront)
            {
                panel.transform.SetAsLastSibling();
            }
        }

        private static Transform ResolveExpectedParent(UIPanel panel, UILevel level)
        {
            if (panel == null)
            {
                return null;
            }

            if (panel.GetComponent<Canvas>() != null)
            {
                return UIKit.Root.CanvasPanel;
            }

            return level switch
            {
                UILevel.Bg => UIKit.Root.Bg,
                UILevel.PopUI => UIKit.Root.PopUI,
                _ => UIKit.Root.Common
            };
        }
    }
}
