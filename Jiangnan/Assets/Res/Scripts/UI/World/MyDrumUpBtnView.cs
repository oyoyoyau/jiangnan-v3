using System;
using JN.Client.Manager;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.UI
{
    /// <summary>
    /// 场景轿子挂点上的拉客按钮（MyDrumUpBtn）：跟随世界坐标，逻辑对齐底栏 btn_DrumUp。
    /// </summary>
    public class MyDrumUpBtnView : MonoBehaviour
    {
        private const string DrumUpLakeSpritePath = "Assets/Res/Resources/Textures/UI/DrumUp/lake.png";
        private const string DrumUpLakeGraySpritePath = "Assets/Res/Resources/Textures/UI/DrumUp/lake_gray.png";

        private Transform target;
        private Vector3 worldOffset;
        private RectTransform cachedRectTransform;
        private CanvasGroup cachedCanvasGroup;
        private Button button;
        private Image iconImage;
        private TMP_Text countDownText;
        private GameObject redDot;
        private Sprite lakeSprite;
        private Sprite lakeGraySprite;
        private Action onClick;
        private bool isVisible = true;
        private float nextVisualRefreshUnscaledTime;

        /// <summary>绑定跟随目标与点击回调。</summary>
        public void Bind(Transform followTarget, Vector3 offset, Action clickHandler)
        {
            target = followTarget;
            worldOffset = offset;
            onClick = clickHandler;
            EnsureNodes();
            EnsureClick();
            RefreshVisual();
        }

        /// <summary>世界锚点位置。</summary>
        public Vector3 GetWorldAnchorPosition()
        {
            return target != null ? target.position + worldOffset : Vector3.zero;
        }

        /// <summary>设置 UI 锚点坐标。</summary>
        public void SetAnchoredPosition(Vector2 position)
        {
            EnsureComponents();
            cachedRectTransform.anchoredPosition = position;
        }

        /// <summary>屏幕可见性（相机背后隐藏）。</summary>
        public void SetVisible(bool visible)
        {
            if (isVisible == visible)
            {
                return;
            }

            isVisible = visible;
            EnsureComponents();
            cachedCanvasGroup.alpha = visible ? 1f : 0f;
            cachedCanvasGroup.blocksRaycasts = visible;
            cachedCanvasGroup.interactable = visible;
        }

        /// <summary>每帧由 HUD 容器调用：冷却倒计时与可拉客态。</summary>
        public void TickVisual()
        {
            if (Time.unscaledTime < nextVisualRefreshUnscaledTime)
            {
                return;
            }

            nextVisualRefreshUnscaledTime = Time.unscaledTime + 0.25f;
            RefreshVisual();
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
        }

        private void EnsureNodes()
        {
            EnsureComponents();
            button ??= GetComponent<Button>();
            if (iconImage == null)
            {
                var icon = transform.Find("img_BtnIcon")
                           ?? HudBindingUtility.FindChildRecursive(transform, "img_BtnIcon");
                iconImage = icon != null ? icon.GetComponent<Image>() : null;
            }

            if (countDownText == null)
            {
                var countDown = transform.Find("txt_countDown")
                                ?? HudBindingUtility.FindChildRecursive(transform, "txt_countDown");
                countDownText = countDown != null ? countDown.GetComponent<TMP_Text>() : null;
            }

            if (redDot == null)
            {
                redDot = transform.Find("img_Red")?.gameObject
                         ?? HudBindingUtility.FindChildRecursive(transform, "img_Red")?.gameObject;
            }

            lakeSprite ??= GameplayResourceStore.LoadAsset<Sprite>(DrumUpLakeSpritePath);
            lakeGraySprite ??= GameplayResourceStore.LoadAsset<Sprite>(DrumUpLakeGraySpritePath);
            ButtonPressScale.EnsureAttached(gameObject);
        }

        private void EnsureClick()
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
            button.interactable = true;
        }

        private void HandleClick()
        {
            GameAudioManager.PlayButtonClick();
            onClick?.Invoke();
        }

        private void RefreshVisual()
        {
            EnsureNodes();
            var dataManager = DataManager.Instance;
            var hasJiaozi = dataManager != null && dataManager.IsJiaoziUnlocked();
            var remaining = dataManager != null ? dataManager.GetPullCustomerCooldownRemainingSeconds() : 0f;
            var onCooldown = remaining > 0.01f;
            var canPull = hasJiaozi && !onCooldown;

            ApplyIconVisual(canPull);
            ApplyCountDown(onCooldown, remaining);
            ApplyRedDot(canPull);
        }

        private void ApplyIconVisual(bool canPull)
        {
            if (iconImage == null)
            {
                return;
            }

            var sprite = canPull ? lakeSprite : lakeGraySprite;
            if (sprite != null && iconImage.sprite != sprite)
            {
                iconImage.sprite = sprite;
            }
        }

        private void ApplyCountDown(bool onCooldown, float remaining)
        {
            if (countDownText == null)
            {
                return;
            }

            if (onCooldown)
            {
                if (!countDownText.gameObject.activeSelf)
                {
                    countDownText.gameObject.SetActive(true);
                }

                countDownText.text = FormatPullCooldown(remaining);
                return;
            }

            countDownText.text = string.Empty;
            if (countDownText.gameObject.activeSelf)
            {
                countDownText.gameObject.SetActive(false);
            }
        }

        private void ApplyRedDot(bool show)
        {
            if (redDot == null)
            {
                return;
            }

            if (redDot.activeSelf != show)
            {
                redDot.SetActive(show);
            }
        }

        private static string FormatPullCooldown(float remainingSeconds)
        {
            var total = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            var minutes = total / 60;
            var seconds = total % 60;
            return $"{minutes:00}:{seconds:00}";
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
            }
        }
    }
}
