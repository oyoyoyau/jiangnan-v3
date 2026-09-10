using QFramework;

namespace JN.Client.UI
{
    /// <summary>
    /// 负责框架面板相关的运行时逻辑。
    /// </summary>
    public abstract class QFrameworkPanel<TData> : UIPanel where TData : UIPanelData, new()
    {
        protected TData Data { get; private set; } = new();

        /// <summary>
        /// 响应初始化事件并同步状态。
        /// </summary>
        /// <param name="uiData">数据编号。</param>
        protected sealed override void OnInit(IUIData uiData = null)
        {
            // 面板允许在无显式数据时打开，因此这里始终提供一个安全的默认值。
            Data = uiData as TData ?? new TData();
            OnPanelInit();
        }

        /// <summary>
        /// 响应打开事件并同步状态。
        /// </summary>
        /// <param name="uiData">数据编号。</param>
        protected sealed override void OnOpen(IUIData uiData = null)
        {
            Data = uiData as TData ?? new TData();
            OnBeforePanelOpen(Data);
            OnPanelOpen(Data);
        }

        /// <summary>
        /// 响应显示事件并同步状态。
        /// </summary>
        protected override void OnShow()
        {
            OnPanelShow();
        }

        /// <summary>
        /// 响应隐藏事件并同步状态。
        /// </summary>
        protected override void OnHide()
        {
            OnPanelHide();
        }

        /// <summary>
        /// 响应关闭事件并同步状态。
        /// </summary>
        protected override void OnClose()
        {
            OnBeforePanelClose();
            OnPanelClose();
        }

        /// <summary>
        /// 面板初始化时绑定控件和事件。
        /// </summary>
        protected virtual void OnPanelInit()
        {
            
            
        }

        /// <summary>
        /// 面板打开前钩子（如 BGM 降低），在 <see cref="OnPanelOpen"/> 之前调用。
        /// </summary>
        /// <param name="data">数据。</param>
        protected virtual void OnBeforePanelOpen(TData data)
        {
        }

        /// <summary>
        /// 面板打开时读取数据并刷新显示。
        /// </summary>
        /// <param name="data">数据。</param>
        protected virtual void OnPanelOpen(TData data)
        {
        }

        /// <summary>
        /// 响应面板显示事件并同步状态。
        /// </summary>
        protected virtual void OnPanelShow()
        {
        }

        /// <summary>
        /// 响应面板隐藏事件并同步状态。
        /// </summary>
        protected virtual void OnPanelHide()
        {
        }

        /// <summary>
        /// 面板关闭前钩子（如 BGM 恢复），在 <see cref="OnPanelClose"/> 之前调用。
        /// </summary>
        protected virtual void OnBeforePanelClose()
        {
        }

        /// <summary>
        /// 面板关闭时清理临时状态和监听。
        /// </summary>
        protected virtual void OnPanelClose()
        {
        }
    }
}
