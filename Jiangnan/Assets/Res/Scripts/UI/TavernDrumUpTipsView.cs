using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using JN.Client.Manager;

namespace JN.Client.UI
{
    /// <summary>
    /// 桌位「客人被拉走」提示：跟随桌子，显示拉客玩家头像与文案。
    /// 挂在 TavernDrumUpTipsItem 预制体根节点；Root 上 Button 可点击关闭。
    /// </summary>
    public sealed class TavernDrumUpTipsView : MonoBehaviour
    {
        public const float DefaultTableOffsetY = TavernWorldRuntimeHudLayout.TableActionHeightOffset;

        private const string HeadIconPathFormat = "Assets/Res/Resources/UI/HeadIcon/{0}.png";
        /// <summary>玩家自己默认头像（与 BuildingItem 自家一致）。</summary>
        private const string SelfDefaultHeadIconPath =
            "Assets/Res/Resources/Textures/UI/CreatePlayer/tx.png";
        private const int MinHeadIconId = 1;
        private const int MaxHeadIconId = 8;

        [SerializeField] private Image headImage;
        [SerializeField] private TextMeshProUGUI targetText;
        [SerializeField] private Button rootButton;

        private RectTransform cachedRectTransform;
        private CanvasGroup cachedCanvasGroup;
        private Transform followTarget;
        private Vector3 worldOffset = new(0f, DefaultTableOffsetY, 0f);
        private bool screenVisible = true;
        private Action onClicked;

        public int TableId { get; private set; }

        public bool ShouldRelease { get; private set; }

        private void Awake()
        {
            EnsureComponents();
            BindRootButtonListener();
        }

        private void OnDestroy()
        {
            if (rootButton != null)
            {
                rootButton.onClick.RemoveListener(HandleRootClicked);
            }
        }

        /// <summary>
        /// 绑定桌子与拉客展示。
        /// </summary>
        /// <param name="displayCaption">非空时直接作为文案；否则用「客人被{pullerName}拉走」。</param>
        /// <param name="useSelfHeadIcon">为真时用玩家默认头像 tx.png。</param>
        /// <param name="clickEnabled">为假时不可点击关闭（拜访他人酒楼）。</param>
        public void Bind(
            Transform tableTarget,
            int tableId,
            Vector3 offset,
            int headIconId,
            string pullerName,
            Action onClick = null,
            string displayCaption = null,
            bool useSelfHeadIcon = false,
            bool clickEnabled = true)
        {
            EnsureComponents();
            BindRootButtonListener();
            followTarget = tableTarget;
            TableId = tableId;
            worldOffset = offset;
            onClicked = clickEnabled ? onClick : null;
            ShouldRelease = false;

            if (headImage != null)
            {
                Sprite sprite = null;
                if (useSelfHeadIcon)
                {
                    sprite = GameplayResourceStore.LoadAsset<Sprite>(SelfDefaultHeadIconPath);
                }
                else
                {
                    var clampedId = Mathf.Clamp(headIconId, MinHeadIconId, MaxHeadIconId);
                    sprite = GameplayResourceStore.LoadAsset<Sprite>(
                        string.Format(HeadIconPathFormat, clampedId));
                }

                if (sprite != null)
                {
                    headImage.sprite = sprite;
                    headImage.preserveAspect = true;
                    headImage.enabled = true;
                    headImage.raycastTarget = false;
                }
            }

            if (targetText != null)
            {
                if (!string.IsNullOrWhiteSpace(displayCaption))
                {
                    targetText.text = displayCaption.Trim();
                }
                else
                {
                    var name = string.IsNullOrWhiteSpace(pullerName) ? "他人" : pullerName.Trim();
                    targetText.text = $"客人被{name}拉走";
                }

                targetText.raycastTarget = false;
            }

            SetScreenVisible(true, clickEnabled);
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

        public void SetScreenVisible(bool visible, bool clickEnabled = true)
        {
            screenVisible = visible;
            if (cachedCanvasGroup == null)
            {
                return;
            }

            cachedCanvasGroup.alpha = visible ? 1f : 0f;
            // 拜访他人：可见但不接收点击。
            var canClick = visible && clickEnabled && onClicked != null;
            cachedCanvasGroup.blocksRaycasts = canClick;
            cachedCanvasGroup.interactable = canClick;
            if (rootButton != null)
            {
                rootButton.interactable = canClick;
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
                ShouldRelease = true;
            }
        }

        public void MarkForRelease()
        {
            ShouldRelease = true;
            SetScreenVisible(false, clickEnabled: false);
        }

        private void HandleRootClicked()
        {
            if (ShouldRelease || !screenVisible)
            {
                return;
            }

            GameAudioManager.PlayButtonClick();
            onClicked?.Invoke();
        }

        private void BindRootButtonListener()
        {
            if (rootButton == null)
            {
                return;
            }

            rootButton.onClick.RemoveListener(HandleRootClicked);
            rootButton.onClick.AddListener(HandleRootClicked);
        }

        private void EnsureComponents()
        {
            cachedRectTransform ??= transform as RectTransform;
            cachedCanvasGroup ??= GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            if (headImage == null)
            {
                var head = transform.Find("Root/img_PlayAvatarIcon")
                           ?? transform.Find("img_PlayAvatarIcon")
                           ?? FindDeepChild(transform, "img_PlayAvatarIcon")
                           ?? transform.Find("Root/img_head")
                           ?? transform.Find("img_head")
                           ?? FindDeepChild(transform, "img_head");
                headImage = head != null ? head.GetComponent<Image>() : null;
            }

            if (targetText == null)
            {
                var textNode = transform.Find("Root/txt_target")
                               ?? transform.Find("txt_target")
                               ?? FindDeepChild(transform, "txt_target");
                targetText = textNode != null ? textNode.GetComponent<TextMeshProUGUI>() : null;
            }

            if (rootButton == null)
            {
                var root = transform.Find("Root") ?? FindDeepChild(transform, "Root");
                rootButton = root != null ? root.GetComponent<Button>() : null;
                rootButton ??= GetComponentInChildren<Button>(true);
            }
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName))
            {
                return null;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var child = root.GetChild(index);
                if (child != null && child.name == childName)
                {
                    return child;
                }

                var nested = FindDeepChild(child, childName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
