using UnityEngine;

namespace JN.Client.UI
{
    /// <summary>
    /// 通用世界锚点 HUD 容器基类。
    /// 负责管理 Content 根节点，以及世界坐标到 UI 坐标的换算。
    /// </summary>
    public abstract class WorldAnchorHudPanelController<TData, TItemView> : HudPanelController<TData>
        where TData : QFramework.UIPanelData, new()
        where TItemView : MonoBehaviour
    {
        protected RectTransform RootRectTransform;
        protected RectTransform ContentRoot;
        protected Canvas RootCanvas;
        protected Camera SceneCamera;
        protected bool IsItemsVisible = true;

        /// <summary>
        /// 初始化画布和内容根节点。
        /// </summary>
        protected override void OnPanelInit()
        {
            RootRectTransform = transform as RectTransform;
            RootCanvas = GetComponentInParent<Canvas>();

            for (var index = 0; index < transform.childCount; index++)
            {
                var child = transform.GetChild(index);
                child.gameObject.SetActive(child.name == "Content");
            }

            EnsureContentRoot();
        }

        /// <summary>
        /// 面板显示时同步一次外部显隐状态。
        /// </summary>
        protected override void OnPanelShow()
        {
            ApplyExternalVisibilityState();
        }

        /// <summary>
        /// 确保存在用于挂载世界跟随条目的 Content 节点。
        /// </summary>
        protected void EnsureContentRoot()
        {
            if (ContentRoot != null)
            {
                return;
            }

            ContentRoot = transform.Find("Content") as RectTransform;
            if (ContentRoot == null)
            {
                ContentRoot = RootRectTransform;
                Debug.LogWarning($"[{GetType().Name}] 缺少静态 Content 节点，已回退使用面板根节点。");
            }

            ContentRoot.anchorMin = Vector2.zero;
            ContentRoot.anchorMax = Vector2.one;
            ContentRoot.offsetMin = Vector2.zero;
            ContentRoot.offsetMax = Vector2.zero;
            ContentRoot.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>
        /// 统一切换所有世界锚点条目的显隐。
        /// </summary>
        protected void SetSceneItemsVisibleInternal(bool isVisible)
        {
            EnsureContentRoot();
            IsItemsVisible = isVisible;
            if (ContentRoot != null)
            {
                ContentRoot.gameObject.SetActive(isVisible);
            }
        }

        /// <summary>
        /// 将单个世界锚点条目刷新到正确的屏幕位置。
        /// </summary>
        protected void RefreshAnchoredItem<TView>(TView item, Vector3 worldAnchorPosition, System.Action<TView, Vector2> setPosition, System.Action<TView, bool> setVisible)
        {
            if (item == null || SceneCamera == null || RootRectTransform == null)
            {
                return;
            }

            var screenPoint = SceneCamera.WorldToScreenPoint(worldAnchorPosition);
            if (screenPoint.z <= 0f)
            {
                setVisible?.Invoke(item, false);
                return;
            }

            var uiCamera = RootCanvas != null && RootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? RootCanvas.worldCamera
                : null;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(RootRectTransform, screenPoint, uiCamera, out var localPoint))
            {
                setPosition?.Invoke(item, localPoint);
                setVisible?.Invoke(item, true);
            }
            else
            {
                setVisible?.Invoke(item, false);
            }
        }

        /// <summary>
        /// 由子类决定如何响应外部显隐状态。
        /// </summary>
        protected abstract void ApplyExternalVisibilityState();
    }
}
