using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using JN.Client.Manager;

namespace JN.Client.UI
{
    /// <summary>
    /// 贵客进店头顶气泡：显示大堂/包厢图标与文案，点击后由外部处理逻辑。
    /// 挂在 VipGuestAction 预制体根节点。
    /// </summary>
    public class VipGuestActionView : MonoBehaviour
    {
        public const float DefaultHeadOffsetY = 1.35f;

        private const string DatangIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/vip_Datang.png";
        private const string BaoxiangIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/vip_Baoxiang.png";
        private const string ColaIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/vip_Cola.png";
        private static readonly Vector2 DefaultIconSize = new(110f, 75f);
        private static readonly Vector2 ColaIconSize = new(72f, 88f);
        /// <summary>二楼不可用时包厢按钮置灰色（#CDCDCD）。</summary>
        private static readonly Color LockedPrivateRoomTint = new(0xCD / 255f, 0xCD / 255f, 0xCD / 255f, 1f);

        [SerializeField] private Image iconImage;
        [SerializeField] private Button iconButton;
        [SerializeField] private TextMeshProUGUI actionText;
        [SerializeField] private Sprite datangSprite;
        [SerializeField] private Sprite baoxiangSprite;
        [SerializeField] private Sprite colaSprite;

        private RectTransform cachedRectTransform;
        private CanvasGroup cachedCanvasGroup;
        private Transform followTarget;
        private Vector3 worldOffset = new(0f, DefaultHeadOffsetY, 0f);
        private Action clickHandler;
        private bool isVisible = true;
        private bool clickConsumed;
        private bool privateRoomLocked;
        private bool showColaIcon;
        private bool colaClickable;

        /// <summary>外部容器据此销毁条目。</summary>
        public bool ShouldRelease { get; private set; }

        /// <summary>是否包厢（上二楼）；否则为大堂（插队首）。</summary>
        public bool IsPrivateRoomAction { get; private set; }

        private void Awake()
        {
            CacheNodes();
            EnsureComponents();
        }

        /// <summary>
        /// 绑定跟随目标，并按是否走包厢刷新图标/文案与点击回调。
        /// privateRoomLocked：包厢不可用时置灰，点击仅提示、不收起气泡。
        /// showColaIcon：点包厢后改为只显示可乐图标；传入 onClick 时可点击上可乐。
        /// </summary>
        public void Bind(
            Transform target,
            Vector3 offset,
            bool usePrivateRoom,
            Action onClick,
            bool privateRoomLocked = false,
            bool showColaIcon = false)
        {
            CacheNodes();
            EnsureComponents();
            followTarget = target;
            worldOffset = offset;
            clickHandler = onClick;
            clickConsumed = false;
            ShouldRelease = false;
            this.showColaIcon = showColaIcon;
            this.privateRoomLocked = privateRoomLocked && usePrivateRoom;
            IsPrivateRoomAction = usePrivateRoom;
            colaClickable = showColaIcon && onClick != null;
            ApplyActionVisual(usePrivateRoom);
            ApplyLockedTint(this.privateRoomLocked);
            if (showColaIcon && !colaClickable)
            {
                BindColaDisplayOnly();
            }
            else
            {
                BindClick();
            }

            SetVisible(true);
        }

        public Vector3 GetWorldAnchorPosition()
        {
            return followTarget != null ? followTarget.position + worldOffset : Vector3.zero;
        }

        public void SetAnchoredPosition(Vector2 position)
        {
            if (cachedRectTransform != null)
            {
                cachedRectTransform.anchoredPosition = position;
            }
        }

        public void SetVisible(bool visible)
        {
            isVisible = visible;
            if (cachedCanvasGroup != null)
            {
                var allowClick = visible && !clickConsumed && (!showColaIcon || colaClickable);
                cachedCanvasGroup.alpha = visible ? 1f : 0f;
                cachedCanvasGroup.blocksRaycasts = allowClick;
                cachedCanvasGroup.interactable = allowClick;
            }
        }

        public void Tick()
        {
            if (ShouldRelease)
            {
                return;
            }

            if (followTarget == null)
            {
                MarkForRelease();
            }
        }

        /// <summary>点击生效后立刻收起气泡。</summary>
        public void MarkForRelease()
        {
            ShouldRelease = true;
            clickConsumed = true;
            SetVisible(false);
        }

        private void CacheNodes()
        {
            if (iconImage == null)
            {
                var iconTransform = transform.Find("img_Icon");
                iconImage = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
            }

            if (iconButton == null)
            {
                iconButton = iconImage != null
                    ? iconImage.GetComponent<Button>()
                    : transform.Find("img_Icon")?.GetComponent<Button>();
            }

            if (actionText == null)
            {
                var textTransform = transform.Find("txt_action");
                actionText = textTransform != null ? textTransform.GetComponent<TextMeshProUGUI>() : null;
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
        }

        private void ApplyActionVisual(bool usePrivateRoom)
        {
            var sprite = showColaIcon
                ? ResolveColaSprite()
                : (usePrivateRoom ? ResolveBaoxiangSprite() : ResolveDatangSprite());
            if (iconImage != null && sprite != null)
            {
                iconImage.sprite = sprite;
                iconImage.preserveAspect = true;
                var iconRect = iconImage.rectTransform;
                if (iconRect != null)
                {
                    iconRect.sizeDelta = showColaIcon ? ColaIconSize : DefaultIconSize;
                }
            }

            if (actionText != null)
            {
                actionText.gameObject.SetActive(!showColaIcon);
                if (!showColaIcon)
                {
                    actionText.text = usePrivateRoom ? "包厢" : "大堂";
                }
            }
        }

        private void ApplyLockedTint(bool locked)
        {
            var tint = locked ? LockedPrivateRoomTint : Color.white;
            if (iconImage != null)
            {
                iconImage.color = tint;
            }

            if (actionText != null)
            {
                actionText.color = tint;
            }

            // 背景一并置灰，避免只有图标变灰、底板仍亮。
            var bg = transform.Find("img_Bg")?.GetComponent<Image>();
            if (bg != null)
            {
                bg.color = tint;
            }
        }

        private Sprite ResolveDatangSprite()
        {
            if (datangSprite != null)
            {
                return datangSprite;
            }

            datangSprite = GameplayResourceStore.LoadAsset<Sprite>(DatangIconPath);
            return datangSprite;
        }

        private Sprite ResolveBaoxiangSprite()
        {
            if (baoxiangSprite != null)
            {
                return baoxiangSprite;
            }

            baoxiangSprite = GameplayResourceStore.LoadAsset<Sprite>(BaoxiangIconPath);
            return baoxiangSprite;
        }

        private Sprite ResolveColaSprite()
        {
            if (colaSprite != null)
            {
                return colaSprite;
            }

            colaSprite = GameplayResourceStore.LoadAsset<Sprite>(ColaIconPath);
            return colaSprite;
        }

        private void BindClick()
        {
            if (iconButton == null)
            {
                return;
            }

            iconButton.onClick.RemoveListener(HandleClick);
            iconButton.onClick.AddListener(HandleClick);
            // 置灰仍可点出提示，不要把 Button.interactable 关掉。
            iconButton.interactable = true;
        }

        /// <summary>可乐需求气泡只展示，不接收点击。</summary>
        private void BindColaDisplayOnly()
        {
            if (iconButton != null)
            {
                iconButton.onClick.RemoveListener(HandleClick);
                iconButton.interactable = false;
            }
        }

        private void HandleClick()
        {
            if (clickConsumed || ShouldRelease)
            {
                return;
            }

            GameAudioManager.PlayButtonClick();

            // 可乐需求：由外部判定库存；不足时气泡保留，可再点。
            if (showColaIcon && colaClickable)
            {
                clickHandler?.Invoke();
                return;
            }

            // 未开放二楼：只飘字，气泡保留，贵客继续等待选择。
            if (privateRoomLocked)
            {
                clickHandler?.Invoke();
                return;
            }

            clickConsumed = true;
            var handler = clickHandler;
            MarkForRelease();
            handler?.Invoke();
        }

        private void OnDestroy()
        {
            if (iconButton != null)
            {
                iconButton.onClick.RemoveListener(HandleClick);
            }
        }
    }
}
