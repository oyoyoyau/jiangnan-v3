using TMPro;
using UnityEngine;

namespace JN.Client.UI
{
    /// <summary>
    /// 顾客反馈文字气泡：跟随模型，高度低于耐心条；可选限时自动消失。
    /// 挂在 TavernReviewTipsUI 预制体根节点。
    /// </summary>
    public class TavernReviewTipsView : MonoBehaviour
    {
        public const float DefaultHeadOffsetY = TavernWorldRuntimeHudLayout.CustomerReviewHeightOffset;

        private const string TipTextNodeName = "txt_Review";
        private const string FrameNodeName = "frame";
        private const float GuestFontSize = 30f;
        private const float MaxTextWidth = 360f;

        [SerializeField] private TMP_Text tipText;

        private RectTransform cachedRectTransform;
        private RectTransform frameRect;
        private CanvasGroup cachedCanvasGroup;
        private Transform followTarget;
        private Vector3 worldOffset = new(0f, DefaultHeadOffsetY, 0f);
        private bool screenVisible = true;
        private float remainingLifetimeSeconds = -1f;
        private bool hasLifetimeLimit;
        private Vector2 defaultTextSize;
        private Vector2 defaultFrameSize;
        private bool hasCachedDefaultLayout;

        /// <summary>容器据此销毁。</summary>
        public bool ShouldRelease { get; private set; }

        /// <summary>当前跟随的顾客 Transform，用于替换同目标气泡。</summary>
        public Transform FollowTarget => followTarget;

        private void Awake()
        {
            EnsureComponents();
        }

        /// <summary>
        /// 绑定跟随目标与文案；durationSeconds &lt; 0 表示常驻到模型消失。
        /// </summary>
        public void Bind(Transform target, Vector3 offset, string content, float durationSeconds = -1f)
        {
            EnsureComponents();
            followTarget = target;
            worldOffset = offset;
            ShouldRelease = false;
            hasLifetimeLimit = durationSeconds >= 0f;
            remainingLifetimeSeconds = durationSeconds;
            if (tipText != null)
            {
                tipText.text = content ?? string.Empty;
                tipText.raycastTarget = false;
                FitBubbleToGuestFont();
            }

            SetScreenVisible(true);
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

        public void SetScreenVisible(bool visible)
        {
            screenVisible = visible;
            if (cachedCanvasGroup == null)
            {
                return;
            }

            cachedCanvasGroup.alpha = visible ? 1f : 0f;
            cachedCanvasGroup.blocksRaycasts = false;
            cachedCanvasGroup.interactable = false;
        }

        public void Tick(float deltaTime)
        {
            if (ShouldRelease)
            {
                return;
            }

            if (followTarget == null)
            {
                ShouldRelease = true;
                return;
            }

            if (!hasLifetimeLimit)
            {
                return;
            }

            remainingLifetimeSeconds -= deltaTime;
            if (remainingLifetimeSeconds <= 0f)
            {
                ShouldRelease = true;
            }
        }

        public void MarkForRelease()
        {
            ShouldRelease = true;
        }

        private void EnsureComponents()
        {
            cachedRectTransform ??= transform as RectTransform ?? gameObject.AddComponent<RectTransform>();
            cachedCanvasGroup ??= GetComponent<CanvasGroup>();
            if (cachedCanvasGroup == null)
            {
                cachedCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            cachedCanvasGroup.blocksRaycasts = false;
            cachedCanvasGroup.interactable = false;

            if (tipText == null)
            {
                var textTransform = transform.Find(TipTextNodeName)
                                    ?? HudBindingUtility.FindChildRecursive(transform, TipTextNodeName);
                tipText = textTransform != null
                    ? textTransform.GetComponent<TMP_Text>()
                    : GetComponentInChildren<TMP_Text>(true);
            }

            frameRect ??= transform.Find(FrameNodeName) as RectTransform;
            CacheDefaultLayout();
        }

        private void CacheDefaultLayout()
        {
            if (hasCachedDefaultLayout)
            {
                return;
            }

            if (tipText != null)
            {
                defaultTextSize = tipText.rectTransform.sizeDelta;
            }

            if (frameRect != null)
            {
                defaultFrameSize = frameRect.sizeDelta;
            }

            hasCachedDefaultLayout = defaultTextSize.sqrMagnitude > 0.01f;
        }

        /// <summary>
        /// 长台词不再靠 auto-size 缩小字号，保持和普通宾客气泡同一字号，气泡跟着撑开。
        /// </summary>
        private void FitBubbleToGuestFont()
        {
            CacheDefaultLayout();
            if (tipText == null || !hasCachedDefaultLayout)
            {
                return;
            }

            tipText.enableAutoSizing = false;
            tipText.fontSize = GuestFontSize;
            tipText.enableWordWrapping = true;
            tipText.overflowMode = TextOverflowModes.Overflow;

            var preferred = tipText.GetPreferredValues(tipText.text, MaxTextWidth, 0f);
            var width = Mathf.Clamp(preferred.x + 12f, defaultTextSize.x, MaxTextWidth);
            var height = Mathf.Max(defaultTextSize.y, preferred.y + 8f);
            tipText.rectTransform.sizeDelta = new Vector2(width, height);

            if (frameRect == null || defaultTextSize.x <= 0.01f || defaultTextSize.y <= 0.01f)
            {
                return;
            }

            frameRect.sizeDelta = new Vector2(
                defaultFrameSize.x * (width / defaultTextSize.x),
                defaultFrameSize.y * (height / defaultTextSize.y));
        }
    }
}
