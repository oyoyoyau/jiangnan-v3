using System;
using DG.Tweening;
using JN.Client.Manager;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.UI
{
    /// <summary>
    /// 单个 Tavern 世界运行时 HUD 条目。
    /// 支持定时进度、动态进度和带体力的状态图标。
    /// </summary>
    public class TavernWorldRuntimeHudItemView : MonoBehaviour
    {
        private enum RuntimeHudItemMode
        {
            TimedProgress,
            ClickableTimedProgress,
            DynamicProgress,
            ClickableDynamicProgress,
            StateIcon
        }

        private const float DynamicCompleteHoldTime = 0.2f;
        private const float StaminaPulseScaleMultiplier = 1.12f;
        private const float StaminaPulseDuration = 0.45f;
        private const float StaminaFillTweenDuration = 0.2f;

        private RectTransform cachedRectTransform;
        private CanvasGroup cachedCanvasGroup;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Sprite NoClickBg;
        [SerializeField] private Sprite ClickBg;
        [SerializeField] private Image StaminaFill1;
        [SerializeField] private Image StaminaFill2;
        [SerializeField] private Image StaminaFill3;
        [SerializeField] private Image progressBackground;
        [SerializeField] private Image progressFill;
        [SerializeField] private Image foodIcon;
        [SerializeField] private Sprite stealProgressFillSprite;
        private GameObject statusBarRoot;
        private GameObject staminaBarRoot;
        private Button clickButton;
        private Selectable[] cachedSelectables;

        private Transform target;
        private Vector3 worldOffset;
        private RuntimeHudItemMode mode;
        private float duration;
        private float elapsed;
        private float completedHoldTime;
        private Func<float> progressProvider;
        private Action onClick;
        private Tween pulseTween;
        private Tween staminaRecoveryTween;
        private bool isVisible = true;
        private bool autoReleaseOnComplete = true;
        private Sprite defaultProgressFillSprite;
        private Vector3 defaultRootScale = Vector3.one;
        private Vector3 defaultFoodIconScale = Vector3.one;
        private Vector2 defaultFoodIconAnchoredPosition;
        private Vector2 defaultFoodIconSizeDelta;
        private Image[] staminaFills;
        private Tween[] staminaFillTweens;
        private Vector3[] defaultStaminaFillScales;
        private int activeRecoverySegmentIndex = -1;
        private bool rootScaleInitialized;
        private bool foodIconScaleInitialized;
        private bool foodIconLayoutInitialized;
        private bool staminaFillScalesInitialized;
        private bool hasInitializedStaminaDisplay;

        private static readonly Color DefaultProgressBackgroundColor = Color.white;
        private static readonly Color DefaultProgressFillColor = Color.white;
        private static readonly Color StealProgressBackgroundColor = new Color(1f, 0.84f, 0.84f, 1f);
        private static readonly Color StealProgressFillColor = Color.white;

        /// <summary>
        /// 标记当前条目是否应被容器回收。
        /// </summary>
        public bool ShouldRelease { get; private set; }

        /// <summary>
        /// 初始化运行时依赖。
        /// </summary>
        private void Awake()
        {
            EnsureComponents();
        }

        private void OnDestroy()
        {
            KillPulseTween();
            KillStaminaRecoveryTween();
            KillStaminaFillTweens();
        }

        /// <summary>
        /// 更新条目的跟随目标和偏移。
        /// </summary>
        public void BindTarget(Transform followTarget, Vector3 offset)
        {
            target = followTarget;
            worldOffset = offset;
        }

        /// <summary>
        /// 配置为定时完成的进度条。
        /// </summary>
        public void ConfigureTimedProgress(Transform followTarget, float progressDuration, Vector3 offset, Sprite icon)
        {
            ConfigureTimedProgressInternal(followTarget, progressDuration, offset, icon, true);
        }

        public void ConfigurePersistentTimedProgress(Transform followTarget, float progressDuration, Vector3 offset, Sprite icon)
        {
            ConfigureTimedProgressInternal(followTarget, progressDuration, offset, icon, false);
        }

        private void ConfigureTimedProgressInternal(Transform followTarget, float progressDuration, Vector3 offset, Sprite icon, bool shouldAutoRelease)
        {
            EnsureComponents();
            ResetVisualState();
            BindTarget(followTarget, offset);
            mode = RuntimeHudItemMode.TimedProgress;
            duration = Mathf.Max(0.1f, progressDuration);
            elapsed = 0f;
            completedHoldTime = DynamicCompleteHoldTime;
            progressProvider = null;
            onClick = null;
            autoReleaseOnComplete = shouldAutoRelease;
            ShouldRelease = false;

            ConfigureIcon(icon);
            SetStatusBarVisible(true);
            SetBackgroundVisible(true);
            SetProgressMode(true);
            // 厨师做菜等定时进度只显示进度条，不显示体力条
            SetStaminaVisible(false);
            SetClickHandler(null);

            if (progressFill != null)
            {
                progressFill.fillAmount = 0f;
                progressFill.raycastTarget = false;
            }

            if (backgroundImage != null)
            {
                backgroundImage.sprite = NoClickBg;
            }
        }

        /// <summary>
        /// 配置为可点击的定时进度条。
        /// </summary>
        public void ConfigureClickableTimedProgress(Transform followTarget, float progressDuration, Vector3 offset, Sprite icon, Action clickAction)
        {
            ConfigureClickableTimedProgressInternal(followTarget, progressDuration, offset, icon, clickAction, true);
        }

        public void ConfigurePersistentClickableTimedProgress(Transform followTarget, float progressDuration, Vector3 offset, Sprite icon, Action clickAction)
        {
            ConfigureClickableTimedProgressInternal(followTarget, progressDuration, offset, icon, clickAction, false);
        }

        private void ConfigureClickableTimedProgressInternal(Transform followTarget, float progressDuration, Vector3 offset, Sprite icon, Action clickAction, bool shouldAutoRelease)
        {
            EnsureComponents();
            ResetVisualState();
            BindTarget(followTarget, offset);
            mode = RuntimeHudItemMode.ClickableTimedProgress;
            duration = Mathf.Max(0.1f, progressDuration);
            elapsed = 0f;
            completedHoldTime = 0f;
            progressProvider = null;
            onClick = clickAction;
            autoReleaseOnComplete = shouldAutoRelease;
            ShouldRelease = false;

            ConfigureIcon(icon);
            SetStatusBarVisible(true);
            SetBackgroundVisible(true);
            SetProgressMode(true);
            SetStaminaVisible(true);
            SetClickHandler(clickAction);

            if (progressFill != null)
            {
                progressFill.fillAmount = 0f;
                progressFill.raycastTarget = false;
            }

            if (backgroundImage != null)
            {
                backgroundImage.sprite = ClickBg;
            }

            ApplyStealProgressStyle();
            StartPulseTween();
        }

        /// <summary>
        /// 配置为由外部回调驱动的进度条。
        /// </summary>
        public void ConfigureDynamicProgress(Transform followTarget, Func<float> provider, Vector3 offset, Sprite icon)
        {
            ConfigureDynamicProgressInternal(followTarget, provider, offset, icon, true);
        }

        public void ConfigurePersistentDynamicProgress(Transform followTarget, Func<float> provider, Vector3 offset, Sprite icon)
        {
            ConfigureDynamicProgressInternal(followTarget, provider, offset, icon, false);
            // 掌柜点单进度只显示进度条，不显示体力条。
            SetStaminaVisible(false);
        }

        private void ConfigureDynamicProgressInternal(Transform followTarget, Func<float> provider, Vector3 offset, Sprite icon, bool shouldAutoRelease)
        {
            EnsureComponents();
            ResetVisualState();
            BindTarget(followTarget, offset);
            mode = RuntimeHudItemMode.DynamicProgress;
            duration = 0f;
            elapsed = 0f;
            completedHoldTime = DynamicCompleteHoldTime;
            progressProvider = provider;
            onClick = null;
            autoReleaseOnComplete = shouldAutoRelease;
            ShouldRelease = false;

            ConfigureIcon(icon);
            SetStatusBarVisible(true);
            SetBackgroundVisible(true);
            SetProgressMode(true);
            SetStaminaVisible(true);
            SetClickHandler(null);

            if (progressFill != null)
            {
                progressFill.fillAmount = 0f;
                progressFill.raycastTarget = false;
            }
        }

        /// <summary>
        /// 配置为由外部回调驱动且可点击的进度条。
        /// </summary>
        public void ConfigureClickableDynamicProgress(Transform followTarget, Func<float> provider, Vector3 offset, Sprite icon, Action clickAction)
        {
            ConfigureClickableDynamicProgressInternal(followTarget, provider, offset, icon, clickAction, true);
        }

        public void ConfigurePersistentClickableDynamicProgress(Transform followTarget, Func<float> provider, Vector3 offset, Sprite icon, Action clickAction)
        {
            ConfigureClickableDynamicProgressInternal(followTarget, provider, offset, icon, clickAction, false);
        }

        private void ConfigureClickableDynamicProgressInternal(Transform followTarget, Func<float> provider, Vector3 offset, Sprite icon, Action clickAction, bool shouldAutoRelease)
        {
            EnsureComponents();
            ResetVisualState();
            BindTarget(followTarget, offset);
            mode = RuntimeHudItemMode.ClickableDynamicProgress;
            duration = 0f;
            elapsed = 0f;
            completedHoldTime = 0f;
            progressProvider = provider;
            onClick = clickAction;
            autoReleaseOnComplete = shouldAutoRelease;
            ShouldRelease = false;

            ConfigureIcon(icon);
            SetStatusBarVisible(true);
            SetBackgroundVisible(true);
            SetProgressMode(true);
            SetStaminaVisible(true);
            SetClickHandler(clickAction);

            if (progressFill != null)
            {
                progressFill.fillAmount = 0f;
                progressFill.raycastTarget = false;
            }

            ApplyStealProgressStyle();
            StartPulseTween();
        }

        /// <summary>
        /// 配置为可点击的状态图标。
        /// </summary>
        public void ConfigureStateIcon(
            Transform followTarget,
            Sprite icon,
            Action clickAction,
            Vector3 offset,
            float currentStamina = 0f,
            float maxStamina = 0f,
            bool isRecovering = false)
        {
            EnsureComponents();
            ResetVisualState();
            BindTarget(followTarget, offset);
            mode = RuntimeHudItemMode.StateIcon;
            duration = 0f;
            elapsed = 0f;
            completedHoldTime = 0f;
            progressProvider = null;
            onClick = clickAction;
            autoReleaseOnComplete = false;
            ShouldRelease = false;

            ConfigureIcon(icon);
            SetStatusBarVisible(true);
            SetBackgroundVisible(true);
            SetProgressMode(false);
            // 灶台待上菜气泡等状态图标不需要体力条
            SetStaminaVisible(false);
            SetClickHandler(clickAction);

            if (backgroundImage != null)
            {
                backgroundImage.sprite = clickAction != null ? ClickBg : NoClickBg;
            }
        }

        /// <summary>
        /// 刷新状态图标、体力值和恢复表现。
        /// </summary>
        /// <param name="useNativeSizeIcon">为真时图标 SetNativeSize（踢小二 kick 图标）。</param>
        /// <param name="iconAnchoredY">覆盖图标本地 Y；null 则用预制体默认。</param>
        public void RefreshWaiterStateHud(
            Sprite icon,
            Action clickAction,
            float currentStamina,
            float maxStamina,
            bool isRecovering,
            bool preserveProgress = false,
            bool showStamina = false,
            bool useNativeSizeIcon = false,
            float? iconAnchoredY = null)
        {
            EnsureComponents();
            if (!preserveProgress)
            {
                mode = RuntimeHudItemMode.StateIcon;
                autoReleaseOnComplete = false;
                progressProvider = null;
                duration = 0f;
                elapsed = 0f;
                completedHoldTime = 0f;
            }

            ConfigureIcon(icon, useNativeSizeIcon, iconAnchoredY);
            SetStatusBarVisible(true);
            SetBackgroundVisible(true);
            SetProgressMode(preserveProgress && mode != RuntimeHudItemMode.StateIcon);
            SetStaminaVisible(showStamina);
            SetClickHandler(clickAction);
            if (showStamina)
            {
                UpdateStaminaFills(currentStamina, maxStamina);
                SetStaminaRecoveryVisual(ResolveRecoverySegmentIndex(currentStamina, maxStamina, isRecovering));
            }

            if (backgroundImage != null)
            {
                backgroundImage.sprite = clickAction != null ? ClickBg : NoClickBg;
            }
        }

        public void RefreshWaiterStamina(float currentStamina, float maxStamina, bool isRecovering)
        {
            EnsureComponents();
            SetStatusBarVisible(true);
            SetStaminaVisible(true);
            UpdateStaminaFills(currentStamina, maxStamina);
            SetStaminaRecoveryVisual(ResolveRecoverySegmentIndex(currentStamina, maxStamina, isRecovering));
        }

        public void HideWaiterStatusBar()
        {
            EnsureComponents();
            SetStatusBarVisible(false);
            SetClickHandler(null);
            SetProgressMode(false);
            SetStaminaVisible(false);
        }

        /// <summary>
        /// 按当前模式推进进度并判断是否需要回收。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (ShouldRelease)
            {
                return;
            }

            if (target == null)
            {
                ShouldRelease = true;
                return;
            }

            switch (mode)
            {
                case RuntimeHudItemMode.TimedProgress:
                case RuntimeHudItemMode.ClickableTimedProgress:
                    elapsed += deltaTime;
                    if (progressFill != null)
                    {
                        progressFill.fillAmount = Mathf.Clamp01(elapsed / duration);
                    }

                    if (elapsed >= duration)
                    {
                        ShouldRelease = autoReleaseOnComplete;
                    }

                    break;
                case RuntimeHudItemMode.DynamicProgress:
                    var dynamicProgress = Mathf.Clamp01(progressProvider?.Invoke() ?? 0f);
                    if (progressFill != null)
                    {
                        progressFill.fillAmount = dynamicProgress;
                    }

                    if (dynamicProgress >= 1f)
                    {
                        completedHoldTime -= deltaTime;
                        if (completedHoldTime <= 0f)
                        {
                            ShouldRelease = autoReleaseOnComplete;
                        }
                    }
                    else
                    {
                        completedHoldTime = DynamicCompleteHoldTime;
                    }

                    break;
                case RuntimeHudItemMode.ClickableDynamicProgress:
                    var clickableDynamicProgress = Mathf.Clamp01(progressProvider?.Invoke() ?? 0f);
                    if (progressFill != null)
                    {
                        progressFill.fillAmount = clickableDynamicProgress;
                    }

                    break;
            }
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
                cachedCanvasGroup.blocksRaycasts = visible && onClick != null;
                cachedCanvasGroup.interactable = visible && onClick != null;
            }
            else
            {
                gameObject.SetActive(visible);
            }

            if (!visible)
            {
                KillPulseTween();
            }
            else if (mode == RuntimeHudItemMode.ClickableTimedProgress || mode == RuntimeHudItemMode.ClickableDynamicProgress)
            {
                StartPulseTween();
            }
        }

        /// <summary>
        /// 缓存本条目依赖的组件。
        /// </summary>
        private void EnsureComponents()
        {
            cachedRectTransform ??= GetComponent<RectTransform>();
            cachedCanvasGroup ??= GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            clickButton ??= GetComponent<Button>();
            cachedSelectables ??= GetComponents<Selectable>();
            statusBarRoot ??= transform.Find("Staus/StausBar")?.gameObject ?? transform.Find("Status/StatusBar")?.gameObject;
            staminaBarRoot ??= transform.Find("Staus/StaminaBar")?.gameObject ?? transform.Find("Status/StaminaBar")?.gameObject;
            backgroundImage ??= transform.Find("img_Bg")?.GetComponent<Image>();
            foodIcon ??= transform.Find("img_FoodIcon")?.GetComponent<Image>();
            progressBackground ??= transform.Find("img_ProgressBg")?.GetComponent<Image>();
            progressFill ??= transform.Find("img_ProgressBg/img_ProgressFill")?.GetComponent<Image>();
            staminaFills ??= new[] { StaminaFill1, StaminaFill2, StaminaFill3 };
            staminaFillTweens ??= new Tween[staminaFills.Length];
            defaultStaminaFillScales ??= new Vector3[staminaFills.Length];
            if (!rootScaleInitialized && cachedRectTransform != null)
            {
                defaultRootScale = cachedRectTransform.localScale;
                rootScaleInitialized = true;
            }

            if (!foodIconScaleInitialized && foodIcon != null)
            {
                defaultFoodIconScale = foodIcon.rectTransform.localScale;
                foodIconScaleInitialized = true;
            }

            if (!foodIconLayoutInitialized && foodIcon != null)
            {
                defaultFoodIconAnchoredPosition = foodIcon.rectTransform.anchoredPosition;
                defaultFoodIconSizeDelta = foodIcon.rectTransform.sizeDelta;
                foodIconLayoutInitialized = true;
            }

            defaultProgressFillSprite ??= progressFill != null ? progressFill.sprite : null;

            for (var index = 0; index < staminaFills.Length; index++)
            {
                var fill = staminaFills[index];
                if (fill == null)
                {
                    continue;
                }

                if (!staminaFillScalesInitialized)
                {
                    defaultStaminaFillScales[index] = fill.rectTransform.localScale;
                }

                fill.raycastTarget = false;
            }

            staminaFillScalesInitialized = true;

            NormalizeSelectableVisuals();
        }

        /// <summary>
        /// 设置条目图标。
        /// </summary>
        /// <param name="useNativeSize">为真时按精灵原始像素设尺寸（踢小二）。</param>
        /// <param name="iconAnchoredY">覆盖本地 Y；null 恢复预制体默认 Y。</param>
        private void ConfigureIcon(Sprite icon, bool useNativeSize = false, float? iconAnchoredY = null)
        {
            if (foodIcon != null)
            {
                foodIcon.enabled = icon != null;
                if (icon != null)
                {
                    foodIcon.sprite = icon;
                    if (useNativeSize)
                    {
                        foodIcon.SetNativeSize();
                    }
                    else if (foodIconLayoutInitialized)
                    {
                        foodIcon.rectTransform.sizeDelta = defaultFoodIconSizeDelta;
                    }

                    if (foodIconLayoutInitialized)
                    {
                        var pos = foodIcon.rectTransform.anchoredPosition;
                        pos.x = defaultFoodIconAnchoredPosition.x;
                        pos.y = iconAnchoredY ?? defaultFoodIconAnchoredPosition.y;
                        foodIcon.rectTransform.anchoredPosition = pos;
                    }
                }

                foodIcon.color = Color.white;
                foodIcon.raycastTarget = onClick != null;
                foodIcon.transform.SetAsLastSibling();
            }

            if (backgroundImage != null)
            {
                backgroundImage.color = Color.white;
                backgroundImage.raycastTarget = false;
                backgroundImage.transform.SetAsFirstSibling();
            }

            if (progressBackground != null)
            {
                progressBackground.color = Color.white;
                progressBackground.raycastTarget = false;
            }

            if (progressFill != null)
            {
                progressFill.color = Color.white;
                progressFill.raycastTarget = false;
            }
        }

        private void ResetVisualState()
        {
            KillPulseTween();
            KillStaminaRecoveryTween();
            KillStaminaFillTweens();
            if (cachedRectTransform != null)
            {
                cachedRectTransform.localScale = defaultRootScale;
            }

            if (foodIcon != null)
            {
                foodIcon.rectTransform.localScale = defaultFoodIconScale;
                if (foodIconLayoutInitialized)
                {
                    foodIcon.rectTransform.anchoredPosition = defaultFoodIconAnchoredPosition;
                    foodIcon.rectTransform.sizeDelta = defaultFoodIconSizeDelta;
                }
            }

            for (var index = 0; index < staminaFills.Length; index++)
            {
                var fill = staminaFills[index];
                if (fill == null)
                {
                    continue;
                }

                fill.rectTransform.localScale = defaultStaminaFillScales[index];
            }

            activeRecoverySegmentIndex = -1;
            ApplyDefaultProgressStyle();
        }

        private void ApplyDefaultProgressStyle()
        {
            if (progressBackground != null)
            {
                progressBackground.color = DefaultProgressBackgroundColor;
            }

            if (progressFill != null)
            {
                if (defaultProgressFillSprite != null)
                {
                    progressFill.sprite = defaultProgressFillSprite;
                }

                progressFill.color = DefaultProgressFillColor;
            }
        }

        private void ApplyStealProgressStyle()
        {
            if (progressBackground != null)
            {
                progressBackground.color = StealProgressBackgroundColor;
            }

            if (progressFill != null)
            {
                if (stealProgressFillSprite != null)
                {
                    progressFill.sprite = stealProgressFillSprite;
                }

                progressFill.color = StealProgressFillColor;
            }
        }

        private void UpdateStaminaFills(float currentStamina, float maxStamina)
        {
            if (staminaFills == null || staminaFills.Length == 0)
            {
                return;
            }

            var safeMaxStamina = Mathf.Max(0.0001f, maxStamina);
            var clampedStamina = Mathf.Clamp(currentStamina, 0f, safeMaxStamina);
            var segmentCapacity = safeMaxStamina / staminaFills.Length;
            var remainingStamina = clampedStamina;

            for (var index = 0; index < staminaFills.Length; index++)
            {
                var fill = staminaFills[index];
                if (fill == null)
                {
                    continue;
                }

                var segmentValue = Mathf.Clamp(remainingStamina, 0f, segmentCapacity);
                var targetFillAmount = segmentCapacity <= 0.0001f ? 0f : Mathf.Clamp01(segmentValue / segmentCapacity);
                if (hasInitializedStaminaDisplay)
                {
                    AnimateStaminaFill(index, fill, targetFillAmount);
                }
                else
                {
                    KillStaminaFillTween(index);
                    fill.fillAmount = targetFillAmount;
                }

                remainingStamina = Mathf.Max(0f, remainingStamina - segmentCapacity);
            }

            hasInitializedStaminaDisplay = true;
        }

        private void AnimateStaminaFill(int index, Image fill, float targetFillAmount)
        {
            if (fill == null)
            {
                return;
            }

            KillStaminaFillTween(index);
            if (!isActiveAndEnabled || Mathf.Abs(fill.fillAmount - targetFillAmount) <= 0.001f)
            {
                fill.fillAmount = targetFillAmount;
                return;
            }

            staminaFillTweens[index] = fill
                .DOFillAmount(targetFillAmount, StaminaFillTweenDuration)
                .SetEase(Ease.OutCubic)
                .OnKill(() =>
                {
                    if (staminaFillTweens != null && index >= 0 && index < staminaFillTweens.Length)
                    {
                        staminaFillTweens[index] = null;
                    }
                });
        }

        private int ResolveRecoverySegmentIndex(float currentStamina, float maxStamina, bool isRecovering)
        {
            if (!isRecovering || staminaFills == null || staminaFills.Length == 0)
            {
                return -1;
            }

            var safeMaxStamina = Mathf.Max(0.0001f, maxStamina);
            if (currentStamina >= safeMaxStamina - 0.0001f)
            {
                return -1;
            }

            var segmentCapacity = safeMaxStamina / staminaFills.Length;
            if (segmentCapacity <= 0.0001f)
            {
                return -1;
            }

            var clampedStamina = Mathf.Clamp(currentStamina, 0f, safeMaxStamina - 0.0001f);
            var segmentIndex = Mathf.FloorToInt(clampedStamina / segmentCapacity);
            return Mathf.Clamp(segmentIndex, 0, staminaFills.Length - 1);
        }

        private void SetStaminaRecoveryVisual(int segmentIndex)
        {
            if (activeRecoverySegmentIndex == segmentIndex
                && (segmentIndex < 0 || (staminaRecoveryTween != null && staminaRecoveryTween.IsActive())))
            {
                return;
            }

            KillStaminaRecoveryTween();
            activeRecoverySegmentIndex = segmentIndex;
            if (segmentIndex < 0 || staminaFills == null || segmentIndex >= staminaFills.Length)
            {
                return;
            }

            var targetFill = staminaFills[segmentIndex];
            if (targetFill == null)
            {
                return;
            }

            var rectTransform = targetFill.rectTransform;
            rectTransform.localScale = defaultStaminaFillScales[segmentIndex];
            staminaRecoveryTween = rectTransform
                .DOScale(defaultStaminaFillScales[segmentIndex] * StaminaPulseScaleMultiplier, StaminaPulseDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        private void StartPulseTween()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            KillPulseTween();
            if (cachedRectTransform == null)
            {
                return;
            }

            cachedRectTransform.localScale = defaultRootScale;
            if (foodIcon != null)
            {
                foodIcon.rectTransform.localScale = defaultFoodIconScale;
            }

            var sequence = DOTween.Sequence();
            sequence.Append(cachedRectTransform.DOScale(defaultRootScale * 1.18f, 0.42f).SetEase(Ease.InOutSine));
            if (foodIcon != null)
            {
                sequence.Join(foodIcon.rectTransform.DOScale(defaultFoodIconScale * 1.08f, 0.42f).SetEase(Ease.InOutSine));
            }

            pulseTween = sequence.SetLoops(-1, LoopType.Yoyo);
        }

        private void KillPulseTween()
        {
            if (pulseTween == null)
            {
                return;
            }

            if (pulseTween.IsActive())
            {
                pulseTween.Kill();
            }

            pulseTween = null;
        }

        private void KillStaminaRecoveryTween()
        {
            if (staminaRecoveryTween != null && staminaRecoveryTween.IsActive())
            {
                staminaRecoveryTween.Kill();
            }

            staminaRecoveryTween = null;
            if (staminaFills == null || defaultStaminaFillScales == null)
            {
                return;
            }

            for (var index = 0; index < staminaFills.Length; index++)
            {
                var fill = staminaFills[index];
                if (fill == null)
                {
                    continue;
                }

                fill.rectTransform.localScale = defaultStaminaFillScales[index];
            }
        }

        private void KillStaminaFillTweens()
        {
            if (staminaFillTweens == null)
            {
                return;
            }

            for (var index = 0; index < staminaFillTweens.Length; index++)
            {
                KillStaminaFillTween(index);
            }
        }

        private void KillStaminaFillTween(int index)
        {
            if (staminaFillTweens == null || index < 0 || index >= staminaFillTweens.Length)
            {
                return;
            }

            var tween = staminaFillTweens[index];
            if (tween != null && tween.IsActive())
            {
                tween.Kill();
            }

            staminaFillTweens[index] = null;
        }

        private void SetStatusBarVisible(bool visible)
        {
            if (statusBarRoot != null)
            {
                statusBarRoot.SetActive(visible);
            }
        }

        /// <summary>
        /// 控制底板显隐。
        /// </summary>
        private void SetBackgroundVisible(bool visible)
        {
            if (backgroundImage != null)
            {
                backgroundImage.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 控制进度条区域显隐。
        /// </summary>
        private void SetProgressMode(bool visible)
        {
            if (progressBackground != null)
            {
                progressBackground.gameObject.SetActive(visible);
            }
        }

        private void SetStaminaVisible(bool visible)
        {
            EnsureComponents();
            if (staminaBarRoot != null)
            {
                staminaBarRoot.SetActive(visible);
            }

            if (visible || staminaFills == null)
            {
                return;
            }

            KillStaminaRecoveryTween();
            for (var index = 0; index < staminaFills.Length; index++)
            {
                var fill = staminaFills[index];
                if (fill != null)
                {
                    KillStaminaFillTween(index);
                    fill.fillAmount = 0f;
                }
            }

            hasInitializedStaminaDisplay = false;
        }

        /// <summary>
        /// 统一关闭残留 Selectable 的颜色过渡，避免图标进入灰态。
        /// </summary>
        private void NormalizeSelectableVisuals()
        {
            if (cachedSelectables == null)
            {
                return;
            }

            for (var index = 0; index < cachedSelectables.Length; index++)
            {
                var selectable = cachedSelectables[index];
                if (selectable == null)
                {
                    continue;
                }

                selectable.transition = Selectable.Transition.None;
                if (selectable != clickButton)
                {
                    selectable.targetGraphic = null;
                    selectable.interactable = false;
                    selectable.enabled = false;
                }
            }
        }

        /// <summary>
        /// 绑定或清理点击事件。
        /// </summary>
        private void SetClickHandler(Action clickAction)
        {
            EnsureComponents();
            onClick = clickAction;

            if (clickButton == null && clickAction != null)
            {
                clickButton = gameObject.AddComponent<Button>();
            }

            if (clickButton != null)
            {
                clickButton.onClick.RemoveAllListeners();
                clickButton.transition = Selectable.Transition.None;
                clickButton.targetGraphic = clickAction != null ? foodIcon : null;
                clickButton.interactable = clickAction != null;
                clickButton.enabled = clickAction != null;

                if (clickAction != null)
                {
                    clickButton.onClick.AddListener(InvokeClickAction);
                }
            }

            NormalizeSelectableVisuals();

            if (foodIcon != null)
            {
                foodIcon.raycastTarget = clickAction != null;
            }

            if (cachedCanvasGroup != null)
            {
                cachedCanvasGroup.blocksRaycasts = clickAction != null;
                cachedCanvasGroup.interactable = clickAction != null;
            }
        }

        private void InvokeClickAction()
        {
            if (onClick == null)
            {
                return;
            }

            GameAudioManager.PlayButtonClick();
            onClick.Invoke();
        }
    }
}
