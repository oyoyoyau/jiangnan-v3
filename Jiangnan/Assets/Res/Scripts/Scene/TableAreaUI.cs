using System.Collections;
using DG.Tweening;
using JN.Client.Manager;
using JN.Client.Model;
using JN.Client.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JN.Client.Scene
{
    /// <summary>
    /// 负责桌位区域相关的运行时逻辑。
    /// </summary>
    public class TableAreaUI : MonoBehaviour, IPointerClickHandler
    {
        private const float PurchasePriceUiScaleMultiplier = 1f;
        private const float ExpandPulseScaleFactor = 1.08f;
        private const float ExpandPulseDuration = 0.4f;
        private static readonly Vector2 ActionButtonAnchoredPosition = Vector2.zero;
        private const string NewOrderButtonPrefabPath = "Assets/Res/Resources/UI/Buttons/NewOrderBtn.prefab";
        private const string CleanButtonPrefabPath = "Assets/Res/Resources/UI/Buttons/CleanBtn.prefab";

        [SerializeField] private Vector3 offset = new(0f, TavernWorldRuntimeHudLayout.TableActionHeightOffset, 0f);
        [SerializeField] public GameObject group_PayCoinNum;
        [SerializeField] private TextMeshProUGUI payCoinText;
        [SerializeField] private TextMeshProUGUI runtimeStatusText;
        [SerializeField] private TableOrderButtonUI orderButtonInstance;
        [SerializeField] private TableCleanButtonUI cleanButtonInstance;
        [SerializeField] private bool showOrderButtonWhenWaitingOrder = true;

        private TableArea tableArea;
        private Transform targetTile;
        private RectTransform rectTransform;
        private CanvasGroup canvasGroup;
        private Coroutine countdownRoutine;
        private bool waitingOrderDisplaySuppressed;
        private bool checkoutDisplaySuppressed;
        private bool customerWaitHudActive;
        private TavernTableRuntimeState currentRuntimeState = TavernTableRuntimeState.Locked;
        private string currentCustomText;
        private Vector2 runtimeStatusDefaultAnchoredPosition;
        private bool runtimeStatusDefaultPositionCached;
        private bool unlockPromptVisible;
        private bool expandPromptMode;
        private bool wallExpandPromptVisible;
        private bool deliveryPurchaseIconVisible;
        private bool screenVisible = true;
        private System.Action onPurchaseClick;
        private System.Action onWallExpandClick;
        private TextMeshProUGUI expandText;
        private GameObject buildIcon;
        private GameObject groupExpandRoot;
        private Button wallExpandButton;
        private Tween expandPulseTween;
        private Vector3 expandPulseDefaultScale = Vector3.one;
        private bool expandPulseDefaultScaleCached;

        private bool PurchaseInteractionActive => unlockPromptVisible && onPurchaseClick != null;

        private bool WallExpandInteractionActive => wallExpandPromptVisible && onWallExpandClick != null;

        /// <summary>
        /// 当前头顶 HUD 绑定的桌位对象。
        /// </summary>
        public TableArea BoundTable => tableArea;

        private bool ResolveRequirePlayerClickForOrder()
        {
            if (TavernSceneManager.Instance == null)
            {
                return false;
            }

            var tableId = tableArea != null ? tableArea.tableId : 0;
            // 前台点单：桌边点单按钮对普通客与贵客均关闭。
            if (tableId > 0 && !TavernSceneManager.Instance.ShouldShowTableSideOrderButton(tableId))
            {
                return false;
            }

            if (tableId > 0
                && currentRuntimeState == TavernTableRuntimeState.WaitingOrder
                && !waitingOrderDisplaySuppressed
                && TavernSceneManager.Instance.TableHasVipCustomer(tableId))
            {
                return true;
            }

            return TavernSceneManager.Instance.RequiresPlayerClickForOrder()
                   || (tableId > 0 && TavernSceneManager.Instance.TableRequiresVipOrderInteraction(tableId));
        }

        /// <summary>
        /// 当前头顶 HUD 绑定的世界目标。
        /// </summary>
        public Transform BoundTarget => targetTile;

        /// <summary>
        /// 初始化组件引用和运行时状态。
        /// </summary>
        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            RebuildActionButtonsFromCleanPrefabs();
            CacheStaticReferences();

            DisableBaseRaycastTargets();
            HideStatus();
            if (groupExpandRoot != null)
            {
                StopExpandPulse();
                groupExpandRoot.SetActive(false);
            }

            RefreshInteractionState();
        }

        /// <summary>
        /// 初始化静态绑定引用。
        /// </summary>
        /// <param name="table">桌位对象。</param>
        public void InitBinding(Transform table)
        {
            targetTile = table;
        }

        /// <summary>
        /// 处理绑定桌位相关逻辑。
        /// </summary>
        /// <param name="table">桌位对象。</param>
        public void BindTable(TableArea table)
        {
            if (tableArea != table)
            {
                waitingOrderDisplaySuppressed = false;
                checkoutDisplaySuppressed = false;
            }

            tableArea = table;
            targetTile = table != null ? table.transform : targetTile;
        }

        /// <summary>
        /// 绑定引导建造价格牌的点击回调。
        /// </summary>
        public void BindPurchaseAction(System.Action onPurchase)
        {
            onPurchaseClick = onPurchase;
            RefreshInteractionState();
        }

        /// <summary>
        /// 设置世界跟随偏移（扩建锚点应对准碰撞体中心，避免点 UI 时射线打偏）。
        /// </summary>
        public void SetWorldOffset(Vector3 worldOffset)
        {
            offset = worldOffset;
        }

        /// <summary>
        /// 设置解锁提示显隐。
        /// </summary>
        /// <param name="visible">参数值。</param>
        /// <param name="cost">价格。</param>
        public void SetUnlockPrompt(bool visible, int cost)
        {
            wallExpandPromptVisible = false;
            onWallExpandClick = null;
            if (groupExpandRoot != null)
            {
                StopExpandPulse();
                groupExpandRoot.SetActive(false);
            }

            expandPromptMode = false;
            unlockPromptVisible = visible;
            if (visible)
            {
                deliveryPurchaseIconVisible = false;
            }

            if (payCoinText != null)
            {
                payCoinText.text = cost.ToString();
            }

            ApplyPayGroupDisplayMode();
            if (group_PayCoinNum != null)
            {
                group_PayCoinNum.transform.localScale = Vector3.one * PurchasePriceUiScaleMultiplier;
            }

            RefreshInteractionState();
        }

        /// <summary>
        /// 购买成功后、搬运到位前：只显示采购图标，隐藏价格牌。
        /// </summary>
        public void SetDeliveryPurchaseIcon(bool visible)
        {
            CacheStaticReferences();
            deliveryPurchaseIconVisible = visible;
            if (visible)
            {
                unlockPromptVisible = false;
                expandPromptMode = false;
            }

            ApplyPayGroupDisplayMode();
            RefreshInteractionState();
        }

        /// <summary>
        /// 设置墙体扩建 HUD（group_expand / btn_expand + group_PayCoinNum 价格）。
        /// </summary>
        public void SetInteriorWallExpandPrompt(bool visible, int cost, System.Action onExpand)
        {
            wallExpandPromptVisible = visible;
            onWallExpandClick = visible ? onExpand : null;
            unlockPromptVisible = false;
            expandPromptMode = false;
            deliveryPurchaseIconVisible = false;

            CacheStaticReferences();
            if (payCoinText != null)
            {
                payCoinText.text = cost.ToString();
            }

            if (group_PayCoinNum != null)
            {
                group_PayCoinNum.SetActive(visible);
                if (visible)
                {
                    ApplyInteriorWallExpandPayGroupDisplay();
                    group_PayCoinNum.transform.localScale = Vector3.one * PurchasePriceUiScaleMultiplier;
                }
            }

            if (groupExpandRoot != null)
            {
                groupExpandRoot.SetActive(visible);
                if (visible)
                {
                    StartExpandPulse();
                }
                else
                {
                    StopExpandPulse();
                }
            }

            EnsureWallExpandButtonBinding(visible);
            RefreshInteractionState();
        }

        /// <summary>
        /// 墙体扩建：价格牌只显示金币与数量，扩建按钮在 group_expand。
        /// </summary>
        private void ApplyInteriorWallExpandPayGroupDisplay()
        {
            if (group_PayCoinNum == null)
            {
                return;
            }

            foreach (Transform child in group_PayCoinNum.transform)
            {
                if (child == null)
                {
                    continue;
                }

                var isBuildIcon = child.name == "BuildIcon";
                var isExpandLabel = child.name == "txt_Expand";
                child.gameObject.SetActive(!isBuildIcon && !isExpandLabel);
            }
        }

        /// <summary>
        /// 设置酒楼扩建提示：同时显示金钱价格与 txt_Expand。
        /// </summary>
        public void SetExpandPrompt(bool visible, int cost)
        {
            wallExpandPromptVisible = false;
            onWallExpandClick = null;
            if (groupExpandRoot != null)
            {
                StopExpandPulse();
                groupExpandRoot.SetActive(false);
            }

            expandPromptMode = visible;
            unlockPromptVisible = visible;
            if (payCoinText != null)
            {
                payCoinText.text = cost.ToString();
            }

            ApplyPayGroupDisplayMode();
            if (group_PayCoinNum != null)
            {
                group_PayCoinNum.transform.localScale = Vector3.one * PurchasePriceUiScaleMultiplier;
            }

            RefreshInteractionState();
        }

        /// <summary>
        /// 按购买/扩建模式切换 group_PayCoinNum 子节点显隐。
        /// 普通购买隐藏 txt_Expand；扩建模式金钱与扩建文字都显示。
        /// </summary>
        private void ApplyPayGroupDisplayMode()
        {
            CacheStaticReferences();
            if (group_PayCoinNum == null)
            {
                return;
            }

            var showPayGroup = unlockPromptVisible || deliveryPurchaseIconVisible;
            group_PayCoinNum.SetActive(showPayGroup);
            if (!showPayGroup)
            {
                if (buildIcon != null)
                {
                    buildIcon.SetActive(false);
                }

                return;
            }

            foreach (Transform child in group_PayCoinNum.transform)
            {
                if (child == null)
                {
                    continue;
                }

                var isBuildIcon = child.name == "BuildIcon";
                if (deliveryPurchaseIconVisible)
                {
                    child.gameObject.SetActive(isBuildIcon);
                    continue;
                }

                // 价格牌阶段隐藏采购图标，只在搬运途中显示。
                if (isBuildIcon)
                {
                    child.gameObject.SetActive(false);
                    continue;
                }

                var isExpandLabel = child.name == "txt_Expand";
                if (expandPromptMode)
                {
                    // 扩建：金币相关节点 + 扩建文字一并显示。
                    child.gameObject.SetActive(true);
                }
                else
                {
                    child.gameObject.SetActive(!isExpandLabel);
                }
            }

            if (expandText != null)
            {
                expandText.gameObject.SetActive(expandPromptMode && !deliveryPurchaseIconVisible);
            }
        }

        /// <summary>
        /// 点击建造价格牌（扩建模式由 btn_expand 自行处理）。
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (wallExpandPromptVisible)
            {
                return;
            }

            if (!PurchaseInteractionActive)
            {
                return;
            }

            eventData?.Use();
            GameAudioManager.PlayButtonClick();
            onPurchaseClick?.Invoke();
        }

        /// <summary>
        /// 刷新状态。
        /// </summary>
        /// <param name="state">参数值。</param>
        /// <param name="customText">参数值。</param>
        public void RefreshState(TavernTableRuntimeState state, string customText = null)
        {
            currentRuntimeState = state;
            currentCustomText = customText;

            if (state == TavernTableRuntimeState.Locked)
            {
                HideStatus();
                return;
            }

            EnsureRuntimeStatusText();
            if (runtimeStatusText == null)
            {
                return;
            }

            if (state != TavernTableRuntimeState.WaitingOrder)
            {
                waitingOrderDisplaySuppressed = false;
            }

            if (state != TavernTableRuntimeState.Checkout)
            {
                checkoutDisplaySuppressed = false;
            }

            var showOrderButton = showOrderButtonWhenWaitingOrder;
            if (tableArea != null
                && tableArea.tableId > 0
                && TavernSceneManager.Instance != null
                && !TavernSceneManager.Instance.ShouldShowTableSideOrderButton(tableArea.tableId))
            {
                showOrderButton = false;
            }

            var viewState = TableRuntimeViewStateFactory.Create(
                state,
                customText,
                waitingOrderDisplaySuppressed,
                ResolveWaiterOrderingInProgress(),
                showOrderButton,
                ResolveRequirePlayerClickForOrder(),
                TavernSceneManager.Instance != null
                && TavernSceneManager.Instance.RequiresPlayerClickForCheckout(),
                checkoutDisplaySuppressed,
                TavernSceneManager.Instance != null
                && TavernSceneManager.Instance.ShouldShowCompactCheckoutBubble());
            RefreshActionButtons(viewState);

            if (state == TavernTableRuntimeState.Idle
                || state == TavernTableRuntimeState.Dining
                || !ShouldShowRuntimeStatusText())
            {
                runtimeStatusText.gameObject.SetActive(false);
                StopRuntimeEffects();
                return;
            }

            if (!viewState.ShowRuntimeText)
            {
                runtimeStatusText.gameObject.SetActive(false);
                return;
            }

            runtimeStatusText.gameObject.SetActive(true);
            runtimeStatusText.text = viewState.RuntimeText;
            runtimeStatusText.color = viewState.RuntimeTextColor;
            ApplyStateAnimation(viewState);
        }

        /// <summary>
        /// 顾客等待进度气泡出现时，同步显示桌位原有状态文案（点菜/等待上菜/结账）。
        /// </summary>
        public void SetCustomerWaitHudActive(bool active)
        {
            if (customerWaitHudActive == active)
            {
                return;
            }

            customerWaitHudActive = active;
            if (currentRuntimeState == TavernTableRuntimeState.Locked)
            {
                return;
            }

            RefreshState(currentRuntimeState, currentCustomText);
        }

        /// <summary>
        /// 隐藏状态。
        /// </summary>
        public void HideStatus()
        {
            waitingOrderDisplaySuppressed = false;
            checkoutDisplaySuppressed = false;
            customerWaitHudActive = false;
            currentRuntimeState = TavernTableRuntimeState.Locked;
            currentCustomText = null;
            StopRuntimeEffects();
            HideActionButtons();
            if (runtimeStatusText != null)
            {
                runtimeStatusText.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 启动状态倒计时。
        /// </summary>
        /// <param name="state">参数值。</param>
        /// <param name="duration">持续时间。</param>
        /// <param name="prefix">参数值。</param>
        public void StartStateCountdown(TavernTableRuntimeState state, float duration, string prefix = null)
        {
            if (!ShouldShowRuntimeStatusText())
            {
                return;
            }

            EnsureRuntimeStatusText();
            if (runtimeStatusText == null)
            {
                return;
            }

            if (countdownRoutine != null)
            {
                StopCoroutine(countdownRoutine);
            }

            var labelPrefix = string.IsNullOrWhiteSpace(prefix) ? GetDefaultStateText(state) : prefix;
            countdownRoutine = StartCoroutine(StateCountdownRoutine(state, duration, labelPrefix));
        }

        /// <summary>
        /// 停止状态倒计时。
        /// </summary>
        public void StopStateCountdown()
        {
            if (countdownRoutine == null)
            {
                return;
            }

            StopCoroutine(countdownRoutine);
            countdownRoutine = null;
        }

        /// <summary>
        /// 获取场景锚点位置。
        /// 购买价签：跟 TableArea.canBuildObj（场景子节点 CanBuild）视觉中心 + 高度偏移。
        /// 其它状态：跟桌位 Transform + 固定高度偏移。
        /// </summary>
        public Vector3 GetWorldAnchorPosition()
        {
            if (unlockPromptVisible && tableArea != null)
            {
                return tableArea.GetPurchaseHudWorldPosition();
            }

            return targetTile == null ? Vector3.zero : targetTile.position + offset;
        }

        /// <summary>
        /// 设置头顶 HUD 在画布中的锚点位置。
        /// </summary>
        public void SetAnchoredPosition(Vector2 position)
        {
            if (rectTransform == null)
            {
                rectTransform = GetComponent<RectTransform>();
            }

            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = position;
            }
        }

        /// <summary>
        /// 切换头顶 HUD 的整体显隐。
        /// </summary>
        public void SetVisible(bool visible)
        {
            screenVisible = visible;
            RefreshInteractionState();
        }

        private void RefreshInteractionState()
        {
            canvasGroup ??= GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                return;
            }

            if (PurchaseInteractionActive)
            {
                SetPurchaseRaycastTargets(true);
            }
            else
            {
                SetPurchaseRaycastTargets(false);
            }

            if (WallExpandInteractionActive)
            {
                SetWallExpandRaycastTargets(true);
            }
            else
            {
                SetWallExpandRaycastTargets(false);
                if (!PurchaseInteractionActive)
                {
                    DisableBaseRaycastTargets();
                }
            }

            var allowRaycast = screenVisible
                               && (PurchaseInteractionActive
                                   || WallExpandInteractionActive
                                   || HasInteractiveActionButtons());
            canvasGroup.alpha = screenVisible ? 1f : 0f;
            canvasGroup.blocksRaycasts = allowRaycast;
            canvasGroup.interactable = allowRaycast;
        }

        private bool HasInteractiveActionButtons()
        {
            return (orderButtonInstance != null && orderButtonInstance.gameObject.activeInHierarchy)
                   || (cleanButtonInstance != null && cleanButtonInstance.gameObject.activeInHierarchy);
        }

        private void SetPurchaseRaycastTargets(bool enabled)
        {
            if (group_PayCoinNum == null)
            {
                return;
            }

            foreach (var graphic in group_PayCoinNum.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = enabled;
            }
        }

        private void SetWallExpandRaycastTargets(bool enabled)
        {
            if (groupExpandRoot == null)
            {
                return;
            }

            foreach (var graphic in groupExpandRoot.GetComponentsInChildren<Graphic>(true))
            {
                // btn_expand 必须始终可点，运行时不得关闭其射线检测。
                if (IsWallExpandButtonGraphic(graphic.transform))
                {
                    graphic.raycastTarget = true;
                    continue;
                }

                graphic.raycastTarget = enabled;
            }
        }

        private void OnClickWallExpandButton()
        {
            if (!WallExpandInteractionActive)
            {
                return;
            }

            GameAudioManager.PlayButtonClick();
            onWallExpandClick?.Invoke();
        }

        /// <summary>
        /// 扩建点击绑定在 btn_expand 的 Button 上。
        /// </summary>
        private void EnsureWallExpandButtonBinding(bool expandVisible)
        {
            if (groupExpandRoot == null)
            {
                groupExpandRoot = transform.Find("group_expand")?.gameObject;
            }

            if (wallExpandButton == null && groupExpandRoot != null)
            {
                wallExpandButton = groupExpandRoot.transform.Find("btn_expand")?.GetComponent<Button>();
            }

            if (wallExpandButton == null)
            {
                return;
            }

            wallExpandButton.onClick.RemoveListener(OnClickWallExpandButton);
            if (!expandVisible)
            {
                wallExpandButton.interactable = false;
                return;
            }

            wallExpandButton.enabled = true;
            wallExpandButton.interactable = true;
            wallExpandButton.onClick.AddListener(OnClickWallExpandButton);
            EnsureWallExpandButtonRaycastEnabled();
        }

        /// <summary>
        /// 保证 btn_expand 自身 Image 始终接收射线。
        /// </summary>
        private void EnsureWallExpandButtonRaycastEnabled()
        {
            if (wallExpandButton == null)
            {
                return;
            }

            foreach (var graphic in wallExpandButton.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = true;
            }
        }

        /// <summary>
        /// 缓存静态引用。
        /// </summary>
        private void CacheStaticReferences()
        {
            if (orderButtonInstance == null || cleanButtonInstance == null)
            {
                RebuildActionButtonsFromCleanPrefabs();
            }

            if (group_PayCoinNum == null)
            {
                group_PayCoinNum = transform.Find("group_PayCoinNum")?.gameObject;
            }

            if (payCoinText == null)
            {
                payCoinText = transform.Find("group_PayCoinNum/txt_CoinNum")?.GetComponent<TextMeshProUGUI>();
            }

            if (runtimeStatusText == null)
            {
                runtimeStatusText = transform.Find("txt_RuntimeState")?.GetComponent<TextMeshProUGUI>();
            }

            if (expandText == null)
            {
                expandText = transform.Find("group_PayCoinNum/txt_Expand")?.GetComponent<TextMeshProUGUI>();
            }

            // 默认隐藏扩建文案（历史杂物扩建入口已移除，保留节点兼容预制体）。
            if (expandText != null && !expandPromptMode)
            {
                expandText.gameObject.SetActive(false);
            }

            if (buildIcon == null)
            {
                var buildIconTransform = group_PayCoinNum != null
                    ? group_PayCoinNum.transform.Find("BuildIcon")
                    : transform.Find("group_PayCoinNum/BuildIcon");
                buildIcon = buildIconTransform != null ? buildIconTransform.gameObject : null;
            }

            if (buildIcon != null && !deliveryPurchaseIconVisible)
            {
                buildIcon.SetActive(false);
            }

            if (groupExpandRoot == null)
            {
                groupExpandRoot = transform.Find("group_expand")?.gameObject;
            }

            if (wallExpandButton == null && groupExpandRoot != null)
            {
                wallExpandButton = groupExpandRoot.transform.Find("btn_expand")?.GetComponent<Button>();
            }

            if (groupExpandRoot != null && !wallExpandPromptVisible)
            {
                StopExpandPulse();
                groupExpandRoot.SetActive(false);
            }

            CacheRuntimeStatusDefaultPosition();

            if (orderButtonInstance == null)
            {
                orderButtonInstance = transform.Find("NewOrderBtn")?.GetComponent<TableOrderButtonUI>();
            }

            if (cleanButtonInstance == null)
            {
                cleanButtonInstance = transform.Find("CleanBtn")?.GetComponent<TableCleanButtonUI>();
            }
        }

        /// <summary>
        /// 确保运行时状态文本。
        /// </summary>
        private void EnsureRuntimeStatusText()
        {
            CacheStaticReferences();
            MoveRuntimeStatusBelowActionButtons();
        }

        /// <summary>
        /// 确保状态文案始终位于点单按钮下方，同时维持较低层级，避免遮挡按钮点击与视觉。
        /// </summary>
        private void MoveRuntimeStatusBelowActionButtons()
        {
            if (runtimeStatusText == null)
            {
                return;
            }

            var runtimeTransform = runtimeStatusText.transform;
            var targetIndex = runtimeTransform.GetSiblingIndex();

            var anchorRect = GetPrimaryVisibleActionButtonRect();
            if (anchorRect == null)
            {
                RestoreRuntimeStatusDefaultPosition();
                return;
            }

            targetIndex = Mathf.Min(targetIndex, anchorRect.transform.GetSiblingIndex());
            runtimeTransform.SetSiblingIndex(Mathf.Max(0, targetIndex - 1));

            var runtimeRect = runtimeStatusText.rectTransform;
            if (runtimeRect == null || runtimeRect.parent != anchorRect.parent)
            {
                RestoreRuntimeStatusDefaultPosition();
                return;
            }

            var orderAnchoredPosition = anchorRect.anchoredPosition;
            var verticalSpacing = 16f;
            runtimeRect.anchoredPosition = new Vector2(
                orderAnchoredPosition.x,
                orderAnchoredPosition.y - anchorRect.rect.height - verticalSpacing);
        }

        /// <summary>
        /// 刷新操作按钮。
        /// </summary>
        /// <param name="state">参数值。</param>
        private void RefreshActionButtons(TableRuntimeViewState viewState)
        {
            if (tableArea == null)
            {
                return;
            }

            switch (viewState.OrderVisualState)
            {
                case TableOrderVisualState.DisplayOnlyWaitingOrder:
                    HideCleanButton();
                    EnsureOrderButton();
                    orderButtonInstance.ShowWaitingForOrderDisplayOnly(GetOrderIcon());
                    break;
                case TableOrderVisualState.ClickableWaitingOrder:
                    HideCleanButton();
                    EnsureOrderButton();
                    orderButtonInstance.ShowWaitingForOrder(GetOrderIcon(), canServe: true);
                    break;
                case TableOrderVisualState.ClickableWaitingServe:
                    HideCleanButton();
                    EnsureOrderButton();
                    orderButtonInstance.ShowWaitingForServe(GetDefaultServeIcon());
                    break;
                case TableOrderVisualState.ReadyToClaim:
                    EnsureOrderButton();
                    orderButtonInstance.ShowReadyToClaim(ShouldPlayCheckoutBubblePulse());
                    break;
                case TableOrderVisualState.ClickableCheckoutCompact:
                    EnsureOrderButton();
                    orderButtonInstance.ShowCompactCheckoutReady();
                    break;
                default:
                    break;
            }

            if (viewState.OrderVisualState == TableOrderVisualState.Hidden)
            {
                HideOrderButton();
            }

            if (!viewState.ShowCleanButton)
            {
                HideCleanButton();
            }

            RefreshInteractionState();
        }

        /// <summary>
        /// 确保点单按钮。
        /// </summary>
        private void EnsureOrderButton()
        {
            CacheStaticReferences();
            if (orderButtonInstance != null)
            {
                NormalizeActionButtonLayout(orderButtonInstance.transform as RectTransform);
                orderButtonInstance.gameObject.SetActive(true);
                orderButtonInstance.Init(tableArea);
                MoveRuntimeStatusBelowActionButtons();
            }
        }

        /// <summary>
        /// 确保清扫按钮。
        /// </summary>
        private void EnsureCleanButton()
        {
            CacheStaticReferences();
            if (cleanButtonInstance != null)
            {
                NormalizeActionButtonLayout(cleanButtonInstance.transform as RectTransform);
                cleanButtonInstance.gameObject.SetActive(true);
                MoveRuntimeStatusBelowActionButtons();
            }
        }

        /// <summary>
        /// 隐藏点单按钮。
        /// </summary>
        private void HideOrderButton()
        {
            if (orderButtonInstance == null)
            {
                return;
            }

            orderButtonInstance.ResetVisuals();
            orderButtonInstance.gameObject.SetActive(false);
        }

        /// <summary>
        /// 隐藏清扫按钮。
        /// </summary>
        private void HideCleanButton()
        {
            if (cleanButtonInstance == null)
            {
                return;
            }

            cleanButtonInstance.ResetVisuals();
            cleanButtonInstance.gameObject.SetActive(false);
        }

        /// <summary>
        /// 隐藏操作按钮。
        /// </summary>
        private void HideActionButtons()
        {
            HideOrderButton();
            HideCleanButton();
        }

        /// <summary>
        /// 获取点单图标（贵客桌使用推荐菜图标）。
        /// </summary>
        private Sprite GetOrderIcon()
        {
            var tableId = tableArea != null ? tableArea.tableId : 0;
            return CustomerWaitHudIconCatalog.ResolveOrderIcon(TavernSceneManager.Instance, tableId);
        }

        /// <summary>
        /// 获取默认点单图标。
        /// </summary>
        /// <returns>返回匹配到的对象引用。</returns>
        private Sprite GetDefaultOrderIcon()
        {
            return CustomerWaitHudIconCatalog.LoadOrderIcon();
        }

        private Sprite GetDefaultServeIcon()
        {
            return GameplayResourceStore.LoadAsset<Sprite>("Assets/Res/Resources/Textures/UI/Icons 1/红烧肉.png")
                   ?? GetDefaultOrderIcon();
        }

        /// <summary>
        /// 统一修正头顶按钮的锚点和局部位置，避免 prefab 资源版本不一致导致漂移。
        /// </summary>
        private void NormalizeActionButtonLayout(RectTransform buttonRect)
        {
            if (buttonRect == null)
            {
                return;
            }

            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = ActionButtonAnchoredPosition;
            buttonRect.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// 使用干净按钮 prefab 重建头顶按钮，避免场景条目继续依赖脏的嵌套资源。
        /// </summary>
        private void RebuildActionButtonsFromCleanPrefabs()
        {
            orderButtonInstance = RebuildActionButton<TableOrderButtonUI>("NewOrderBtn", NewOrderButtonPrefabPath);
            cleanButtonInstance = RebuildActionButton<TableCleanButtonUI>("CleanBtn", CleanButtonPrefabPath);
        }

        /// <summary>
        /// 替换指定名称的旧按钮节点，并重新实例化为当前运行时使用的干净版本。
        /// </summary>
        private TButton RebuildActionButton<TButton>(string childName, string prefabPath) where TButton : Component
        {
            RemoveExistingChild(childName);

            var prefab = GameplayResourceStore.LoadAsset<GameObject>(prefabPath);
            if (prefab == null)
            {
                return null;
            }

            var instance = Instantiate(prefab, transform, false);
            instance.name = childName;
            var buttonRect = instance.transform as RectTransform;
            NormalizeActionButtonLayout(buttonRect);
            return instance.GetComponent<TButton>();
        }

        /// <summary>
        /// 删除旧的按钮节点，避免 prefab 残留和运行时重影。
        /// </summary>
        private void RemoveExistingChild(string childName)
        {
            var existingChild = transform.Find(childName);
            if (existingChild == null)
            {
                return;
            }

            Destroy(existingChild.gameObject);
        }

        /// <summary>
        /// 应用状态动画。
        /// </summary>
        /// <param name="state">参数值。</param>
        private void ApplyStateAnimation(TableRuntimeViewState viewState)
        {
            if (runtimeStatusText == null)
            {
                return;
            }

            runtimeStatusText.transform.localScale = Vector3.one;
            if (viewState.RuntimeTextAnchor == TableRuntimeTextAnchor.OrderButton)
            {
                MoveRuntimeStatusToOrderAnchor();
                return;
            }

            MoveRuntimeStatusBelowActionButtons();
        }

        private void MoveRuntimeStatusToOrderAnchor()
        {
            if (runtimeStatusText == null)
            {
                return;
            }

            var runtimeTransform = runtimeStatusText.transform;
            var targetIndex = runtimeTransform.GetSiblingIndex();
            if (orderButtonInstance != null)
            {
                targetIndex = Mathf.Min(targetIndex, orderButtonInstance.transform.GetSiblingIndex());
            }

            runtimeTransform.SetSiblingIndex(Mathf.Max(0, targetIndex));

            var runtimeRect = runtimeStatusText.rectTransform;
            var orderRect = orderButtonInstance != null ? orderButtonInstance.GetComponent<RectTransform>() : null;
            if (runtimeRect == null || orderRect == null || runtimeRect.parent != orderRect.parent)
            {
                return;
            }

            runtimeRect.anchoredPosition = orderRect.anchoredPosition;
        }

        /// <summary>
        /// 按秒刷新桌位状态倒计时。
        /// </summary>
        /// <param name="state">参数值。</param>
        /// <param name="duration">持续时间。</param>
        /// <param name="prefix">参数值。</param>
        /// <returns>协程迭代器。</returns>
        private IEnumerator StateCountdownRoutine(TavernTableRuntimeState state, float duration, string prefix)
        {
            duration = Mathf.Max(0f, duration);
            runtimeStatusText.text = prefix;
            runtimeStatusText.color = GetDefaultStateColor(state);
            while (duration > 0f)
            {
                yield return null;
                duration -= Time.deltaTime;
            }

            countdownRoutine = null;
            runtimeStatusText.text = GetDefaultStateText(state);
        }

        /// <summary>
        /// 停止运行时特效。
        /// </summary>
        private void StopRuntimeEffects()
        {
            StopStateCountdown();
            StopExpandPulse();
            if (runtimeStatusText != null)
            {
                runtimeStatusText.transform.localScale = Vector3.one;
            }
        }

        /// <summary>
        /// 扩建按钮呼吸缩放（与结账气泡同款：1→1.08 Yoyo）。
        /// </summary>
        private void StartExpandPulse()
        {
            if (groupExpandRoot == null)
            {
                return;
            }

            StopExpandPulse();
            var expandTransform = groupExpandRoot.transform;
            if (!expandPulseDefaultScaleCached)
            {
                expandPulseDefaultScale = expandTransform.localScale;
                if (expandPulseDefaultScale.sqrMagnitude < 0.0001f)
                {
                    expandPulseDefaultScale = Vector3.one;
                }

                expandPulseDefaultScaleCached = true;
            }

            expandTransform.localScale = expandPulseDefaultScale;
            expandPulseTween = expandTransform
                .DOScale(expandPulseDefaultScale * ExpandPulseScaleFactor, ExpandPulseDuration)
                .SetEase(Ease.InOutSine)
                .SetUpdate(true)
                .SetLoops(-1, LoopType.Yoyo);
        }

        private void StopExpandPulse()
        {
            if (expandPulseTween != null && expandPulseTween.IsActive())
            {
                expandPulseTween.Kill();
            }

            expandPulseTween = null;
            if (groupExpandRoot != null && expandPulseDefaultScaleCached)
            {
                groupExpandRoot.transform.localScale = expandPulseDefaultScale;
            }
        }

        /// <summary>
        /// 禁用时移除事件监听，避免重复回调。
        /// </summary>
        private void OnDisable()
        {
            StopRuntimeEffects();
            if (wallExpandButton != null)
            {
                wallExpandButton.onClick.RemoveListener(OnClickWallExpandButton);
            }
        }

        /// <summary>
        /// 禁用底层不需要交互的射线目标。
        /// </summary>
        private void DisableBaseRaycastTargets()
        {
            foreach (var graphic in GetComponentsInChildren<Graphic>(true))
            {
                if (ShouldKeepRaycast(graphic.transform))
                {
                    continue;
                }

                graphic.raycastTarget = false;
            }

            foreach (var text in GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (ShouldKeepRaycast(text.transform))
                {
                    continue;
                }

                text.raycastTarget = false;
            }
        }

        /// <summary>
        /// 处理是否保留射线相关逻辑。
        /// </summary>
        /// <param name="target">目标对象。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private bool ShouldKeepRaycast(Transform target)
        {
            if (IsWallExpandButtonGraphic(target))
            {
                return true;
            }

            if (PurchaseInteractionActive
                && group_PayCoinNum != null
                && target.IsChildOf(group_PayCoinNum.transform))
            {
                return true;
            }

            if (WallExpandInteractionActive
                && groupExpandRoot != null
                && target.IsChildOf(groupExpandRoot.transform))
            {
                return true;
            }

            return (orderButtonInstance != null && target.IsChildOf(orderButtonInstance.transform))
                   || (cleanButtonInstance != null && target.IsChildOf(cleanButtonInstance.transform));
        }

        private bool IsWallExpandButtonGraphic(Transform target)
        {
            if (target == null)
            {
                return false;
            }

            if (target.name == "btn_expand")
            {
                return true;
            }

            if (wallExpandButton != null)
            {
                return target == wallExpandButton.transform
                       || target.IsChildOf(wallExpandButton.transform);
            }

            return false;
        }

        private bool ShouldShowRuntimeStatusText()
        {
            if (customerWaitHudActive)
            {
                return true;
            }

            return TavernSceneManager.Instance == null
                   || TavernSceneManager.Instance.ShouldShowTableRuntimeStatusText();
        }

        /// <summary>
        /// 获取默认状态文本。
        /// </summary>
        /// <param name="state">参数值。</param>
        /// <returns>返回方法执行后的结果。</returns>
        internal static string GetDefaultStateText(TavernTableRuntimeState state)
        {
            switch (state)
            {
                case TavernTableRuntimeState.Reserved:
                    return "入座中";
                case TavernTableRuntimeState.WaitingOrder:
                    return "点菜";
                case TavernTableRuntimeState.WaitingServe:
                    return "等待上菜";
                case TavernTableRuntimeState.Dining:
                    return "吃饭中";
                case TavernTableRuntimeState.Checkout:
                    return "结账";
                case TavernTableRuntimeState.Cleaning:
                    return "清理中";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 获取默认状态颜色。
        /// </summary>
        /// <param name="state">参数值。</param>
        /// <returns>返回方法执行后的结果。</returns>
        internal static Color GetDefaultStateColor(TavernTableRuntimeState state)
        {
            switch (state)
            {
                case TavernTableRuntimeState.WaitingOrder:
                case TavernTableRuntimeState.WaitingServe:
                    return new Color(1f, 0.85f, 0.2f);
                case TavernTableRuntimeState.Checkout:
                    return new Color(0.4f, 1f, 0.4f);
                case TavernTableRuntimeState.Cleaning:
                    return new Color(0.5f, 0.9f, 1f);
                default:
                    return Color.white;
            }
        }

        private bool ResolveWaiterOrderingInProgress()
        {
            if (tableArea == null || tableArea.tableId <= 0 || TavernSceneManager.Instance == null)
            {
                return false;
            }

            return TavernSceneManager.Instance.IsWaiterAssignedToOrderTable(tableArea.tableId);
        }

        public void HideWaitingOrderDisplay()
        {
            waitingOrderDisplaySuppressed = true;
            RefreshState(currentRuntimeState, currentCustomText);
        }

        /// <summary>
        /// 小二点单任务中断且桌位仍为待点单时，恢复点单气泡。
        /// </summary>
        public void RestoreWaitingOrderDisplay()
        {
            if (!waitingOrderDisplaySuppressed)
            {
                return;
            }

            waitingOrderDisplaySuppressed = false;
            RefreshState(currentRuntimeState, currentCustomText);
        }

        /// <summary>
        /// 自动收账时气泡静止；仍需玩家点派单时播放脉冲。
        /// </summary>
        private static bool ShouldPlayCheckoutBubblePulse()
        {
            return TavernSceneManager.Instance != null
                   && TavernSceneManager.Instance.RequiresPlayerClickForCheckout();
        }

        /// <summary>
        /// 结账气泡是否已被小二接单 suppress。
        /// </summary>
        public bool IsCheckoutDisplaySuppressed => checkoutDisplaySuppressed;

        /// <summary>
        /// 小二走到桌边开始结账：隐藏派单气泡，仅保留收账中文案（若仍显示）。
        /// </summary>
        public void HideCheckoutDisplay(string progressText = "结账中")
        {
            checkoutDisplaySuppressed = true;
            RefreshState(currentRuntimeState, progressText ?? currentCustomText);
        }

        /// <summary>
        /// 小二结账任务中断且桌位仍为待结账时，恢复结账气泡。
        /// </summary>
        public void RestoreCheckoutDisplay()
        {
            if (!checkoutDisplaySuppressed)
            {
                return;
            }

            checkoutDisplaySuppressed = false;
            RefreshState(currentRuntimeState, currentCustomText);
        }

        /// <summary>
        /// 缓存运行中文案默认位置，便于按钮隐藏后回到基础布局。
        /// </summary>
        private void CacheRuntimeStatusDefaultPosition()
        {
            if (runtimeStatusDefaultPositionCached || runtimeStatusText == null)
            {
                return;
            }

            runtimeStatusDefaultAnchoredPosition = runtimeStatusText.rectTransform.anchoredPosition;
            runtimeStatusDefaultPositionCached = true;
        }

        /// <summary>
        /// 获取金币飞行特效的起点（优先当前可见操作按钮图标中心）。
        /// </summary>
        public Transform GetCoinFlySourceTransform()
        {
            if (orderButtonInstance != null && orderButtonInstance.gameObject.activeInHierarchy)
            {
                return orderButtonInstance.GetFlyIconTransform();
            }

            if (cleanButtonInstance != null && cleanButtonInstance.gameObject.activeInHierarchy)
            {
                return cleanButtonInstance.transform;
            }

            return transform;
        }

        /// <summary>
        /// 获取当前可见的主操作按钮锚点。
        /// </summary>
        private RectTransform GetPrimaryVisibleActionButtonRect()
        {
            if (orderButtonInstance != null && orderButtonInstance.gameObject.activeInHierarchy)
            {
                return orderButtonInstance.GetComponent<RectTransform>();
            }

            if (cleanButtonInstance != null && cleanButtonInstance.gameObject.activeInHierarchy)
            {
                return cleanButtonInstance.GetComponent<RectTransform>();
            }

            return null;
        }

        /// <summary>
        /// 没有头顶按钮时，把运行中文案恢复到默认位置。
        /// </summary>
        private void RestoreRuntimeStatusDefaultPosition()
        {
            if (!runtimeStatusDefaultPositionCached || runtimeStatusText == null)
            {
                return;
            }

            runtimeStatusText.rectTransform.anchoredPosition = runtimeStatusDefaultAnchoredPosition;
        }
    }

    public enum TableRuntimeTextAnchor
    {
        Default = 0,
        OrderButton = 1
    }

    public enum TableOrderVisualState
    {
        Hidden = 0,
        DisplayOnlyWaitingOrder = 1,
        ReadyToClaim = 2,
        ClickableWaitingOrder = 3,
        ClickableWaitingServe = 4,
        ClickableCheckoutCompact = 5
    }

    public sealed class TableRuntimeViewState
    {
        public bool ShowRuntimeText { get; set; }
        public string RuntimeText { get; set; }
        public Color RuntimeTextColor { get; set; }
        public TableRuntimeTextAnchor RuntimeTextAnchor { get; set; }
        public TableOrderVisualState OrderVisualState { get; set; }
        public bool ShowCleanButton { get; set; }
    }

    /// <summary>
    /// 把桌位运行时状态转换成声明式 ViewState，避免 UI 脚本持续堆叠状态分支。
    /// </summary>
    public static class TableRuntimeViewStateFactory
    {
        public static TableRuntimeViewState Create(
            TavernTableRuntimeState state,
            string customText,
            bool waitingOrderDisplaySuppressed,
            bool waiterOrderingInProgress,
            bool showOrderButtonWhenWaitingOrder,
            bool requirePlayerClickForOrder = false,
            bool requirePlayerClickForCheckout = false,
            bool checkoutDisplaySuppressed = false,
            bool showCompactCheckoutBubble = false)
        {
            var text = customText ?? TableAreaUI.GetDefaultStateText(state);
            var viewState = new TableRuntimeViewState
            {
                ShowRuntimeText = state != TavernTableRuntimeState.Idle
                                  && state != TavernTableRuntimeState.Dining,
                RuntimeText = text,
                RuntimeTextColor = TableAreaUI.GetDefaultStateColor(state),
                RuntimeTextAnchor = TableRuntimeTextAnchor.Default,
                OrderVisualState = TableOrderVisualState.Hidden,
                ShowCleanButton = false
            };

            switch (state)
            {
                case TavernTableRuntimeState.WaitingOrder:
                    // 点单改由前台完成：桌子不显示点单文案/气泡；「点单中」挂在 FrontTableOrder。
                    viewState.ShowRuntimeText = false;
                    viewState.OrderVisualState = TableOrderVisualState.Hidden;
                    viewState.RuntimeText = string.Empty;
                    break;
                case TavernTableRuntimeState.WaitingServe:
                    // 上菜气泡改挂在出餐台菜品上，桌子只保留“待上菜”文案。
                    viewState.OrderVisualState = TableOrderVisualState.Hidden;
                    break;
                case TavernTableRuntimeState.Checkout:
                    if (checkoutDisplaySuppressed)
                    {
                        viewState.OrderVisualState = TableOrderVisualState.Hidden;
                        break;
                    }

                    viewState.OrderVisualState = TableOrderVisualState.ReadyToClaim;
                    break;
            }

            return viewState;
        }
    }
}
