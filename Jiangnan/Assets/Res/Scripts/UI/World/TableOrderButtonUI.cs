using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using JN.Client.Manager;
using JN.Client.Scene;
using JN.Client.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class TableOrderButtonUI : MonoBehaviour
{
    private const string CheckoutCoinIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/coin.png";
    private const string CheckoutVipIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/checkout.png";
    private const float CheckoutPulseScaleFactor = 1.08f;

    public enum State
    {
        WaitingForOrder,
        WaitingForServe,
        InProgress,
        ReadyToClaim,
        CompactCheckoutReady,
        WarningSkipFee
    }

    [Header("Screen Clamping")]
    [SerializeField] private float minX = -417f;
    [SerializeField] private float maxX = 418f;
    [SerializeField] private float minY = -850f;
    [SerializeField] private float maxY = 850f;

    [Header("Skip Fee Visuals")]
    [SerializeField] private Sprite warningIcon;
    [SerializeField] private Sprite catchingIcon;
    [SerializeField] private Sprite catchResultIcon;
    [SerializeField] private Sprite checkoutCoinIcon;
    [SerializeField] private GameObject warning;
    [SerializeField] private TMP_Text timerText;

    [Header("Refs")]
    [SerializeField] private Image progressImage;
    [SerializeField] private Image glowImage;
    [SerializeField] private Image icon;
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text dishText;

    public State CurrentState => state;

    private TableArea boundTable;
    private State state = State.WaitingForOrder;
    private System.Action externalClickHandler;
    private Coroutine progressRoutine;
    private Tween pulseTween;
    private Tween iconTween;
    private Sprite defaultIcon;
    private bool displayOnly;
    private Vector3 defaultLocalScale = Vector3.one;
    /// <summary>世界跟随挂载时跳过屏幕坐标钳制（由父节点定位）。</summary>
    private bool skipScreenClamp;

    /// <summary>
    /// 初始化组件引用和运行时状态。
    /// </summary>
    private void Awake()
    {
        defaultLocalScale = transform.localScale;

        if (icon != null)
        {
            defaultIcon = icon.sprite;
        }

        if (button != null)
        {
            button.onClick.AddListener(OnClick);
        }

        ResetVisuals();
    }

    /// <summary>
    /// 销毁时释放监听、协程和运行时缓存。
    /// </summary>
    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnClick);
        }

        KillTweens();
    }

    /// <summary>
    /// 在帧末同步跟随 界面 和场景表现位置。
    /// </summary>
    private void LateUpdate()
    {
        if (skipScreenClamp)
        {
            return;
        }

        var rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            return;
        }

        var anchoredPosition = rectTransform.anchoredPosition;
        anchoredPosition.x = Mathf.Clamp(anchoredPosition.x, minX, maxX);
        anchoredPosition.y = Mathf.Clamp(anchoredPosition.y, minY, maxY);
        rectTransform.anchoredPosition = anchoredPosition;
    }

    /// <summary>
    /// 初始化模块依赖和默认状态。
    /// </summary>
    /// <param name="table">桌位对象。</param>
    public void Init(TableArea table)
    {
        boundTable = table;
        externalClickHandler = null;
    }

    /// <summary>
    /// 显示等待点单状态。
    /// </summary>
    /// <param name="productIcon">参数值。</param>
    /// <param name="canServe">参数值。</param>
    public void ShowWaitingForOrder(Sprite productIcon, bool canServe)
    {
        displayOnly = false;
        state = State.WaitingForOrder;
        ResetVisuals();
        SetIcon(canServe ? productIcon : warningIcon);

        if (!canServe)
        {
            if (warning != null)
            {
                warning.SetActive(true);
            }

            if (timerText != null)
            {
                timerText.gameObject.SetActive(true);
                timerText.text = "缺菜";
            }

            return;
        }

        StartGlowLoop();
    }

    /// <summary>
    /// 显示等待上菜、可点击派发状态。
    /// </summary>
    public void ShowWaitingForServe(Sprite productIcon)
    {
        displayOnly = false;
        externalClickHandler = null;
        state = State.WaitingForServe;
        ResetVisuals();
        SetIcon(productIcon);
        StartGlowLoop();
    }

    /// <summary>
    /// 世界 HUD 上菜按钮：复用 NewOrderBtn 外观，由外部回调处理点击。
    /// </summary>
    public void ShowWorldServeAction(Sprite productIcon, System.Action onClick)
    {
        displayOnly = false;
        state = State.WaitingForServe;
        ResetVisuals();
        externalClickHandler = onClick;
        SetIcon(productIcon);
        StartGlowLoop();
        ButtonPressScale.EnsureAttached(gameObject);
    }

    /// <summary>
    /// 二楼点单：显示「上大众菜 / 上招牌菜」。一楼点单不传文案则保持隐藏。
    /// </summary>
    public void SetDishCaption(string caption)
    {
        var text = ResolveDishText();
        if (text == null)
        {
            return;
        }

        var show = !string.IsNullOrWhiteSpace(caption);
        text.gameObject.SetActive(show);
        if (show)
        {
            text.text = caption;
        }
    }

    /// <summary>
    /// 切换菜单时只改图标和菜名，不打断点击与发光。
    /// </summary>
    public void ApplyMenuOrderVisual(Sprite productIcon, string dishCaption)
    {
        if (productIcon != null)
        {
            SetIcon(productIcon);
        }

        if (dishCaption != null)
        {
            SetDishCaption(dishCaption);
        }
    }

    /// <summary>
    /// 贵客菜单点单按钮呼吸缩放；大众菜单关闭。重复开启会先停掉局部发光再整颗缩放。
    /// </summary>
    public void SetBreathingEnabled(bool enabled)
    {
        if (enabled)
        {
            KillTweens();
            StartPulseLoop();
            return;
        }

        KillTweens();
    }

    private TMP_Text ResolveDishText()
    {
        if (dishText != null)
        {
            return dishText;
        }

        var child = transform.Find("txt_dish");
        if (child != null)
        {
            dishText = child.GetComponent<TMP_Text>();
            return dishText;
        }

        var texts = GetComponentsInChildren<TMP_Text>(true);
        for (var i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].name == "txt_dish")
            {
                dishText = texts[i];
                break;
            }
        }

        return dishText;
    }

    /// <summary>
    /// 上楼按钮：只绑点击，并确保 DrumUp 文案显示为「上楼」（Awake 的 ResetVisuals 会藏掉）。
    /// </summary>
    public void ShowUpStairAction(System.Action onClick)
    {
        BindExternalClickOnly(onClick);
        if (timerText == null)
        {
            return;
        }

        timerText.gameObject.SetActive(true);
        timerText.text = "上楼";
    }

    /// <summary>
    /// 仅绑定外部点击，不改图标/文案/显隐（用于已配好外观的预制体）。
    /// </summary>
    public void BindExternalClickOnly(System.Action onClick)
    {
        displayOnly = false;
        skipScreenClamp = true;
        state = State.WaitingForServe;
        KillTweens();
        externalClickHandler = onClick;

        if (warning != null)
        {
            warning.SetActive(false);
        }

        if (button != null)
        {
            button.enabled = true;
            button.interactable = true;
        }

        transform.localScale = defaultLocalScale;
    }

    /// <summary>
    /// 拜访拉客按钮：保留预制体「拉客」文案与图标，仅绑定点击。
    /// 容量不足置灰由 DrumUpBtn 专用逻辑处理，不在此改通用 TableOrderButtonUI 外观。
    /// </summary>
    /// <param name="capacityInsufficient">容量不够（占位参数，外观由外部 Apply）。</param>
    public void ShowDrumUpPullAction(System.Action onClick, bool capacityInsufficient = false)
    {
        displayOnly = false;
        skipScreenClamp = true;
        state = State.WaitingForServe;
        KillTweens();
        externalClickHandler = onClick;

        if (warning != null)
        {
            warning.SetActive(false);
        }

        // DrumUpBtn 把「拉客」文案挂在 timerText 上，不能走 ResetVisuals（会 SetActive(false)）。
        if (timerText != null)
        {
            timerText.gameObject.SetActive(true);
            if (string.IsNullOrWhiteSpace(timerText.text))
            {
                timerText.text = "拉客";
            }
        }

        if (icon != null && icon.sprite == null && defaultIcon != null)
        {
            icon.sprite = defaultIcon;
        }

        if (button != null)
        {
            button.enabled = true;
            button.interactable = true;
        }

        transform.localScale = defaultLocalScale;
        // 保持可点出 tips；置灰视觉见 ApplyVisitDrumUpBtnCapacityVisual。
        _ = capacityInsufficient;
    }

    /// <summary>
    /// 兼容旧调用：DrumUpBtn 置灰已外移，此处仅保证可点击。
    /// </summary>
    public void SetDrumUpCapacityInsufficient(bool insufficient)
    {
        _ = insufficient;
        var canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        if (button != null)
        {
            button.interactable = true;
        }
    }

    /// <summary>
    /// 只显示等待点单状态，不允许玩家点击推进流程。
    /// </summary>
    /// <param name="productIcon">菜品图标。</param>
    public void ShowWaitingForOrderDisplayOnly(Sprite productIcon)
    {
        state = State.WaitingForOrder;
        ResetVisuals();
        displayOnly = true;
        if (button != null)
        {
            button.enabled = false;
        }

        SetIcon(productIcon);
        StartGlowLoop();
    }

    /// <summary>
    /// 显示顾客用餐中的按钮状态。
    /// </summary>
    /// <param name="duration">持续时间。</param>
    /// <param name="productIcon">参数值。</param>
    public void ShowDining(float duration, Sprite productIcon)
    {
        displayOnly = false;
        if (state == State.InProgress)
        {
            return;
        }

        state = State.InProgress;
        ResetVisuals();
        SetIcon(productIcon);

        if (progressImage != null && progressImage.transform.parent != null)
        {
            progressImage.transform.parent.gameObject.SetActive(true);
            progressImage.fillAmount = 0f;
        }

        if (timerText != null)
        {
            timerText.gameObject.SetActive(false);
            timerText.text = string.Empty;
        }

        progressRoutine = StartCoroutine(DiningProgressRoutine(duration));
    }

    /// <summary>
    /// 显示可领取收益状态。
    /// </summary>
    public void ShowReadyToClaim(bool playPulseAnimation = true)
    {
        displayOnly = false;
        state = State.ReadyToClaim;
        ResetVisuals();
        SetIcon(GetCheckoutCoinIcon());
        if (playPulseAnimation)
        {
            StartPulseLoop();
        }
    }

    /// <summary>
    /// 103 后结账进行中：可点击收账气泡（尺寸由预制体控制）。
    /// </summary>
    public void ShowCompactCheckoutReady()
    {
        displayOnly = false;
        state = State.CompactCheckoutReady;
        ResetVisuals();
        SetIcon(GetCheckoutCoinIcon());
    }

    /// <summary>
    /// 播放缺菜警告提示。
    /// </summary>
    public void FlashNoDishWarning()
    {
        displayOnly = false;
        state = State.WarningSkipFee;
        ResetVisuals();
        SetIcon(warningIcon != null ? warningIcon : catchingIcon);

        if (warning != null)
        {
            warning.SetActive(true);
        }

        if (timerText != null)
        {
            timerText.gameObject.SetActive(true);
            timerText.text = "缺菜";
        }

        transform.DOPunchScale(new Vector3(0.12f, 0.12f, 0.12f), 0.3f, 6, 0.4f);
    }

    /// <summary>
    /// 重置按钮图标、文字和特效显示。
    /// </summary>
    public void ResetVisuals()
    {
        displayOnly = false;
        externalClickHandler = null;
        if (progressRoutine != null)
        {
            StopCoroutine(progressRoutine);
            progressRoutine = null;
        }

        KillTweens();

        if (warning != null)
        {
            warning.SetActive(false);
        }

        if (timerText != null)
        {
            timerText.gameObject.SetActive(false);
            timerText.text = string.Empty;
        }

        SetDishCaption(null);

        if (glowImage != null)
        {
            glowImage.gameObject.SetActive(true);
            glowImage.transform.localScale = Vector3.one;
        }

        if (progressImage != null)
        {
            progressImage.fillAmount = 0f;
            if (progressImage.transform.parent != null)
            {
                progressImage.transform.parent.gameObject.SetActive(false);
            }
        }

        var background = GetComponent<Image>();
        if (background != null)
        {
            background.enabled = true;
        }

        if (button != null)
        {
            button.enabled = true;
            button.interactable = true;
        }

        transform.localScale = defaultLocalScale;
        ResetIconTransform();
    }

    /// <summary>
    /// 处理用餐进度协程相关逻辑。
    /// </summary>
    /// <param name="duration">持续时间。</param>
    /// <returns>协程迭代器。</returns>
    private IEnumerator DiningProgressRoutine(float duration)
    {
        duration = Mathf.Max(0.1f, duration);
        var remaining = duration;

        while (remaining > 0f)
        {
            remaining -= Time.deltaTime;

            if (progressImage != null)
            {
                progressImage.fillAmount = Mathf.Clamp01(1f - (remaining / duration));
            }

            yield return null;
        }

        progressRoutine = null;
    }

    public Transform GetFlyIconTransform()
    {
        return icon != null ? icon.transform : transform;
    }

    /// <summary>
    /// 处理按钮点击事件。
    /// </summary>
    private void OnClick()
    {
        if (externalClickHandler != null)
        {
            GameAudioManager.PlayButtonClick();
            externalClickHandler();
            return;
        }

        if (displayOnly || boundTable == null)
        {
            return;
        }

        switch (state)
        {
            case State.WaitingForOrder:
                if (!boundTable.CanServeNow())
                {
                    FlashNoDishWarning();
                    return;
                }

                break;
            case State.WaitingForServe:
            case State.ReadyToClaim:
            case State.CompactCheckoutReady:
                break;
            default:
                return;
        }

        GameAudioManager.PlayButtonClick();
        boundTable.HandleActionButtonClick();
    }

    /// <summary>
    /// 设置按钮图标并处理缺省显示。
    /// </summary>
    /// <param name="sprite">参数值。</param>
    private void SetIcon(Sprite sprite)
    {
        if (icon == null)
        {
            return;
        }

        icon.sprite = sprite != null ? sprite : defaultIcon;
    }

    /// <summary>
    /// 结账图标：普客桌用 coin，有贵客用 checkout。
    /// </summary>
    private Sprite GetCheckoutCoinIcon()
    {
        var hasVip = false;
        if (boundTable != null)
        {
            var scene = TavernSceneManager.Instance;
            hasVip = scene != null && scene.TableHasVipCustomer(boundTable.tableId);
        }

        var path = hasVip ? CheckoutVipIconPath : CheckoutCoinIconPath;
        var loaded = GameplayResourceStore.LoadAsset<Sprite>(path);
        if (loaded != null)
        {
            checkoutCoinIcon = loaded;
            return loaded;
        }

        if (checkoutCoinIcon != null)
        {
            return checkoutCoinIcon;
        }

        return null;
    }

    /// <summary>
    /// 启动发光循环动画。
    /// </summary>
    private void StartGlowLoop()
    {
        if (glowImage != null)
        {
            pulseTween = glowImage.transform
                .DOScale(Vector3.one * 0.94f, 0.45f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        ResetIconTransform();
    }

    /// <summary>
    /// 启动缩放脉冲动画。
    /// </summary>
    private void StartPulseLoop()
    {
        transform.localScale = defaultLocalScale;
        pulseTween = transform
            .DOScale(defaultLocalScale * CheckoutPulseScaleFactor, 0.4f)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);
    }

    /// <summary>
    /// 停止当前 界面 上的 动画缓动 动画。
    /// </summary>
    private void KillTweens()
    {
        pulseTween?.Kill();
        iconTween?.Kill();
        pulseTween = null;
        iconTween = null;
        transform.localScale = defaultLocalScale;
        ResetIconTransform();
    }

    /// <summary>
    /// 将按钮图标恢复到预制体标准姿态，避免动画中断后残留倾斜。
    /// </summary>
    private void ResetIconTransform()
    {
        if (icon == null)
        {
            return;
        }

        icon.transform.localRotation = Quaternion.identity;
        icon.transform.localScale = Vector3.one;
    }
}
