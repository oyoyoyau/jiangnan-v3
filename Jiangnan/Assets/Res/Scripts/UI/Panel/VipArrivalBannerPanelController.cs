using DG.Tweening;
using JN.Client.Manager;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.UI
{
    /// <summary>
    /// 贵客临门横幅：从下方滑入顶部，停留后继续向上滑出。
    /// </summary>
    public class VipArrivalBannerPanelControllerData : QFramework.UIPanelData
    {
    }

    /// <summary>
    /// 自家店贵客到店（自己来或抢客卸下）时的顶部横幅。
    /// </summary>
    public class VipArrivalBannerPanelController : OverlayPanelController<VipArrivalBannerPanelControllerData>
    {
        private const string BannerSpritePath =
            "Assets/Res/Resources/Textures/UI/VipArrival/vipArrivalBanner.png";

        private static readonly Vector2 PanelAnchor = new(0.5f, 1f);
        private static readonly Vector2 PanelPivot = new(0.5f, 1f);
        private static readonly Vector2 PanelSize = new(1020f, 340f);
        private const float StartAnchoredY = -380f;
        private const float SettledAnchoredY = -8f;
        private const float ExitAnchoredY = 420f;
        private const float EaseInSeconds = 0.45f;
        private const float HoldSeconds = 3f;
        private const float EaseOutSeconds = 0.4f;

        private RectTransform panelRect;
        private CanvasGroup canvasGroup;
        private Image bannerImage;
        private Tween panelTween;

        protected override void OnPanelInit()
        {
            EnsureNodes();
        }

        protected override void OnPanelOpen(VipArrivalBannerPanelControllerData data)
        {
            EnsureNodes();
            ApplyLayout();
            ApplySprite();
        }

        protected override void OnPanelShow()
        {
            EnsureNodes();
            ApplyLayout();
            ApplySprite();
            GameAudioManager.PlayVipArrival();
            PlayBannerSequence();
        }

        protected override void OnPanelClose()
        {
            KillPanelTween();
        }

        private void OnDestroy()
        {
            KillPanelTween();
        }

        private void EnsureNodes()
        {
            panelRect ??= GetComponent<RectTransform>();
            canvasGroup ??= GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            bannerImage ??= GetComponent<Image>();
            if (bannerImage == null)
            {
                bannerImage = gameObject.AddComponent<Image>();
            }
        }

        private void ApplyLayout()
        {
            if (panelRect == null)
            {
                return;
            }

            panelRect.anchorMin = PanelAnchor;
            panelRect.anchorMax = PanelAnchor;
            panelRect.pivot = PanelPivot;
            panelRect.sizeDelta = PanelSize;
            panelRect.anchoredPosition = new Vector2(0f, StartAnchoredY);
            panelRect.localScale = Vector3.one;
            panelRect.localRotation = Quaternion.identity;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }

            if (bannerImage != null)
            {
                bannerImage.raycastTarget = false;
                bannerImage.preserveAspect = true;
                bannerImage.color = Color.white;
            }
        }

        private void ApplySprite()
        {
            if (bannerImage == null)
            {
                return;
            }

            var sprite = GameplayResourceStore.LoadAsset<Sprite>(BannerSpritePath);
            if (sprite != null)
            {
                bannerImage.sprite = sprite;
            }
        }

        private void PlayBannerSequence()
        {
            KillPanelTween();
            if (panelRect == null)
            {
                CloseSelf();
                return;
            }

            panelRect.anchoredPosition = new Vector2(0f, StartAnchoredY);
            var settled = new Vector2(0f, SettledAnchoredY);
            var exit = new Vector2(0f, ExitAnchoredY);
            panelTween = DOTween.Sequence()
                .SetUpdate(true)
                .Append(panelRect.DOAnchorPos(settled, EaseInSeconds).SetEase(Ease.OutCubic))
                .AppendInterval(HoldSeconds)
                .Append(panelRect.DOAnchorPos(exit, EaseOutSeconds).SetEase(Ease.InCubic))
                .OnComplete(() =>
                {
                    panelTween = null;
                    CloseSelf();
                });
        }

        private void KillPanelTween()
        {
            if (panelTween == null)
            {
                return;
            }

            panelTween.Kill();
            panelTween = null;
        }
    }
}
