using JN.Client.Manager;
using QFramework;

namespace JN.Client.UI
{
    /// <summary>
    /// HUD 面板统一基类。
    /// 同时兜底处理 prefab 误嵌套导致的重复实例化问题。
    /// </summary>
    public abstract class HudPanelController<TData> : QFrameworkPanel<TData> where TData : UIPanelData, new()
    {
        protected override void OnBeforePanelOpen(TData data)
        {
            if (BgmOverlayDuckRegistry.ShouldDuck(GetType()))
            {
                GameAudioManager.DuckBgmForOverlay();
            }
        }

        protected override void OnBeforePanelClose()
        {
            if (BgmOverlayDuckRegistry.ShouldDuck(GetType()))
            {
                GameAudioManager.UnduckBgmForOverlay();
            }
        }

        /// <summary>
        /// 如果当前面板被错误地挂在另一个 UIPanel 下面，则直接停用自身，避免递归开面板。
        /// </summary>
        protected bool DisableIfNestedInsideAnotherPanel()
        {
            var parentPanels = GetComponentsInParent<UIPanel>(true);
            for (var index = 0; index < parentPanels.Length; index++)
            {
                var panel = parentPanels[index];
                if (panel == null || ReferenceEquals(panel, this))
                {
                    continue;
                }

                gameObject.SetActive(false);
                return true;
            }

            return false;
        }
    }
}
