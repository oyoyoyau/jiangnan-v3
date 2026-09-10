using System;
using DG.Tweening;
using UnityEngine;

namespace JN.Client.UI
{
    /// <summary>
    /// 世界锚点跟随的 NewOrderBtn 容器，供出餐台等处复用统一点单按钮 prefab。
    /// </summary>
    public class WorldFollowOrderButtonView : MonoBehaviour
    {
        private const float BreathingPulseScaleFactor = 1.08f;
        private const float BreathingPulseSeconds = 0.4f;

        private Transform target;
        private Vector3 worldOffset;
        private RectTransform cachedRectTransform;
        private CanvasGroup cachedCanvasGroup;
        private TableOrderButtonUI orderButton;
        private Action serveClickHandler;
        private bool isVisible = true;
        private float wrapperScale = 1f;
        private Tween breathingTween;

        /// <summary>
        /// 绑定跟随目标与世界偏移。
        /// </summary>
        public void BindTarget(Transform followTarget, Vector3 offset)
        {
            target = followTarget;
            worldOffset = offset;
        }

        /// <summary>
        /// 挂载 NewOrderBtn 实例并完成布局初始化。
        /// </summary>
        public void Initialize(TableOrderButtonUI button, float wrapperScale = 1f)
        {
            orderButton = button;
            this.wrapperScale = wrapperScale;
            EnsureComponents();
            NormalizeChildLayout();
            cachedRectTransform.localScale = Vector3.one * wrapperScale;
        }

        /// <summary>
        /// 配置为可点击的上菜按钮。
        /// </summary>
        public void ConfigureServe(Sprite icon, Action onClick)
        {
            serveClickHandler = onClick;
            orderButton?.ShowWorldServeAction(icon, HandleServeClick);
        }

        /// <summary>
        /// 刷新上菜图标，保留当前点击回调。
        /// </summary>
        public void RefreshServeIcon(Sprite icon)
        {
            RefreshServeVisual(icon, null);
        }

        /// <summary>
        /// 只换图标和菜名文案，不重置点击与发光。
        /// </summary>
        public void RefreshServeVisual(Sprite icon, string dishCaption)
        {
            if (orderButton == null)
            {
                return;
            }

            orderButton.ApplyMenuOrderVisual(icon, dishCaption);
        }

        /// <summary>贵客菜单点单：整颗跟随节点呼吸缩放，而非子按钮局部发光。</summary>
        public void SetBreathingEnabled(bool enabled)
        {
            orderButton?.SetBreathingEnabled(false);

            if (enabled)
            {
                if (breathingTween != null && breathingTween.IsActive())
                {
                    return;
                }

                EnsureComponents();
                var baseScale = Vector3.one * wrapperScale;
                cachedRectTransform.localScale = baseScale;
                breathingTween = cachedRectTransform
                    .DOScale(baseScale * BreathingPulseScaleFactor, BreathingPulseSeconds)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo);
                return;
            }

            StopBreathing();
        }

        /// <summary>
        /// 计算当前条目的世界锚点位置。
        /// </summary>
        public Vector3 GetWorldAnchorPosition()
        {
            return target != null ? target.position + worldOffset : Vector3.zero;
        }

        /// <summary>
        /// 设置条目的本地 UI 坐标。
        /// </summary>
        public void SetAnchoredPosition(Vector2 position)
        {
            if (cachedRectTransform != null)
            {
                cachedRectTransform.anchoredPosition = position;
            }
        }

        /// <summary>
        /// 切换条目显隐。
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (isVisible == visible)
            {
                return;
            }

            isVisible = visible;

            if (cachedCanvasGroup != null)
            {
                cachedCanvasGroup.alpha = visible ? 1f : 0f;
                cachedCanvasGroup.blocksRaycasts = visible;
                cachedCanvasGroup.interactable = visible;
            }
        }

        private void EnsureComponents()
        {
            cachedRectTransform ??= transform as RectTransform;
            if (cachedRectTransform == null)
            {
                cachedRectTransform = gameObject.AddComponent<RectTransform>();
            }

            cachedCanvasGroup ??= GetComponent<CanvasGroup>();
            if (cachedCanvasGroup == null)
            {
                cachedCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            cachedRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            cachedRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            cachedRectTransform.pivot = new Vector2(0.5f, 0.5f);
            cachedRectTransform.anchoredPosition = Vector2.zero;
            cachedRectTransform.localRotation = Quaternion.identity;
            cachedRectTransform.localScale = Vector3.one;
        }

        private void NormalizeChildLayout()
        {
            if (orderButton == null)
            {
                return;
            }

            var buttonRect = orderButton.transform as RectTransform;
            if (buttonRect == null)
            {
                return;
            }

            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = Vector2.zero;
            buttonRect.localRotation = Quaternion.identity;
        }

        private void HandleServeClick()
        {
            serveClickHandler?.Invoke();
        }

        private void OnDisable()
        {
            StopBreathing();
        }

        private void StopBreathing()
        {
            breathingTween?.Kill();
            breathingTween = null;
            if (cachedRectTransform != null)
            {
                cachedRectTransform.localScale = Vector3.one * wrapperScale;
            }
        }
    }
}
