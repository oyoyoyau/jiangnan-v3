using cfg;
using JN.Client.Manager;
using JN.Client.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.Scene
{
    /// <summary>
    /// 场景雇佣头顶 HUD：挂在 Overlay 画布上跟随挂点。
    /// 主流程已改回底栏员工按钮招募，本组件仅作兼容保留。
    /// </summary>
    public class EmployAreaUI : MonoBehaviour
    {
        private const string RecruitChefSpritePath =
            "Assets/Res/Resources/Textures/UI/Panel/TavernScene/img_RecruitChef_Btn.png";
        private const string RecruitWaiterSpritePath =
            "Assets/Res/Resources/Textures/UI/Panel/TavernScene/img_RecruitWaiter_Btn.png";

        [SerializeField] private Vector3 offset = new(0f, 0.72f, 0f);
        [SerializeField] public GameObject group_PayCoinNum;
        [SerializeField] private TextMeshProUGUI payCoinText;
        [SerializeField] private Image buttonImage;
        [SerializeField] private Button employButton;

        private RectTransform rectTransform;
        private CanvasGroup canvasGroup;
        private Transform targetTile;
        private System.Action onEmploy;
        private string employKey;
        private StaffPosition staffPosition;
        private bool promptVisible;
        private bool screenVisible = true;
        private int cachedPositionIconId = -1;

        /// <summary>当前绑定的招聘键值。</summary>
        public string EmployKey => employKey;

        /// <summary>当前绑定的场景目标。</summary>
        public Transform BoundTarget => targetTile;

        /// <summary>当前绑定职位。</summary>
        public StaffPosition BoundPosition => staffPosition;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            EnsureCanvasGroup();
            CacheStaticReferences();
            EnsureEmployClick();
            ButtonPressScale.EnsureAttached(gameObject);
            RefreshVisibility();
        }

        /// <summary>
        /// 绑定职位雇佣 HUD：挂点、价格与点击回调。
        /// </summary>
        public void BindEmploy(
            string key,
            Transform target,
            StaffPosition position,
            int cost,
            System.Action onEmployAction)
        {
            employKey = key;
            targetTile = target;
            staffPosition = position;
            onEmploy = onEmployAction;
            ApplyPositionVisual(position);
            SetEmployPrompt(true, cost);
            EnsureEmployClick();
        }

        /// <summary>
        /// 兼容旧接口：无职位时仅绑定回调，不刷新职位图标。
        /// </summary>
        public void BindEmploy(string key, Transform target, System.Action onEmployAction)
        {
            employKey = key;
            targetTile = target;
            onEmploy = onEmployAction;
            EnsureEmployClick();
            RefreshVisibility();
        }

        /// <summary>
        /// 设置雇佣价格提示显隐和价格。
        /// </summary>
        public void SetEmployPrompt(bool visible, int cost)
        {
            promptVisible = visible;
            if (group_PayCoinNum != null)
            {
                group_PayCoinNum.SetActive(visible);
            }

            if (payCoinText != null)
            {
                payCoinText.raycastTarget = false;
                payCoinText.text = Mathf.Max(0, cost).ToString();
            }

            RefreshVisibility();
        }

        /// <summary>获取世界锚点位置。</summary>
        public Vector3 GetWorldAnchorPosition()
        {
            return targetTile == null ? Vector3.zero : targetTile.position + offset;
        }

        /// <summary>设置画布锚点位置。</summary>
        public void SetAnchoredPosition(Vector2 position)
        {
            rectTransform ??= GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = position;
            }
        }

        /// <summary>设置屏幕可见状态。</summary>
        public void SetVisible(bool visible)
        {
            screenVisible = visible;
            RefreshVisibility();
        }

        private void HandleEmployClicked()
        {
            if (!promptVisible || onEmploy == null)
            {
                return;
            }

            GameAudioManager.PlayButtonClick();
            onEmploy.Invoke();
        }

        private void ApplyPositionVisual(StaffPosition position)
        {
            CacheStaticReferences();
            if (buttonImage == null)
            {
                return;
            }

            var iconId = (int)position;
            if (cachedPositionIconId == iconId && buttonImage.sprite != null)
            {
                return;
            }

            var spritePath = position == StaffPosition.Chef
                ? RecruitChefSpritePath
                : RecruitWaiterSpritePath;
            var sprite = GameplayResourceStore.LoadAsset<Sprite>(spritePath);
            if (sprite != null)
            {
                buttonImage.sprite = sprite;
                cachedPositionIconId = iconId;
            }
            else
            {
                Debug.LogWarning($"[EmployAreaUI] 未找到招募按钮图：{spritePath}");
            }
        }

        private void CacheStaticReferences()
        {
            if (group_PayCoinNum == null)
            {
                group_PayCoinNum = transform.Find("group_PayCoinNum")?.gameObject;
            }

            if (payCoinText == null)
            {
                payCoinText = transform.Find("group_PayCoinNum/txt_CoinNum")?.GetComponent<TextMeshProUGUI>()
                              ?? transform.Find("txt_CoinNum")?.GetComponent<TextMeshProUGUI>();
            }

            if (buttonImage == null)
            {
                buttonImage = GetComponent<Image>();
            }

            if (employButton == null)
            {
                employButton = GetComponent<Button>();
            }
        }

        private void EnsureEmployClick()
        {
            CacheStaticReferences();
            if (buttonImage != null)
            {
                buttonImage.raycastTarget = true;
            }

            if (employButton == null)
            {
                employButton = gameObject.AddComponent<Button>();
                employButton.transition = Selectable.Transition.None;
                if (buttonImage != null)
                {
                    employButton.targetGraphic = buttonImage;
                }
            }

            employButton.onClick.RemoveListener(HandleEmployClicked);
            employButton.onClick.AddListener(HandleEmployClicked);
        }

        private void RefreshVisibility()
        {
            EnsureCanvasGroup();
            var visible = screenVisible && promptVisible && targetTile != null;
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.blocksRaycasts = visible;
            canvasGroup.interactable = visible;
        }

        private void EnsureCanvasGroup()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        private void OnDestroy()
        {
            if (employButton != null)
            {
                employButton.onClick.RemoveListener(HandleEmployClicked);
            }
        }
    }
}
