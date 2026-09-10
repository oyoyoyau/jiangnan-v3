using QFramework;

namespace JN.Client.UI
{
    /// <summary>
    /// 统一管理一组 HUD 子面板的打开、显示和关闭。
    /// </summary>
    public sealed class HudRootCoordinator
    {
        /// <summary>
        /// 确保指定面板已打开；如果已存在则直接显示。
        /// </summary>
        public T EnsureOpened<T>(UILevel? level = null, IUIData uiData = null, string prefabName = null, bool bringToFront = true) where T : UIPanel
        {
            var resolvedLevel = level ?? JiangNanUIPanelLayerConfig.Resolve<T>();
            var panel = UIKit.GetPanel<T>();
            if (panel != null)
            {
                JiangNanUIPanelLayerConfig.Apply(panel, resolvedLevel, bringToFront);
                UIKit.ShowPanel(panel.name);
                return panel;
            }

            panel = UIKit.OpenPanel<T>(resolvedLevel, uiData, prefabName: prefabName);
            if (panel != null && !bringToFront)
            {
                JiangNanUIPanelLayerConfig.Apply(panel, resolvedLevel, bringToFront: false);
            }

            return panel;
        }

        /// <summary>
        /// 安全关闭指定面板；面板不存在时直接忽略。
        /// </summary>
        public void Close<T>() where T : UIPanel
        {
            if (UIKit.GetPanel<T>() == null)
            {
                return;
            }

            UIKit.ClosePanel<T>();
        }

        /// <summary>
        /// 安全显示指定面板；面板不存在时直接忽略。
        /// </summary>
        public void Show<T>(UILevel? level = null) where T : UIPanel
        {
            var panel = UIKit.GetPanel<T>();
            if (panel == null)
            {
                return;
            }

            JiangNanUIPanelLayerConfig.Apply(panel, level ?? JiangNanUIPanelLayerConfig.Resolve<T>());
            UIKit.ShowPanel(panel.name);
        }

        /// <summary>
        /// 安全隐藏指定面板；面板不存在时直接忽略。
        /// </summary>
        public void Hide<T>() where T : UIPanel
        {
            var panel = UIKit.GetPanel<T>();
            if (panel == null)
            {
                return;
            }

            UIKit.HidePanel(panel.name);
        }
    }
}
