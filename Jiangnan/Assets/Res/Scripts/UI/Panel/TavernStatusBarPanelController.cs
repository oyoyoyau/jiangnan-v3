using JN.Client.Manager;
using QFramework;
using UnityEngine;

namespace JN.Client.UI
{
    /// <summary>
    /// Tavern HUD Root 面板数据。
    /// </summary>
    public class TavernStatusBarPanelControllerData : UIPanelData
    {
    }

    /// <summary>
    /// Tavern HUD Root，只负责子 panel 生命周期、信号分发与整体协调。
    /// </summary>
    /// <summary>
    /// Tavern HUD Root。
    /// 只负责子面板生命周期、信号分发和整体协调。
    /// </summary>
    public class TavernStatusBarPanelController : HudPanelController<TavernStatusBarPanelControllerData>
    {
        private readonly HudRootCoordinator hudRootCoordinator = new();

        /// <summary>
        /// 组装传给 Tavern 子面板的共享数据。
        /// </summary>
        private TavernHudPanelData BuildHudData()
        {
            return new TavernHudPanelData
            {
                HudRoot = transform,
                RootController = this
            };
        }

        /// <summary>
        /// 初始化时打开 Tavern HUD 子面板。
        /// </summary>
        protected override void OnPanelInit()
        {
            if (DisableIfNestedInsideAnotherPanel())
            {
                return;
            }

            OpenChildPanels();
        }

        /// <summary>
        /// 面板显示时注册信号并刷新各子面板。
        /// </summary>
        protected override void OnPanelShow()
        {
            if (DisableIfNestedInsideAnotherPanel())
            {
                return;
            }

            Signals.Get<UpdateCoinNumSignal>().AddListener(HandleCoinChanged);
            Signals.Get<TavernRuntimeChangedSignal>().AddListener(HandleRuntimeChanged);
            Signals.Get<TavernBusinessStateSignal>().AddListener(HandleBusinessStateChanged);
            Signals.Get<GameplayGuideProgressSignal>().AddListener(HandleGuideChanged);
            Signals.Get<TableNumSignal>().AddListener(HandleGuideChanged);

            UIKit.ClosePanel<StartOpeningWindowController>();
            OpenChildPanels();
            RefreshAllPanels();
        }

        /// <summary>
        /// 面板关闭时移除监听并关闭子面板。
        /// </summary>
        protected override void OnPanelClose()
        {
            if (DisableIfNestedInsideAnotherPanel())
            {
                return;
            }

            Signals.Get<UpdateCoinNumSignal>().RemoveListener(HandleCoinChanged);
            Signals.Get<TavernRuntimeChangedSignal>().RemoveListener(HandleRuntimeChanged);
            Signals.Get<TavernBusinessStateSignal>().RemoveListener(HandleBusinessStateChanged);
            Signals.Get<GameplayGuideProgressSignal>().RemoveListener(HandleGuideChanged);
            Signals.Get<TableNumSignal>().RemoveListener(HandleGuideChanged);

            CloseChildPanels();
            UIKit.ClosePanel<StartOpeningWindowController>();
        }

        /// <summary>
        /// 刷新所有 Tavern 子面板。
        /// </summary>
        public void RefreshAllPanels()
        {
            SyncBusinessHudVisibility();
            EnsureInteractiveHudPanelsOnTop();
            UIKit.GetPanel<TavernTopStatusPanelController>()?.RefreshPanel();
            UIKit.GetPanel<TavernGuidePanelController>()?.RefreshPanel();
            UIKit.GetPanel<TavernBottomNavPanelController>()?.RefreshPanel();
            var businessFlowPanel = UIKit.GetPanel<TavernBusinessFlowPanelController>();
            if (businessFlowPanel == null
                || (!businessFlowPanel.IsSettlementPresentationPlaying
                    && !businessFlowPanel.IsWaitingSettlementConfirm))
            {
                businessFlowPanel?.RefreshPanel();
            }
        }

        /// <summary>
        /// 打开 Tavern 所有固定 HUD 和世界 HUD 子面板。
        /// </summary>
        private void OpenChildPanels()
        {
            var hudData = BuildHudData();
            hudRootCoordinator.EnsureOpened<TavernTopStatusPanelController>(uiData: hudData);
            hudRootCoordinator.EnsureOpened<TavernGuidePanelController>(uiData: hudData);
            // TavernTempEmployPanelController 暂不打开（临时招募入口停用，脚本与预制体保留）。
            hudRootCoordinator.EnsureOpened<TavernBottomNavPanelController>(uiData: hudData);
            hudRootCoordinator.EnsureOpened<TavernBusinessFlowPanelController>(uiData: hudData);
            hudRootCoordinator.EnsureOpened<TavernWorldRuntimeHudPanelController>(bringToFront: false);
            EnsureInteractiveHudPanelsOnTop();
        }

        /// <summary>
        /// 顶栏（含营业倒计时）必须置于 Common HUD 最上层，避免被引导/营业面板挡住点击。
        /// </summary>
        private static void EnsureInteractiveHudPanelsOnTop()
        {
            BringPanelToFront<TavernGuidePanelController>();
            BringPanelToFront<TavernBottomNavPanelController>();
            // 营业/结算面板置于底栏之上；无交互时由 CanvasGroup 穿透，不挡员工/科技/成就入口
            BringPanelToFront<TavernBusinessFlowPanelController>();
            BringPanelToFront<TavernTopStatusPanelController>();
        }

        private static void BringPanelToFront<TPanel>() where TPanel : UIPanel
        {
            var panel = UIKit.GetPanel<TPanel>();
            if (panel != null)
            {
                panel.transform.SetAsLastSibling();
            }
        }

        /// <summary>
        /// 关闭 Tavern 所有固定 HUD 和世界 HUD 子面板。
        /// </summary>
        private void CloseChildPanels()
        {
            hudRootCoordinator.Close<TavernTopStatusPanelController>();
            hudRootCoordinator.Close<TavernGuidePanelController>();
            hudRootCoordinator.Close<TavernTempEmployPanelController>();
            hudRootCoordinator.Close<TavernBottomNavPanelController>();
            hudRootCoordinator.Close<TavernBusinessFlowPanelController>();
            hudRootCoordinator.Close<TavernWorldRuntimeHudPanelController>();
        }

        /// <summary>
        /// 金币变化时按需刷新受影响的子面板。
        /// </summary>
        private void HandleCoinChanged(int changeNum)
        {
            UIKit.GetPanel<TavernTopStatusPanelController>()?.HandleCoinChanged(changeNum);
        }

        /// <summary>
        /// 运行时状态变化时刷新全部 HUD。
        /// </summary>
        private void HandleRuntimeChanged()
        {
            RefreshAllPanels();
        }

        /// <summary>
        /// 引导阶段变化时刷新全部 HUD。
        /// </summary>
        private void HandleGuideChanged()
        {
            RefreshAllPanels();
        }

        /// <summary>
        /// 营业开关变化时只刷新相关子面板。
        /// </summary>
        private void HandleBusinessStateChanged(bool isOpen)
        {
            SyncBusinessHudVisibility();
            UIKit.GetPanel<TavernTopStatusPanelController>()?.HandleBusinessStateChanged(isOpen);
            UIKit.GetPanel<TavernBusinessFlowPanelController>()?.HandleBusinessStateChanged(isOpen);
            UIKit.GetPanel<TavernGuidePanelController>()?.RefreshPanel();
            UIKit.GetPanel<TavernBottomNavPanelController>()?.RefreshPanel();
        }

        /// <summary>
        /// 按营业状态刷新底部导航（开业后仍显示店内底栏，仅回城按钮随营业状态切换）。
        /// </summary>
        private void SyncBusinessHudVisibility()
        {
            hudRootCoordinator.Show<TavernBottomNavPanelController>();
        }
    }
}
