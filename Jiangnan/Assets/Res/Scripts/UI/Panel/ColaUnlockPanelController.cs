using System;
using DG.Tweening;
using JN.Client.Manager;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JN.Client.UI
{
    /// <summary>
    /// 三星后解锁可乐买卖的拍脸数据。
    /// </summary>
    public class ColaUnlockPanelControllerData : UIPanelData
    {
        public Action OnClosed;
    }

    /// <summary>
    /// 三星酒楼回到店内后：恭喜解锁可乐买卖，点「接受」后关闭并放出底栏可乐按钮。
    /// </summary>
    public class ColaUnlockPanelController : OverlayPanelController<ColaUnlockPanelControllerData>
    {
        private const string CardSpritePath =
            "Assets/Res/Resources/Textures/UI/ColaUnlock/colaUnlockCard.jpg";
        private const string AcceptSpritePath =
            "Assets/Res/Resources/Textures/UI/ColaUnlock/colaUnlockAccept.png";
        private const string TitleText = "恭喜!";
        private const string BodyText = "已解锁<color=#FFE44D>[可乐]</color>\n买卖功能!";
        private const float HoverScale = 1.12f;
        private const float HoverDuration = 0.12f;
        private const float CardPopDuration = 0.22f;
        private const float TitleFontSize = 68f;
        private const float BodyFontSize = 42f;
        private const float TitleOutlineWidth = 0.2f;
        private const float BodyOutlineWidth = 0.18f;

        private static readonly Vector2 CardSize = new(780f, 1220f);
        private static readonly Color MaskColor = new(0f, 0f, 0f, 0.55f);
        private static TMP_FontAsset cachedUiFont;
        private static Material cachedUiFontMaterial;

        private RectTransform panelRect;
        private RectTransform cardRect;
        private Image maskImage;
        private Image cardImage;
        private TMP_Text titleText;
        private TMP_Text bodyText;
        private Button acceptButton;
        private RectTransform acceptRect;
        private Image acceptImage;
        private Tween hoverTween;
        private Tween cardPopTween;
        private bool accepted;

        protected override void OnPanelInit()
        {
            EnsureNodes();
        }

        protected override void OnPanelOpen(ColaUnlockPanelControllerData data)
        {
            accepted = false;
            EnsureNodes();
            ApplyLayout();
            ApplySprites();
        }

        protected override void OnPanelShow()
        {
            accepted = false;
            EnsureNodes();
            ApplyLayout();
            ApplySprites();
            ResetAcceptScale();
            GameAudioManager.PlayFacilityPurchaseSuccess();
            PlayCardPop();
        }

        protected override void OnPanelClose()
        {
            KillHoverTween();
            KillCardPopTween();
            ResetAcceptScale();

            var callback = Data?.OnClosed;
            if (Data != null)
            {
                Data.OnClosed = null;
            }

            if (callback == null)
            {
                return;
            }

            ActionKit.NextFrame(callback).StartGlobal();
        }

        private void OnDestroy()
        {
            KillHoverTween();
            KillCardPopTween();
        }

        private void EnsureNodes()
        {
            panelRect ??= GetComponent<RectTransform>();
            var canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
            canvasGroup.alpha = 1f;

            maskImage ??= EnsureChildImage("img_Mask", out _);
            cardImage ??= EnsureChildImage("img_Card", out cardRect);
            titleText ??= EnsureChildText("txt_Title", cardRect);
            bodyText ??= EnsureChildText("txt_Body", cardRect);
            if (acceptButton == null)
            {
                acceptImage = EnsureChildImage("btn_Accept", out acceptRect, cardRect);
                acceptButton = acceptRect.gameObject.GetComponent<Button>()
                               ?? acceptRect.gameObject.AddComponent<Button>();
                acceptButton.transition = Selectable.Transition.None;
                acceptButton.targetGraphic = acceptImage;
                BindButton(acceptButton, OnClickAccept);
                BindAcceptHover();
            }
        }

        private void ApplyLayout()
        {
            if (panelRect != null)
            {
                panelRect.anchorMin = Vector2.zero;
                panelRect.anchorMax = Vector2.one;
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;
                panelRect.localScale = Vector3.one;
            }

            StretchFull(maskImage != null ? maskImage.rectTransform : null);
            if (maskImage != null)
            {
                maskImage.color = MaskColor;
                maskImage.raycastTarget = true;
                maskImage.sprite = null;
            }

            if (cardRect != null)
            {
                cardRect.anchorMin = new Vector2(0.5f, 0.5f);
                cardRect.anchorMax = new Vector2(0.5f, 0.5f);
                cardRect.pivot = new Vector2(0.5f, 0.5f);
                cardRect.sizeDelta = CardSize;
                cardRect.anchoredPosition = Vector2.zero;
                cardRect.SetAsLastSibling();
            }

            if (cardImage != null)
            {
                cardImage.color = Color.white;
                cardImage.preserveAspect = true;
                cardImage.raycastTarget = false;
            }

            LayoutText(titleText, new Vector2(0f, -240f), new Vector2(640f, 90f), TitleFontSize, wrap: false);
            LayoutText(bodyText, new Vector2(0f, -365f), new Vector2(640f, 150f), BodyFontSize, wrap: true);
            if (titleText != null)
            {
                titleText.text = TitleText;
                titleText.alignment = TextAlignmentOptions.Center;
                titleText.outlineWidth = TitleOutlineWidth;
            }

            if (bodyText != null)
            {
                bodyText.text = BodyText;
                bodyText.alignment = TextAlignmentOptions.Center;
                bodyText.lineSpacing = 8f;
                bodyText.richText = true;
                bodyText.outlineWidth = BodyOutlineWidth;
            }

            if (acceptRect != null)
            {
                acceptRect.anchorMin = new Vector2(0.5f, 0.5f);
                acceptRect.anchorMax = new Vector2(0.5f, 0.5f);
                acceptRect.pivot = new Vector2(0.5f, 0.5f);
                acceptRect.sizeDelta = new Vector2(560f, 156f);
                acceptRect.anchoredPosition = new Vector2(0f, -530f);
                acceptRect.SetAsLastSibling();
            }

            if (acceptImage != null)
            {
                acceptImage.color = Color.white;
                acceptImage.preserveAspect = true;
                acceptImage.raycastTarget = true;
            }
        }

        private void ApplySprites()
        {
            ApplySprite(cardImage, CardSpritePath);
            ApplySprite(acceptImage, AcceptSpritePath);
        }

        private void PlayCardPop()
        {
            if (cardRect == null)
            {
                return;
            }

            KillCardPopTween();
            cardRect.localScale = Vector3.one * 0.82f;
            cardPopTween = cardRect.DOScale(1f, CardPopDuration)
                .SetEase(Ease.OutBack)
                .SetUpdate(true)
                .OnKill(() => cardPopTween = null);
        }

        private void OnClickAccept()
        {
            if (accepted)
            {
                return;
            }

            accepted = true;
            DataManager.Instance?.UnlockColaTrading();
            CloseSelf();
        }

        private void BindAcceptHover()
        {
            if (acceptButton == null)
            {
                return;
            }

            var trigger = acceptButton.gameObject.GetComponent<EventTrigger>()
                          ?? acceptButton.gameObject.AddComponent<EventTrigger>();
            trigger.triggers ??= new System.Collections.Generic.List<EventTrigger.Entry>();
            trigger.triggers.RemoveAll(entry =>
                entry.eventID is EventTriggerType.PointerEnter or EventTriggerType.PointerExit);

            AddHoverEntry(trigger, EventTriggerType.PointerEnter, () => ScaleAccept(HoverScale));
            AddHoverEntry(trigger, EventTriggerType.PointerExit, () => ScaleAccept(1f));
        }

        private static void AddHoverEntry(EventTrigger trigger, EventTriggerType type, Action callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => callback());
            trigger.triggers.Add(entry);
        }

        private void ScaleAccept(float scale)
        {
            if (acceptRect == null)
            {
                return;
            }

            KillHoverTween();
            hoverTween = acceptRect.DOScale(scale, HoverDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .OnKill(() => hoverTween = null);
        }

        private void ResetAcceptScale()
        {
            KillHoverTween();
            if (acceptRect != null)
            {
                acceptRect.localScale = Vector3.one;
            }
        }

        private void KillHoverTween()
        {
            if (hoverTween == null)
            {
                return;
            }

            hoverTween.Kill();
            hoverTween = null;
        }

        private void KillCardPopTween()
        {
            if (cardPopTween == null)
            {
                return;
            }

            cardPopTween.Kill();
            cardPopTween = null;
        }

        private Image EnsureChildImage(string name, out RectTransform rect, Transform parent = null)
        {
            parent ??= transform;
            var existing = parent.Find(name);
            if (existing != null)
            {
                rect = existing as RectTransform ?? existing.GetComponent<RectTransform>();
                return existing.GetComponent<Image>() ?? existing.gameObject.AddComponent<Image>();
            }

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            rect = go.GetComponent<RectTransform>();
            return go.GetComponent<Image>();
        }

        private TMP_Text EnsureChildText(string name, Transform parent)
        {
            if (parent == null)
            {
                return null;
            }

            var existing = parent.Find(name);
            if (existing != null)
            {
                return existing.GetComponent<TMP_Text>() ?? existing.gameObject.AddComponent<TextMeshProUGUI>();
            }

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            return go.GetComponent<TextMeshProUGUI>();
        }

        private static void LayoutText(
            TMP_Text text,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            bool wrap)
        {
            if (text == null)
            {
                return;
            }

            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            ApplyChineseUiFont(text);
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Normal;
            text.color = Color.white;
            text.raycastTarget = false;
            text.enableAutoSizing = false;
            text.enableWordWrapping = wrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.outlineColor = Color.black;
            text.characterSpacing = 2f;
        }

        /// <summary>
        /// 运行时创建的 TMP 默认是西文 LiberationSans，中文会缺字、描边糊成一团。
        /// 改用酒楼 HUD 已加载的阿里巴巴普惠体。
        /// </summary>
        private static void ApplyChineseUiFont(TMP_Text text)
        {
            EnsureCachedUiFont(text);
            if (cachedUiFont != null)
            {
                text.font = cachedUiFont;
            }
            else if (text.font == null && TMP_Settings.defaultFontAsset != null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }

            if (cachedUiFontMaterial != null)
            {
                text.fontSharedMaterial = cachedUiFontMaterial;
            }
        }

        private static void EnsureCachedUiFont(TMP_Text self)
        {
            if (cachedUiFont != null && cachedUiFontMaterial != null)
            {
                return;
            }

            if (cachedUiFont == null)
            {
                var loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                for (var i = 0; i < loadedFonts.Length; i++)
                {
                    var font = loadedFonts[i];
                    if (font != null && font.name == "AlibabaPuHuiTi SDF")
                    {
                        cachedUiFont = font;
                        break;
                    }
                }

                if (cachedUiFont == null)
                {
                    for (var i = 0; i < loadedFonts.Length; i++)
                    {
                        var font = loadedFonts[i];
                        if (font != null && font.name.Contains("AlibabaPuHuiTi"))
                        {
                            cachedUiFont = font;
                            break;
                        }
                    }
                }
            }

            var samples = UnityEngine.Object.FindObjectsOfType<TMP_Text>(true);
            for (var i = 0; i < samples.Length; i++)
            {
                var sample = samples[i];
                if (sample == null || sample == self || sample.font == null)
                {
                    continue;
                }

                if (cachedUiFont == null)
                {
                    cachedUiFont = sample.font;
                }

                if (sample.font != cachedUiFont || sample.fontSharedMaterial == null)
                {
                    continue;
                }

                cachedUiFontMaterial = sample.fontSharedMaterial;
                if (cachedUiFont.name.Contains("Alibaba"))
                {
                    return;
                }
            }
        }

        private static void StretchFull(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsFirstSibling();
        }

        private static void ApplySprite(Image image, string path)
        {
            if (image == null)
            {
                return;
            }

            var sprite = GameplayResourceStore.LoadAsset<Sprite>(path);
            if (sprite == null)
            {
                return;
            }

            image.sprite = sprite;
            image.enabled = true;
        }
    }
}
