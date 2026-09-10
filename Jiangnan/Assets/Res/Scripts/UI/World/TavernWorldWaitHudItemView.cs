using DG.Tweening;
using JN.Client.Scene;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.UI
{
    /// <summary>
    /// 顾客等待状态头顶 HUD：状态图标 + 绿/红双环进度。
    /// </summary>
    public class TavernWorldWaitHudItemView : MonoBehaviour
    {
        public const float DefaultHeadOffsetY = TavernWorldRuntimeHudLayout.CustomerWaitHeightOffset;
        private const float FadeDuration = 0.22f;

        private RectTransform cachedRectTransform;
        private CanvasGroup cachedCanvasGroup;
        private Image foodIcon;
        private Image progressBackground;
        private Image fillGreen;
        private Image fillRed;
        private GameObject staminaBarRoot;

        private Transform target;
        private Vector3 worldOffset = new(0f, DefaultHeadOffsetY, 0f);
        private bool screenVisible = true;
        private float fadeAlpha = 1f;
        private bool isFadingOut;
        private Tween fadeTween;
        private CustomerWaitHudState currentState = CustomerWaitHudState.None;

        public bool ShouldRelease { get; private set; }

        private void Awake()
        {
            EnsureComponents();
        }

        private void OnDestroy()
        {
            fadeTween?.Kill();
            fadeTween = null;
        }

        public void BindTarget(Transform followTarget, Vector3 offset)
        {
            target = followTarget;
            worldOffset = offset;
        }

        public void Configure(CustomerWaitHudState state, Sprite icon)
        {
            EnsureComponents();
            ShouldRelease = false;
            isFadingOut = false;
            ApplyVisual(state, icon);
            SetWaitProgress(0f);
            PlayFadeIn();
        }

        public void RefreshVisual(CustomerWaitHudState state, Sprite icon)
        {
            if (ShouldRelease || isFadingOut)
            {
                return;
            }

            EnsureComponents();
            ApplyVisual(state, icon);
        }

        private void ApplyVisual(CustomerWaitHudState state, Sprite icon)
        {
            currentState = state;

            if (staminaBarRoot != null)
            {
                staminaBarRoot.SetActive(false);
            }

            // 耐心条不显示中心菜品/状态图标，只保留双环进度。
            if (foodIcon != null)
            {
                foodIcon.enabled = false;
                foodIcon.sprite = null;
                foodIcon.raycastTarget = false;
                if (foodIcon.gameObject.activeSelf)
                {
                    foodIcon.gameObject.SetActive(false);
                }
            }
        }

        public void SetWaitProgress(float normalizedProgress)
        {
            EnsureComponents();
            var progress = Mathf.Clamp01(normalizedProgress);

            if (fillGreen != null)
            {
                fillGreen.fillAmount = progress;
            }

            if (fillRed != null)
            {
                fillRed.fillAmount = progress;
                var color = fillRed.color;
                color.a = progress;
                fillRed.color = color;
            }
        }

        public void Tick(float deltaTime)
        {
            if (ShouldRelease)
            {
                return;
            }

            if (target == null)
            {
                MarkForRelease();
                return;
            }

            var customer = target.GetComponent<TavernCustomerRuntimeController>();
            if (customer != null && customer.IsLeavingTavern)
            {
                MarkForRelease();
            }
        }

        public Vector3 GetWorldAnchorPosition()
        {
            return target != null ? target.position + worldOffset : Vector3.zero;
        }

        public void SetAnchoredPosition(Vector2 position)
        {
            if (cachedRectTransform != null)
            {
                cachedRectTransform.anchoredPosition = position;
            }
        }

        /// <summary>
        /// 由容器根据屏幕可见性调用；与渐显渐隐 alpha 叠加。
        /// </summary>
        public void SetScreenVisible(bool visible)
        {
            if (screenVisible == visible)
            {
                return;
            }

            screenVisible = visible;
            ApplyAlpha();
        }

        public void MarkForRelease()
        {
            if (ShouldRelease || isFadingOut)
            {
                return;
            }

            isFadingOut = true;
            KillFadeTween();
            fadeTween = DOTween.To(() => fadeAlpha, value =>
                {
                    fadeAlpha = value;
                    ApplyAlpha();
                }, 0f, FadeDuration)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    ShouldRelease = true;
                    fadeTween = null;
                });
        }

        private void PlayFadeIn()
        {
            screenVisible = true;
            KillFadeTween();
            fadeAlpha = 0f;
            ApplyAlpha();
            gameObject.SetActive(true);

            fadeTween = DOTween.To(() => fadeAlpha, value =>
                {
                    fadeAlpha = value;
                    ApplyAlpha();
                }, 1f, FadeDuration)
                .SetUpdate(true)
                .OnComplete(() => fadeTween = null);
        }

        private void ApplyAlpha()
        {
            EnsureComponents();
            if (cachedCanvasGroup != null)
            {
                cachedCanvasGroup.alpha = screenVisible ? fadeAlpha : 0f;
                cachedCanvasGroup.blocksRaycasts = false;
                cachedCanvasGroup.interactable = false;
            }
        }

        private void KillFadeTween()
        {
            fadeTween?.Kill();
            fadeTween = null;
        }

        private void EnsureComponents()
        {
            cachedRectTransform ??= GetComponent<RectTransform>();
            cachedCanvasGroup ??= GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            var statusBarRoot = transform.Find("Staus/StausBar") ?? transform.Find("Status/StatusBar");
            staminaBarRoot ??= transform.Find("Staus/StaminaBar")?.gameObject
                               ?? transform.Find("Status/StaminaBar")?.gameObject;

            foodIcon ??= statusBarRoot?.Find("img_FoodIcon")?.GetComponent<Image>();
            if (foodIcon != null)
            {
                foodIcon.enabled = false;
                foodIcon.raycastTarget = false;
                foodIcon.gameObject.SetActive(false);
            }

            progressBackground ??= statusBarRoot?.Find("img_ProgressBg")?.GetComponent<Image>();

            var progressRoot = statusBarRoot?.Find("img_ProgressBg");
            fillGreen ??= progressRoot?.Find("FillGreen")?.GetComponent<Image>()
                          ?? progressRoot?.Find("img_ProgressFill")?.GetComponent<Image>();
            fillRed ??= progressRoot?.Find("FillRed")?.GetComponent<Image>();

            if (fillGreen != null)
            {
                fillGreen.type = Image.Type.Filled;
                fillGreen.fillMethod = Image.FillMethod.Radial360;
                fillGreen.raycastTarget = false;
            }

            if (fillRed != null)
            {
                fillRed.type = Image.Type.Filled;
                fillRed.fillMethod = Image.FillMethod.Radial360;
                fillRed.raycastTarget = false;
            }

            if (progressBackground != null)
            {
                progressBackground.raycastTarget = false;
            }
        }
    }

    /// <summary>
    /// 顾客等待 HUD 状态图标加载；缺失时在 Console 提醒一次。
    /// </summary>
    public static class CustomerWaitHudIconCatalog
    {
        private const string QueueIconPath = "Assets/Res/Resources/UI/CustomerWait/QueueIcon.png";
        private const string OrderIconPath = "Assets/Res/Resources/UI/CustomerWait/OrderIcon.png";
        private const string ServeIconPath = "Assets/Res/Resources/UI/CustomerWait/ServeIcon.png";
        private const string CheckoutIconPath = "Assets/Res/Resources/UI/CustomerWait/CheckoutIcon.png";

        private const string BuiltInQueueIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/customer.png";
        private const string BuiltInOrderIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/dingDan.png";
        private const string VipOrderIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/tuijian.png";
        private const string BuiltInServeIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/红烧肉.png";
        private const string BuiltInCheckoutCoinIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/coin.png";
        private const string BuiltInCheckoutVipIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/checkout.png";

        private static readonly bool[] MissingDedicatedIconLogged = new bool[4];
        private static readonly bool[] MissingIconLogged = new bool[4];

        /// <summary>
        /// 通用点单气泡与点单等待 HUD 共用图标。
        /// </summary>
        public static Sprite LoadOrderIcon()
        {
            return LoadDedicatedIcon(CustomerWaitHudState.WaitingOrder)
                   ?? LoadBuiltInFallbackIcon(CustomerWaitHudState.WaitingOrder);
        }

        /// <summary>
        /// 贵客点单专用图标（猜菜/待点单气泡）。
        /// </summary>
        public static Sprite LoadVipOrderIcon()
        {
            return GameplayResourceStore.LoadAsset<Sprite>(VipOrderIconPath);
        }

        /// <summary>
        /// 按桌位解析点单图标：贵客桌用推荐菜图标，其余用通用点单。
        /// </summary>
        public static Sprite ResolveOrderIcon(TavernSceneManager scene, int tableId)
        {
            if (tableId > 0 && scene != null && scene.TableHasVipCustomer(tableId))
            {
                return LoadVipOrderIcon() ?? LoadOrderIcon();
            }

            return LoadOrderIcon();
        }

        /// <summary>
        /// 结账图标：普客桌 coin，有贵客 checkout。
        /// </summary>
        public static Sprite ResolveCheckoutIcon(TavernSceneManager scene, int tableId)
        {
            var hasVip = tableId > 0 && scene != null && scene.TableHasVipCustomer(tableId);
            var path = hasVip ? BuiltInCheckoutVipIconPath : BuiltInCheckoutCoinIconPath;
            return GameplayResourceStore.LoadAsset<Sprite>(path);
        }

        public static Sprite Resolve(CustomerWaitHudState state, TavernSceneManager scene, int tableId = 0)
        {
            if (state == CustomerWaitHudState.WaitingCheckout)
            {
                return ResolveCheckoutIcon(scene, tableId)
                       ?? LoadBuiltInFallbackIcon(state);
            }

            if (state == CustomerWaitHudState.WaitingOrder
                && tableId > 0
                && scene != null
                && scene.TableHasVipCustomer(tableId))
            {
                return LoadVipOrderIcon() ?? LoadOrderIcon();
            }

            var dedicated = LoadDedicatedIcon(state);
            if (dedicated != null)
            {
                return dedicated;
            }

            var fallback = ResolveSceneFallback(state, scene);
            if (fallback != null)
            {
                LogMissingDedicatedIconOnce(state);
                return fallback;
            }

            var builtIn = LoadBuiltInFallbackIcon(state);
            if (builtIn != null)
            {
                LogMissingDedicatedIconOnce(state);
                return builtIn;
            }

            LogMissingIconOnce(state);
            return null;
        }

        private static Sprite LoadDedicatedIcon(CustomerWaitHudState state)
        {
            var path = GetDedicatedIconPath(state);
            return string.IsNullOrWhiteSpace(path)
                ? null
                : GameplayResourceStore.LoadAsset<Sprite>(path);
        }

        private static Sprite LoadBuiltInFallbackIcon(CustomerWaitHudState state)
        {
            var path = state switch
            {
                CustomerWaitHudState.Queue => BuiltInQueueIconPath,
                CustomerWaitHudState.WaitingOrder => BuiltInOrderIconPath,
                CustomerWaitHudState.WaitingServe => BuiltInServeIconPath,
                CustomerWaitHudState.WaitingCheckout => BuiltInCheckoutCoinIconPath,
                _ => null,
            };

            return string.IsNullOrWhiteSpace(path)
                ? null
                : GameplayResourceStore.LoadAsset<Sprite>(path);
        }

        private static string GetDedicatedIconPath(CustomerWaitHudState state)
        {
            return state switch
            {
                CustomerWaitHudState.Queue => QueueIconPath,
                CustomerWaitHudState.WaitingOrder => OrderIconPath,
                CustomerWaitHudState.WaitingServe => ServeIconPath,
                CustomerWaitHudState.WaitingCheckout => CheckoutIconPath,
                _ => null,
            };
        }

        private static Sprite ResolveSceneFallback(CustomerWaitHudState state, TavernSceneManager scene)
        {
            return scene == null ? null : scene.ResolveCustomerWaitHudFallbackIcon(state);
        }

        private static void LogMissingDedicatedIconOnce(CustomerWaitHudState state)
        {
            var index = GetStateLogIndex(state);
            if (index < 0 || MissingDedicatedIconLogged[index])
            {
                return;
            }

            MissingDedicatedIconLogged[index] = true;
            Debug.LogWarning(
                $"[CustomerWaitHud] 缺少专用图标：{GetDedicatedIconPath(state)}，已临时使用 fallback。");
        }

        private static void LogMissingIconOnce(CustomerWaitHudState state)
        {
            var index = GetStateLogIndex(state);
            if (index < 0 || MissingIconLogged[index])
            {
                return;
            }

            MissingIconLogged[index] = true;
            Debug.LogWarning(
                $"[CustomerWaitHud] 缺少 {state} 状态图标，且未找到可用 fallback：{GetDedicatedIconPath(state)}");
        }

        private static int GetStateLogIndex(CustomerWaitHudState state)
        {
            return (int)state - 1;
        }
    }
}
