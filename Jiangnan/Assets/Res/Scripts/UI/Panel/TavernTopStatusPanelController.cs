using System;
using System.Collections;
using DG.Tweening;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.Scene;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.UI
{
    /// <summary>
    /// Tavern 顶部状态面板。
    /// 负责玩家信息、营业倒计时；员工/科技入口已移至底部导航栏。
    /// </summary>
    public class TavernTopStatusPanelController : HudPanelController<TavernHudPanelData>
    {
        private const string CustomerEnterQueueFillSpritePath = "Assets/Res/Resources/Textures/UI/Icons 1/customerEnterProgressFillRed.png";
        private const string StaffEntryNodeName = "group_staff";
        private const string TechEntryNodeName = "group_tech";
        private const string TavernLevelSpritePathFormat =
            "Assets/Res/Resources/Textures/UI/Panel/UpgradeTavern/lv{0}.png";
        private const string PopularMenuTitleSpritePath =
            "Assets/Res/Resources/Textures/UI/MenuSwitch/大众菜单.png";
        private const string VipMenuTitleSpritePath =
            "Assets/Res/Resources/Textures/UI/MenuSwitch/贵客菜单.png";
        private const string DebugSkipButtonSpritePath =
            "Assets/Res/Resources/Textures/UI/Menu/horizontalPanel.png";
        private const string DebugSkipButtonName = "btn_DebugSkipThreeStar";
        private const string DebugSkipButtonLabel = "跳去三星酒楼";
        private const float DebugSkipPulseMinScale = 0.95f;
        private const float DebugSkipPulseMaxScale = 1.05f;
        private const float DebugSkipPulseSpeed = 4f;

        [SerializeField] private Transform groupGoldNum;
        [SerializeField] private TextMeshProUGUI playerNameText;
        [SerializeField] private TextMeshProUGUI tavernNameText;
        [SerializeField] private TextMeshProUGUI goldNumText;
        [SerializeField] private TextMeshProUGUI changeGoldText;
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private TextMeshProUGUI runtimeInfoText;
        [SerializeField] private RectTransform runtimeInfoRoot;
        [SerializeField] private TextMeshProUGUI taskText;
        [SerializeField] private RectTransform clockRoot;
        [SerializeField] private RectTransform clockTipsRoot;
        [SerializeField] private TMP_Text clockDownText;
        [SerializeField] private RectTransform customerEnterProgressRoot;
        [SerializeField] private Image customerEnterProgressBackground;
        [SerializeField] private Image customerEnterProgressFill;
        [SerializeField] private Image customerEnterQueueBackground;
        [SerializeField] private TMP_Text customerEnterProgressText;
        [SerializeField] private RectTransform staffEntryRoot;
        [SerializeField] private RectTransform techEntryRoot;
        [SerializeField] private RectTransform prestigeRoot;
        [SerializeField] private Image prestigeBar;
        [SerializeField] private TMP_Text prestigeNameText;
        [SerializeField] private TMP_Text prestigeValueText;
        [SerializeField] private Button tavernLevelButton;
        [SerializeField] private Image tavernLevelImage;
        [SerializeField] private Image playAvatarIcon;
        [SerializeField] private RectTransform topBarRoot;
        [SerializeField] private RectTransform otherGroupRoot;
        [SerializeField] private TMP_Text otherTavernNameText;
        [SerializeField] private Image otherTavernLevelImage;
        [SerializeField] private Image otherPlayAvatarIcon;
        [SerializeField] private TMP_Text otherMoneyText;
        [SerializeField] private RectTransform menuGroupRoot;
        [SerializeField] private TMP_Text curMenuText;
        [SerializeField] private TMP_Text curMenuEffectText;
        [SerializeField] private Image curMenuImage;
        [SerializeField] private GameObject menuBgRoot;

        private Sprite popularMenuTitleSprite;
        private Sprite vipMenuTitleSprite;

        private const int VisitProfitStar1Base = 10000;
        private const int VisitProfitStar2Base = 30000;
        private const int VisitProfitStar3Base = 80000;
        private const int VisitProfitRandomRange = 5000;

        private Sprite customerEnterDefaultFillSprite;
        private Sprite customerEnterQueueFillSprite;

        private Coroutine coinDeltaRoutine;
        private Coroutine businessRemainRoutine;
        /// <summary>倒计时代数：递增后旧协程自行退出，避免 StopCoroutine continue failure。</summary>
        private int businessRemainEpoch;
        private Vector2 coinDeltaBasePosition;
        private bool hasCoinDeltaBasePosition;
        private int displayedCoinNum = -1;
        private Tween goldNumScaleTween;
        private bool tavernLevelButtonBound;
        private int cachedTavernLevelSprite = int.MinValue;
        private int cachedOtherTavernLevelSprite = int.MinValue;
        private int cachedVisitProfitTileId = int.MinValue;
        private int cachedVisitProfitAmount;
        /// <summary>当前已展示的声望，用于判断增量并播过渡。</summary>
        private int displayedPrestige = -1;
        private int displayedPrestigeRequired = 1;
        private int prestigeGainShown;
        private Coroutine prestigeGainRoutine;
        private Tween prestigeBarTween;
        private Button debugSkipThreeStarButton;
        private RectTransform debugSkipThreeStarRoot;
        private Tween debugSkipPulseTween;

        /// <summary>
        /// 打开时缓存节点引用。
        /// </summary>
        protected override void OnPanelOpen(TavernHudPanelData data)
        {
            Signals.Get<TavernBusinessStateSignal>().RemoveListener(HandleBusinessStateChanged);
            Signals.Get<TavernBusinessStateSignal>().AddListener(HandleBusinessStateChanged);
            Signals.Get<TavernPrestigeChangedSignal>().RemoveListener(HandlePrestigeChanged);
            Signals.Get<TavernPrestigeChangedSignal>().AddListener(HandlePrestigeChanged);
            Signals.Get<TavernRuntimeChangedSignal>().RemoveListener(HandlePrestigeChanged);
            Signals.Get<TavernRuntimeChangedSignal>().AddListener(HandlePrestigeChanged);
            CoinDisplayRefreshCoordinator.GoldRefreshArrived -= HandleCoinFlyArrived;
            CoinDisplayRefreshCoordinator.GoldRefreshArrived += HandleCoinFlyArrived;
            BindNodes();
            CacheCoinTarget();
            HideCustomerEnterProgressUi();
            HideRuntimeInfoGroup();
            // 倒计时仅营业中；员工/科技入口已移至底栏
            ShowClock(IsTavernOpen());
            EnsureBusinessCountdownRunning();
            RefreshFeatureEntries();
            RefreshPrestigeUi();
            RefreshMenuStatusUi();
            RefreshTavernLevelImage();
            var visiting = DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern;
            RefreshVisitHudMode(visiting);
            if (visiting)
            {
                RefreshOtherTavernHud();
            }

            EnsureDebugSkipThreeStarButton();
            RefreshDebugSkipThreeStarButtonVisibility();
        }

        /// <summary>
        /// 显示时恢复顶部状态节点并刷新内容。
        /// </summary>
        protected override void OnPanelShow()
        {
            SetManagedNodesVisible(true);
            CacheCoinTarget();
            HideCustomerEnterProgressUi();
            RefreshPanel();
            transform.SetAsLastSibling();
        }

        /// <summary>
        /// 关闭时停止倒计时和浮动动画，并恢复世界进度条。
        /// </summary>
        protected override void OnPanelClose()
        {
            Signals.Get<TavernBusinessStateSignal>().RemoveListener(HandleBusinessStateChanged);
            Signals.Get<TavernPrestigeChangedSignal>().RemoveListener(HandlePrestigeChanged);
            Signals.Get<TavernRuntimeChangedSignal>().RemoveListener(HandlePrestigeChanged);
            CoinDisplayRefreshCoordinator.GoldRefreshArrived -= HandleCoinFlyArrived;
            StopGoldNumScaleTween();
            StopPrestigeGainPresentation(restoreText: true);
            StopBusinessCountdown();
            StopCoinDelta();
            if (debugSkipThreeStarButton != null)
            {
                debugSkipThreeStarButton.onClick.RemoveListener(OnClickDebugSkipThreeStar);
            }

            StopDebugSkipThreeStarPulse();

            TavernSceneManager.Instance?.SetWorldCustomerEnterProgressVisible(true);
            SetManagedNodesVisible(false);
        }

        /// <summary>
        /// 刷新顶部状态的所有显示内容。
        /// </summary>
        public void RefreshPanel()
        {
            BindNodes();
            CacheCoinTarget();
            HideCustomerEnterProgressUi();
            var visiting = DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern;
            RefreshVisitHudMode(visiting);
            ShowClock(IsTavernOpen());
            EnsureBusinessCountdownRunning();
            RefreshFeatureEntries();
            RefreshPrestigeUi();
            RefreshMenuStatusUi();
            if (visiting)
            {
                RefreshOtherTavernHud();
                return;
            }

            if (DataManager.Instance == null || DataManager.Instance.PlayerData == null)
            {
                return;
            }

            // 头像旁玩家名不显示；店名默认用玩家名。
            if (playerNameText != null)
            {
                playerNameText.gameObject.SetActive(false);
            }

            if (tavernNameText != null)
            {
                tavernNameText.gameObject.SetActive(true);
                tavernNameText.text = DataManager.Instance.PlayerData.playerName;
            }

            RefreshGoldVisibility(true);
            HideCoinIcon();
            if (goldNumText != null)
            {
                SyncDisplayedCoinNum(forceSyncGold: !CoinDisplayRefreshCoordinator.ShouldDeferGoldRefresh);
                goldNumText.text = displayedCoinNum.ToString();
            }

            RefreshPlayAvatarIcon(playAvatarIcon, visiting: false);
            HideRuntimeInfoGroup();
            RefreshTavernLevelImage();
            RefreshTaskText();
            EnsureDebugSkipThreeStarButton();
            RefreshDebugSkipThreeStarButtonVisibility();
        }

        /// <summary>
        /// 拜访他人酒楼显示 group_other，自家显示 group_TopBar。
        /// </summary>
        private void RefreshVisitHudMode(bool visiting)
        {
            BindNodes();
            if (topBarRoot != null && topBarRoot.gameObject.activeSelf == visiting)
            {
                topBarRoot.gameObject.SetActive(!visiting);
            }

            if (otherGroupRoot != null && otherGroupRoot.gameObject.activeSelf != visiting)
            {
                otherGroupRoot.gameObject.SetActive(visiting);
            }

            RefreshMenuStatusUi();
            EnsureDebugSkipThreeStarButton();
            RefreshDebugSkipThreeStarButtonVisibility();
        }

        /// <summary>
        /// 拜访他人酒楼顶部：店名、星级图、头像、今日盈利（按自家星级基础值 ±5000）。
        /// </summary>
        private void RefreshOtherTavernHud()
        {
            BindNodes();
            var dataManager = DataManager.Instance;
            if (otherTavernNameText != null)
            {
                otherTavernNameText.text = dataManager != null
                    && !string.IsNullOrWhiteSpace(dataManager.VisitingShopName)
                    ? dataManager.VisitingShopName
                    : "他人酒楼";
            }

            RefreshOtherTavernLevelImage();
            RefreshPlayAvatarIcon(otherPlayAvatarIcon, visiting: true);
            if (otherMoneyText != null)
            {
                otherMoneyText.text = $"今日盈利：{ResolveVisitTodayProfit()}";
            }
        }

        private void RefreshOtherTavernLevelImage()
        {
            if (otherTavernLevelImage == null)
            {
                return;
            }

            var dataManager = DataManager.Instance;
            var level = dataManager != null ? dataManager.VisitingTavernLevel : 1;
            var spriteLevel = Mathf.Clamp(level, 0, 4);
            if (cachedOtherTavernLevelSprite == spriteLevel && otherTavernLevelImage.sprite != null)
            {
                return;
            }

            var sprite = GameplayResourceStore.LoadAsset<Sprite>(
                string.Format(TavernLevelSpritePathFormat, spriteLevel));
            if (sprite == null)
            {
                return;
            }

            otherTavernLevelImage.sprite = sprite;
            otherTavernLevelImage.preserveAspect = true;
            cachedOtherTavernLevelSprite = spriteLevel;
        }

        /// <summary>
        /// 今日盈利：按当前玩家自家星级取基础值，再 ±5000；同一次拜访内不变。
        /// </summary>
        private int ResolveVisitTodayProfit()
        {
            var dataManager = DataManager.Instance;
            var tileId = dataManager != null ? dataManager.VisitingTileId : 0;
            if (cachedVisitProfitTileId == tileId && tileId > 0)
            {
                return cachedVisitProfitAmount;
            }

            var ownLevel = dataManager != null ? dataManager.GetTavernLevel() : 1;
            var baseProfit = ownLevel >= 3
                ? VisitProfitStar3Base
                : ownLevel >= 2
                    ? VisitProfitStar2Base
                    : VisitProfitStar1Base;
            cachedVisitProfitAmount = Mathf.Max(0, baseProfit + UnityEngine.Random.Range(-VisitProfitRandomRange, VisitProfitRandomRange + 1));
            cachedVisitProfitTileId = tileId;
            return cachedVisitProfitAmount;
        }

        /// <summary>
        /// 顶部头像：拜访用对应酒楼 TownBuilding.headIconId；自家用默认 tx.png。
        /// </summary>
        private void RefreshPlayAvatarIcon(Image target, bool visiting)
        {
            BindNodes();
            if (target == null)
            {
                return;
            }

            const string selfDefaultHeadIconPath =
                "Assets/Res/Resources/Textures/UI/CreatePlayer/tx.png";
            const string headIconPathFormat = "Assets/Res/Resources/UI/HeadIcon/{0}.png";

            Sprite sprite = null;
            if (visiting && DataManager.Instance != null)
            {
                var headIconId = TownBuildingConfigUtility.GetHeadIconIdByFieldId(
                    DataManager.Instance.VisitingTileId);
                if (headIconId >= 1 && headIconId <= 8)
                {
                    sprite = GameplayResourceStore.LoadAsset<Sprite>(
                        string.Format(headIconPathFormat, headIconId));
                }
            }
            else
            {
                sprite = GameplayResourceStore.LoadAsset<Sprite>(selfDefaultHeadIconPath);
            }

            if (sprite == null)
            {
                target.enabled = true;
                return;
            }

            target.sprite = sprite;
            target.preserveAspect = true;
            target.enabled = true;
        }

        /// <summary>
        /// 顶部菜单运行时不显示金钱图标，只保留数额。
        /// </summary>
        private void HideCoinIcon()
        {
            var coinIcon = HudBindingUtility.FindChildRecursive(transform, "img_CoinIcon");
            if (coinIcon != null && coinIcon.gameObject.activeSelf)
            {
                coinIcon.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 访客模式隐藏金钱显示；自家酒楼恢复显示。
        /// </summary>
        private void RefreshGoldVisibility(bool visible)
        {
            BindNodes();
            var goldRoot = ResolveGoldDisplayRoot();
            if (goldRoot != null)
            {
                goldRoot.gameObject.SetActive(visible);
            }

            if (goldNumText != null)
            {
                goldNumText.gameObject.SetActive(visible);
            }

            if (changeGoldText != null)
            {
                changeGoldText.gameObject.SetActive(visible);
            }

            HideCoinIcon();
        }

        private Transform ResolveGoldDisplayRoot()
        {
            if (groupGoldNum != null)
            {
                // BindNodes 可能绑到图标；优先隐藏整块金钱组。
                if (groupGoldNum.name.IndexOf("Gold", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || groupGoldNum.name.Contains("Coin"))
                {
                    return groupGoldNum;
                }

                var parent = groupGoldNum.parent;
                if (parent != null
                    && (parent.name.IndexOf("Gold", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || parent.name.Contains("Coin")))
                {
                    return parent;
                }

                return groupGoldNum;
            }

            return null;
        }

        /// <summary>
        /// 响应金币变化，并播放顶部金币浮动文本。
        /// </summary>
        public void HandleCoinChanged(int changeNum)
        {
            if (DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern)
            {
                return;
            }

            if (changeNum < 0)
            {
                SyncDisplayedCoinNum(forceSyncGold: true);
                ApplyGoldDisplay();
                PlayGoldSpendScalePulse();
                PlayCoinDelta(changeNum);
                return;
            }

            RefreshPanel();

            if (changeNum <= 0)
            {
                return;
            }

            if (CoinDisplayRefreshCoordinator.ShouldDeferGoldRefresh)
            {
                CoinDisplayRefreshCoordinator.RegisterPendingPositiveDisplay(changeNum);
                return;
            }

            PlayCoinDelta(changeNum);
        }

        private void HandleCoinFlyArrived()
        {
            var pendingDelta = CoinDisplayRefreshCoordinator.ConsumePendingPositiveDisplay();
            if (pendingDelta > 0)
            {
                PlayCoinDelta(pendingDelta);
            }

            PlayGoldIncomeScaleThenRefresh();
        }

        private void PlayCoinDelta(int changeNum)
        {
            if (changeGoldText == null || changeNum == 0)
            {
                return;
            }

            if (changeNum > 0)
            {
                changeGoldText.text = $"+{changeNum}";
                changeGoldText.color = Color.green;
            }
            else
            {
                changeGoldText.text = changeNum.ToString();
                changeGoldText.color = Color.red;
            }

            StopCoinDelta();
            coinDeltaRoutine = StartCoroutine(CoinDeltaAnim(changeGoldText.rectTransform));
        }

        private void SyncDisplayedCoinNum(bool forceSyncGold)
        {
            if (DataManager.Instance?.PlayerData == null)
            {
                return;
            }

            if (forceSyncGold || displayedCoinNum < 0)
            {
                displayedCoinNum = DataManager.Instance.PlayerData.coinNum;
            }
        }

        private void ApplyGoldDisplay()
        {
            if (goldNumText != null)
            {
                goldNumText.text = displayedCoinNum.ToString();
            }
        }

        private void PlayGoldIncomeScaleThenRefresh()
        {
            var scaleTarget = ResolveGoldScaleTarget();
            if (scaleTarget == null)
            {
                SyncDisplayedCoinNum(forceSyncGold: true);
                ApplyGoldDisplay();
                return;
            }

            StopGoldNumScaleTween();
            scaleTarget.localScale = Vector3.one;
            goldNumScaleTween = DOTween.Sequence()
                .Append(scaleTarget.DOScale(1.28f, 0.12f).SetEase(Ease.OutQuad))
                .AppendCallback(() =>
                {
                    SyncDisplayedCoinNum(forceSyncGold: true);
                    ApplyGoldDisplay();
                })
                .Append(scaleTarget.DOScale(1f, 0.18f).SetEase(Ease.OutBack))
                .OnKill(() => goldNumScaleTween = null)
                .OnComplete(() => goldNumScaleTween = null);
        }

        private void PlayGoldSpendScalePulse()
        {
            var scaleTarget = ResolveGoldScaleTarget();
            if (scaleTarget == null)
            {
                return;
            }

            StopGoldNumScaleTween();
            scaleTarget.localScale = Vector3.one;
            goldNumScaleTween = scaleTarget
                .DOPunchScale(Vector3.one * 0.14f, 0.28f, 6, 0.55f)
                .OnKill(() => goldNumScaleTween = null)
                .OnComplete(() => goldNumScaleTween = null);
        }

        private RectTransform ResolveGoldScaleTarget()
        {
            if (goldNumText != null)
            {
                return goldNumText.rectTransform;
            }

            return groupGoldNum as RectTransform;
        }

        private void StopGoldNumScaleTween()
        {
            if (goldNumScaleTween == null)
            {
                return;
            }

            goldNumScaleTween.Kill();
            goldNumScaleTween = null;

            var scaleTarget = ResolveGoldScaleTarget();
            if (scaleTarget != null)
            {
                scaleTarget.localScale = Vector3.one;
            }
        }

        /// <summary>
        /// 营业开关只驱动倒计时；员工/科技入口在底栏刷新。
        /// </summary>
        public void HandleBusinessStateChanged(bool isOpen)
        {
            BindNodes();
            HideCustomerEnterProgressUi();
            ShowClock(isOpen);
            UIKit.GetPanel<TavernBottomNavPanelController>()?.RefreshPanel();
            if (isOpen)
            {
                EnsureBusinessCountdownRunning(forceRestart: true);
                return;
            }

            StopBusinessCountdown();
        }

        private static bool IsTavernOpen()
        {
            return DataManager.Instance != null
                   && DataManager.Instance.TavernData != null
                   && DataManager.Instance.TavernData.isOpen;
        }

        /// <summary>
        /// 营业中确保倒计时协程在跑（进店恢复营业时可能错过 BusinessState 信号）。
        /// </summary>
        private void EnsureBusinessCountdownRunning(bool forceRestart = false)
        {
            if (!IsTavernOpen())
            {
                StopBusinessCountdown();
                return;
            }

            if (forceRestart)
            {
                StopBusinessCountdown();
            }

            if (businessRemainRoutine == null)
            {
                businessRemainRoutine = StartCoroutine(RemainingBusinessHours());
            }
        }

        /// <summary>
        /// 停止营业剩余时间倒计时协程。
        /// </summary>
        public void StopBusinessCountdown()
        {
            businessRemainEpoch++;
            businessRemainRoutine = null;
        }

        /// <summary>
        /// 查找并缓存顶部状态面板的节点引用。
        /// </summary>
        private void BindNodes()
        {
            var hudRoot = transform;

            topBarRoot ??= hudRoot.Find("group_TopBar") as RectTransform
                           ?? HudBindingUtility.FindChildRecursive(hudRoot, "group_TopBar") as RectTransform;
            BindOtherHudNodes();

            groupGoldNum ??= HudBindingUtility.FindChildRecursive(hudRoot, "@group_GoldNum")
                             ?? HudBindingUtility.FindChildRecursive(hudRoot, "group_GoldNum");
            HideCoinIcon();
            playerNameText ??= hudRoot.Find("group_TopBar/group_PlayerAvatar/@txt_PlayerName")?.GetComponent<TextMeshProUGUI>()
                               ?? hudRoot.Find("group_TopBar/group_PlayerAvatar/txt_PlayerName")?.GetComponent<TextMeshProUGUI>()
                               ?? HudBindingUtility.FindChildRecursive(hudRoot, "txt_PlayerName")?.GetComponent<TextMeshProUGUI>();
            playAvatarIcon ??= topBarRoot != null
                ? HudBindingUtility.FindChildRecursive(topBarRoot, "img_PlayAvatarIcon")?.GetComponent<Image>()
                : null;
            tavernNameText ??= hudRoot.Find("group_TopBar/group_TavernName/@txt_TavernName")?.GetComponent<TextMeshProUGUI>()
                               ?? hudRoot.Find("group_TopBar/group_TavernName/txt_TavernName")?.GetComponent<TextMeshProUGUI>();
            goldNumText ??= HudBindingUtility.FindChildRecursive(hudRoot, "txt_GoldNum")?.GetComponent<TextMeshProUGUI>();
            runtimeInfoRoot ??= hudRoot.Find("group_TopBar/group_RuntimeInfo") as RectTransform
                                ?? HudBindingUtility.FindChildRecursive(hudRoot, "group_RuntimeInfo") as RectTransform;
            levelText ??= hudRoot.Find("group_TopBar/group_RuntimeInfo/txt_Level")?.GetComponent<TextMeshProUGUI>()
                          ?? HudBindingUtility.FindChildRecursive(hudRoot, "txt_Level")?.GetComponent<TextMeshProUGUI>();
            runtimeInfoText ??= HudBindingUtility.FindChildRecursive(hudRoot, "txt_RuntimeInfo")?.GetComponent<TextMeshProUGUI>();
            taskText ??= HudBindingUtility.FindChildRecursive(hudRoot, "txt_Task")?.GetComponent<TextMeshProUGUI>();
            BindTavernLevelButton();
            HideRuntimeInfoGroup();
            clockTipsRoot ??= HudBindingUtility.FindChildRecursive(hudRoot, "tips_ClockDown") as RectTransform;
            // prefab 里 clockRoot 曾误绑到 tips_ClockDown；优先用 tips 下的 Clock 子节点
            if (clockRoot != null && clockTipsRoot != null && clockRoot == clockTipsRoot)
            {
                clockRoot = clockTipsRoot.Find("Clock") as RectTransform
                            ?? HudBindingUtility.FindChildRecursive(clockTipsRoot, "Clock") as RectTransform;
            }

            clockRoot ??= HudBindingUtility.FindChildRecursive(hudRoot, "Clock") as RectTransform;
            clockDownText ??= HudBindingUtility.FindChildRecursive(hudRoot, "clockDownText")?.GetComponent<TMP_Text>()
                              ?? HudBindingUtility.FindChildRecursive(hudRoot, "ClockDown")?.GetComponent<TMP_Text>();
            if (clockTipsRoot == null && clockDownText != null)
            {
                clockTipsRoot = clockDownText.transform.parent as RectTransform;
            }

            // tips_ClockDown 永久隐藏。
            HideClockTips();

            if (changeGoldText == null)
            {
                changeGoldText = hudRoot.Find("group_TopBar/@group_GoldNum/@txt_ChangeGoldNum")?.GetComponent<TextMeshProUGUI>()
                                 ?? hudRoot.Find("group_TopBar/@group_GoldNum/txt_ChangeGoldNum")?.GetComponent<TextMeshProUGUI>()
                                 ?? HudBindingUtility.FindChildRecursive(hudRoot, "@txt_ChangeGoldNum")?.GetComponent<TextMeshProUGUI>()
                                 ?? HudBindingUtility.FindChildRecursive(hudRoot, "txt_ChangeGoldNum")?.GetComponent<TextMeshProUGUI>();
            }

            if (changeGoldText != null && !hasCoinDeltaBasePosition)
            {
                coinDeltaBasePosition = changeGoldText.rectTransform.anchoredPosition;
                hasCoinDeltaBasePosition = true;
                var canvasGroup = changeGoldText.GetComponent<CanvasGroup>() ?? changeGoldText.gameObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 0f;
            }

            BindPrestigeNodes();
            BindMenuNodes();

            if (customerEnterProgressRoot == null)
            {
                customerEnterProgressRoot = HudBindingUtility.FindChildRecursive(hudRoot, "group_CustomerEnterProgress") as RectTransform;
            }

            // group_CustomerEnterProgress 暂隐藏，进度刷新逻辑保留不删。
            HideCustomerEnterProgressUi();
            if (customerEnterProgressRoot == null)
            {
                return;
            }

            customerEnterProgressBackground ??= customerEnterProgressRoot.Find("img_ProgressBg")?.GetComponent<Image>();
            customerEnterProgressFill ??= customerEnterProgressRoot.Find("img_ProgressBg/img_ProgressFill")?.GetComponent<Image>();
            customerEnterQueueBackground ??= customerEnterProgressRoot.Find("img_QueueBg")?.GetComponent<Image>();
            customerEnterProgressText ??= customerEnterProgressRoot.Find("txt_Time")?.GetComponent<TMP_Text>()
                                           ?? customerEnterProgressRoot.GetComponentInChildren<TMP_Text>(true);

            if (customerEnterProgressFill != null && customerEnterDefaultFillSprite == null)
            {
                customerEnterDefaultFillSprite = customerEnterProgressFill.sprite;
            }

            customerEnterQueueFillSprite ??= GameplayResourceStore.LoadAsset<Sprite>(CustomerEnterQueueFillSpritePath);
        }

        /// <summary>
        /// 绑定声望 UI（group_presitige）；顶栏 btn_Upgrade 已废弃，强制隐藏。
        /// </summary>
        private void BindPrestigeNodes()
        {
            var hudRoot = transform;
            if (prestigeRoot == null)
            {
                prestigeRoot = transform.Find("group_presitige") as RectTransform
                               ?? HudBindingUtility.FindChildRecursive(hudRoot, "group_presitige") as RectTransform;
            }

            if (prestigeRoot == null)
            {
                return;
            }

            CachePrestigeTarget();

            if (prestigeBar == null)
            {
                prestigeBar = prestigeRoot.Find("img_bar")?.GetComponent<Image>();
            }

            if (prestigeNameText == null)
            {
                prestigeNameText = prestigeRoot.Find("txt_name")?.GetComponent<TMP_Text>();
            }

            // 每次以直属子节点为准，避免误绑到升级按钮上的同名文本。
            var valueNode = prestigeRoot.Find("txt_presitige");
            if (valueNode != null)
            {
                prestigeValueText = valueNode.GetComponent<TMP_Text>();
            }

            // 升级入口已移至底栏 btn_Upgrade，顶栏按钮始终隐藏。
            var upgradeNode = prestigeRoot.Find("btn_Upgrade")
                              ?? prestigeRoot.Find("btn_upgrade")
                              ?? HudBindingUtility.FindChildRecursive(prestigeRoot, "btn_Upgrade")
                              ?? HudBindingUtility.FindChildRecursive(prestigeRoot, "btn_upgrade");
            if (upgradeNode != null && upgradeNode.gameObject.activeSelf)
            {
                upgradeNode.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 绑定 group_menu：隐藏旧 bg，绑定 img_curMenu 与效果文案。
        /// </summary>
        private void BindMenuNodes()
        {
            if (menuGroupRoot == null)
            {
                menuGroupRoot = transform.Find("group_menu") as RectTransform
                                ?? HudBindingUtility.FindChildRecursive(transform, "group_menu") as RectTransform;
            }

            if (menuGroupRoot == null)
            {
                return;
            }

            if (menuBgRoot == null)
            {
                menuBgRoot = menuGroupRoot.Find("bg")?.gameObject
                             ?? HudBindingUtility.FindChildRecursive(menuGroupRoot, "bg")?.gameObject;
            }

            if (menuBgRoot != null && menuBgRoot.activeSelf)
            {
                menuBgRoot.SetActive(false);
            }

            if (curMenuImage == null)
            {
                var imageNode = menuGroupRoot.Find("img_curMenu")
                                ?? HudBindingUtility.FindChildRecursive(menuGroupRoot, "img_curMenu");
                curMenuImage = imageNode != null ? imageNode.GetComponent<Image>() : null;
            }

            // 兼容旧结构：txt_curMenu 在 bg 下；bg 隐藏后不再依赖该文案。
            if (curMenuText == null)
            {
                curMenuText = HudBindingUtility.ResolveChildText(menuGroupRoot, "txt_curMenu");
            }

            if (curMenuEffectText == null)
            {
                curMenuEffectText = HudBindingUtility.ResolveChildText(menuGroupRoot, "txt_curMenuEffect");
            }

            popularMenuTitleSprite ??= GameplayResourceStore.LoadAsset<Sprite>(PopularMenuTitleSpritePath);
            vipMenuTitleSprite ??= GameplayResourceStore.LoadAsset<Sprite>(VipMenuTitleSpritePath);
        }

        /// <summary>
        /// 菜单切换后立刻刷新已打开的顶栏 group_menu（不依赖信号时序）。
        /// </summary>
        public static void RefreshOpenedMenuStatusUi()
        {
            UIKit.GetPanel<TavernTopStatusPanelController>()?.RefreshMenuStatusUi();
        }

        /// <summary>
        /// 解析顶栏金币落点（飞钱动画用）；二楼等场景会先尝试刷新缓存。
        /// </summary>
        public static Transform ResolveCoinFlyTarget()
        {
            var panel = UIKit.GetPanel<TavernTopStatusPanelController>();
            panel?.CacheCoinTarget();
            return GOReferenceManager.Instance != null
                ? GOReferenceManager.Instance.GetCoinTransform()
                : null;
        }

        /// <summary>
        /// 二星及以上自家酒楼显示当前菜单图与效果；拜访他人店隐藏。
        /// </summary>
        public void RefreshMenuStatusUi()
        {
            BindMenuNodes();
            if (menuGroupRoot == null)
            {
                return;
            }

            var dataManager = DataManager.Instance;
            var show = dataManager != null && dataManager.ShouldShowTavernMenuEntry();
            if (menuGroupRoot.gameObject.activeSelf != show)
            {
                menuGroupRoot.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            // 始终藏旧 bg，避免预制体默认开启。
            if (menuBgRoot != null && menuBgRoot.activeSelf)
            {
                menuBgRoot.SetActive(false);
            }

            menuGroupRoot.SetAsLastSibling();
            var vipMenu = dataManager.IsVipMenuSelected();
            if (curMenuImage != null)
            {
                var sprite = vipMenu ? vipMenuTitleSprite : popularMenuTitleSprite;
                if (sprite != null && curMenuImage.sprite != sprite)
                {
                    curMenuImage.sprite = sprite;
                }

                if (!curMenuImage.enabled)
                {
                    curMenuImage.enabled = true;
                }

                if (!curMenuImage.gameObject.activeSelf)
                {
                    curMenuImage.gameObject.SetActive(true);
                }
            }

            if (curMenuEffectText != null)
            {
                curMenuEffectText.text = string.Empty;
                if (curMenuEffectText.gameObject.activeSelf)
                {
                    curMenuEffectText.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// 绑定 group_other：店名 / 星级图 / 头像 / 今日盈利。
        /// </summary>
        private void BindOtherHudNodes()
        {
            otherGroupRoot ??= transform.Find("group_other") as RectTransform
                               ?? HudBindingUtility.FindChildRecursive(transform, "group_other") as RectTransform;
            if (otherGroupRoot == null)
            {
                return;
            }

            otherTavernNameText ??= ResolveNamedTmp(otherGroupRoot, "@txt_TavernName")
                                    ?? ResolveNamedTmp(otherGroupRoot, "txt_TavernName");
            otherTavernLevelImage ??= HudBindingUtility.ResolveChildImage(otherGroupRoot, "img_Tavern");
            otherPlayAvatarIcon ??= HudBindingUtility.ResolveChildImage(otherGroupRoot, "img_PlayAvatarIcon");
            otherMoneyText ??= ResolveNamedTmp(otherGroupRoot, "txt_money");
        }

        private static TMP_Text ResolveNamedTmp(Transform root, string nodeName)
        {
            return HudBindingUtility.ResolveChildText(root, nodeName);
        }

        /// <summary>
        /// 绑定酒楼星级展示（btn_Tavern 自身 Image 显示 lvN 图；不再响应点击弹窗）。
        /// </summary>
        private void BindTavernLevelButton()
        {
            var tavernButtonNode = HudBindingUtility.FindChildRecursive(transform, "btn_Tavern");
            if (tavernButtonNode == null)
            {
                return;
            }

            tavernLevelButton ??= tavernButtonNode.GetComponent<Button>();
            tavernLevelImage ??= tavernLevelButton != null
                ? tavernLevelButton.GetComponent<Image>()
                : tavernButtonNode.GetComponent<Image>();
            tavernLevelImage ??= tavernButtonNode.GetComponent<Image>();

            if (tavernLevelButton != null)
            {
                tavernLevelButton.interactable = false;
            }

            if (tavernLevelImage != null)
            {
                tavernLevelImage.raycastTarget = false;
            }

            tavernLevelButtonBound = true;
        }

        private void HandlePrestigeChanged()
        {
            RefreshPrestigeUi();
            RefreshTavernLevelImage();
            RefreshMenuStatusUi();
            ShowClock(IsTavernOpen());
        }

        /// <summary>
        /// 刷新声望进度；拜访他人店时隐藏整个 group_presitige。
        /// 声望增加时进度条过渡，文字后追加绿色 (+X)，2 秒后恢复。
        /// </summary>
        private void RefreshPrestigeUi()
        {
            BindPrestigeNodes();
            if (prestigeRoot == null)
            {
                return;
            }

            var visiting = DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern;
            prestigeRoot.gameObject.SetActive(!visiting);
            if (visiting)
            {
                StopPrestigeGainPresentation(restoreText: false);
                displayedPrestige = -1;
                return;
            }

            var prestige = DataManager.Instance != null ? DataManager.Instance.GetTavernPrestige() : 0;
            var required = DataManager.Instance != null
                ? Mathf.Max(1, DataManager.Instance.GetNextTavernPrestigeRequirement())
                : 90;
            var targetFill = Mathf.Clamp01((float)prestige / required);
            var initialized = displayedPrestige >= 0;
            var gained = initialized && prestige > displayedPrestige && required == displayedPrestigeRequired;
            var delta = gained ? prestige - displayedPrestige : 0;

            displayedPrestige = prestige;
            displayedPrestigeRequired = required;
            EnsurePrestigeBarFillSetup();

            if (gained)
            {
                prestigeGainShown += delta;
                PlayPrestigeBarFillTween(targetFill);
                ApplyPrestigeValueText(prestige, required, prestigeGainShown);
                RestartPrestigeGainRestoreRoutine();
                return;
            }

            if (prestigeGainShown > 0)
            {
                ApplyPrestigeValueText(prestige, required, prestigeGainShown);
                if (prestigeBar != null && prestigeBarTween == null)
                {
                    prestigeBar.fillAmount = targetFill;
                }

                return;
            }

            StopPrestigeBarTween();
            ApplyPrestigeValueText(prestige, required, 0);
            if (prestigeBar != null)
            {
                prestigeBar.fillAmount = targetFill;
            }
        }

        private void EnsurePrestigeBarFillSetup()
        {
            if (prestigeBar == null)
            {
                return;
            }

            prestigeBar.type = Image.Type.Filled;
            prestigeBar.fillMethod = Image.FillMethod.Horizontal;
            prestigeBar.fillOrigin = (int)Image.OriginHorizontal.Left;
        }

        private void PlayPrestigeBarFillTween(float targetFill)
        {
            if (prestigeBar == null)
            {
                return;
            }

            StopPrestigeBarTween();
            prestigeBarTween = prestigeBar
                .DOFillAmount(targetFill, 0.45f)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .OnComplete(() => prestigeBarTween = null);
        }

        private void ApplyPrestigeValueText(int prestige, int required, int gain)
        {
            if (prestigeValueText == null)
            {
                return;
            }

            if (gain > 0)
            {
                prestigeValueText.text = $"声望{prestige}/{required} <color=#3CFF64>(+{gain})</color>";
                return;
            }

            prestigeValueText.SetText("声望{0}/{1}", prestige, required);
        }

        private void RestartPrestigeGainRestoreRoutine()
        {
            if (prestigeGainRoutine != null)
            {
                StopCoroutine(prestigeGainRoutine);
            }

            prestigeGainRoutine = StartCoroutine(RestorePrestigeGainTextRoutine());
        }

        private IEnumerator RestorePrestigeGainTextRoutine()
        {
            yield return new WaitForSeconds(2f);
            prestigeGainShown = 0;
            prestigeGainRoutine = null;
            ApplyPrestigeValueText(displayedPrestige, displayedPrestigeRequired, 0);
        }

        private void StopPrestigeGainPresentation(bool restoreText)
        {
            if (prestigeGainRoutine != null)
            {
                StopCoroutine(prestigeGainRoutine);
                prestigeGainRoutine = null;
            }

            prestigeGainShown = 0;
            StopPrestigeBarTween();
            if (restoreText)
            {
                ApplyPrestigeValueText(Mathf.Max(0, displayedPrestige), Mathf.Max(1, displayedPrestigeRequired), 0);
            }
        }

        private void StopPrestigeBarTween()
        {
            if (prestigeBarTween == null)
            {
                return;
            }

            prestigeBarTween.Kill();
            prestigeBarTween = null;
        }

        /// <summary>
        /// btn_Tavern：按当前酒楼星级切换 lv0~lvN 图。
        /// </summary>
        private void RefreshTavernLevelImage()
        {
            BindTavernLevelButton();
            if (tavernLevelImage == null)
            {
                return;
            }

            var level = DataManager.Instance != null ? DataManager.Instance.GetTavernLevel() : 0;
            // 0 星用 lv0；最高 clamp 到 4。
            var spriteLevel = Mathf.Clamp(level, 0, 4);
            if (cachedTavernLevelSprite == spriteLevel && tavernLevelImage.sprite != null)
            {
                return;
            }

            var sprite = GameplayResourceStore.LoadAsset<Sprite>(string.Format(TavernLevelSpritePathFormat, spriteLevel));
            if (sprite != null)
            {
                tavernLevelImage.sprite = sprite;
                cachedTavernLevelSprite = spriteLevel;
            }
        }

        /// <summary>
        /// group_TopBar 下员工/科技入口已废弃，统一隐藏（入口移至 TavernBottomNavPanelController）。
        /// </summary>
        private void EnsureFeatureEntryRefs()
        {
            staffEntryRoot ??= ResolveTopBarChild(StaffEntryNodeName);
            techEntryRoot ??= ResolveTopBarChild(TechEntryNodeName);
            if (staffEntryRoot != null)
            {
                staffEntryRoot.gameObject.SetActive(false);
            }

            if (techEntryRoot != null)
            {
                techEntryRoot.gameObject.SetActive(false);
            }
        }

        private void RefreshFeatureEntries()
        {
            EnsureFeatureEntryRefs();
        }

        private RectTransform ResolveTopBarChild(string nodeName)
        {
            return transform.Find($"group_TopBar/{nodeName}") as RectTransform
                   ?? HudBindingUtility.FindChildRecursive(transform, nodeName) as RectTransform;
        }

        private void CacheCoinTarget()
        {
            if (GOReferenceManager.Instance == null)
            {
                return;
            }

            var target = goldNumText != null
                ? goldNumText.rectTransform
                : groupGoldNum;
            if (target != null)
            {
                GOReferenceManager.Instance.SaveCoinTransform(target);
            }
        }

        /// <summary>
        /// 缓存声望栏，供设施建造飞声望落点。
        /// </summary>
        private void CachePrestigeTarget()
        {
            if (GOReferenceManager.Instance == null || prestigeRoot == null)
            {
                return;
            }

            GOReferenceManager.Instance.SavePrestigeTransform(prestigeRoot);
        }

        /// <summary>
        /// 隐藏顾客进入进度条 UI（功能暂停，节点保留）。
        /// </summary>
        private void HideCustomerEnterProgressUi()
        {
            if (customerEnterProgressRoot == null)
            {
                customerEnterProgressRoot = HudBindingUtility.FindChildRecursive(transform, "group_CustomerEnterProgress") as RectTransform;
            }

            if (customerEnterProgressRoot != null)
            {
                customerEnterProgressRoot.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// group_RuntimeInfo 已废弃，统一隐藏。
        /// </summary>
        private void HideRuntimeInfoGroup()
        {
            if (runtimeInfoRoot == null)
            {
                runtimeInfoRoot = transform.Find("group_TopBar/group_RuntimeInfo") as RectTransform
                                  ?? HudBindingUtility.FindChildRecursive(transform, "group_RuntimeInfo") as RectTransform;
            }

            if (runtimeInfoRoot != null && runtimeInfoRoot.gameObject.activeSelf)
            {
                runtimeInfoRoot.gameObject.SetActive(false);
            }

            if (runtimeInfoText != null && runtimeInfoText.gameObject.activeSelf)
            {
                runtimeInfoText.gameObject.SetActive(false);
            }

            if (levelText != null && levelText.gameObject.activeSelf)
            {
                levelText.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 当前顶部任务文本已废弃，统一隐藏。
        /// </summary>
        private void RefreshTaskText()
        {
            if (taskText != null && taskText.gameObject.activeSelf)
            {
                taskText.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// tips_ClockDown 永久隐藏（倒计时逻辑可保留，仅不显示 UI）。
        /// </summary>
        private void ShowClock(bool isBusinessOpen)
        {
            _ = isBusinessOpen;
            HideClockTips();
        }

        /// <summary>
        /// 强制隐藏 tips_ClockDown 及其独立 Clock 节点。
        /// </summary>
        private void HideClockTips()
        {
            if (clockTipsRoot != null && clockTipsRoot.gameObject.activeSelf)
            {
                clockTipsRoot.gameObject.SetActive(false);
            }

            if (clockRoot != null
                && (clockTipsRoot == null || clockRoot != clockTipsRoot && !clockRoot.IsChildOf(clockTipsRoot))
                && clockRoot.gameObject.activeSelf)
            {
                clockRoot.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 刷新顾客进入进度条与排队状态（当前已停用，仅保持隐藏）。
        /// </summary>
        private void RefreshCustomerEnterProgress()
        {
            HideCustomerEnterProgressUi();
        }

        /// <summary>
        /// 营业剩余时间倒计时（单轮长时营业，不自动续轮）。
        /// 剩余时间以场景 businessOpenElapsed 为准，支持快照恢复后续跑。
        /// </summary>
        private IEnumerator RemainingBusinessHours()
        {
            var epoch = businessRemainEpoch;
            var sceneManager = TavernSceneManager.Instance;
            if (sceneManager == null)
            {
                businessRemainRoutine = null;
                yield break;
            }

            while (epoch == businessRemainEpoch
                   && DataManager.Instance != null
                   && DataManager.Instance.TavernData != null
                   && DataManager.Instance.TavernData.isOpen)
            {
                RefreshClockDownText(sceneManager.GetBusinessRemainingSeconds());
                yield return null;
            }

            businessRemainRoutine = null;
        }

        private void RefreshClockDownText(float remainingSeconds)
        {
            if (clockDownText == null)
            {
                return;
            }

            var displaySeconds = Mathf.Max(0f, remainingSeconds);
            var time = TimeSpan.FromSeconds(displaySeconds).ToString(@"mm\:ss");
            clockDownText.text = $"剩余时间:{time}";
        }

        /// <summary>
        /// 顶部金币变化文本上浮并淡出的动画。
        /// </summary>
        private IEnumerator CoinDeltaAnim(RectTransform target)
        {
            var canvasGroup = target.GetComponent<CanvasGroup>() ?? target.gameObject.AddComponent<CanvasGroup>();
            var elapsed = 0f;
            const float duration = 1f;
            var start = coinDeltaBasePosition;
            var end = start + new Vector2(0f, 80f);

            canvasGroup.alpha = 1f;
            target.gameObject.SetActive(true);
            target.SetAsLastSibling();
            target.anchoredPosition = start;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var progress = Mathf.Clamp01(elapsed / duration);
                target.anchoredPosition = Vector2.Lerp(start, end, progress);
                canvasGroup.alpha = 1f - progress;
                yield return null;
            }

            canvasGroup.alpha = 0f;
            target.anchoredPosition = start;
            coinDeltaRoutine = null;
        }

        /// <summary>
        /// 停止金币变化动画。
        /// </summary>
        private void StopCoinDelta()
        {
            if (coinDeltaRoutine != null)
            {
                StopCoroutine(coinDeltaRoutine);
                coinDeltaRoutine = null;
            }
        }

        /// <summary>
        /// 统一控制顶部节点显隐。
        /// </summary>
        private void SetManagedNodesVisible(bool visible)
        {
            BindNodes();
            if (!visible)
            {
                if (topBarRoot != null)
                {
                    topBarRoot.gameObject.SetActive(false);
                }

                if (otherGroupRoot != null)
                {
                    otherGroupRoot.gameObject.SetActive(false);
                }
            }
            else
            {
                var visiting = DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern;
                RefreshVisitHudMode(visiting);
            }

            // group_presitige 与 TopBar 同级，需单独控制显隐。
            BindPrestigeNodes();
            if (prestigeRoot != null)
            {
                var showPrestige = visible
                                  && (DataManager.Instance == null || !DataManager.Instance.IsVisitingOtherTavern);
                prestigeRoot.gameObject.SetActive(showPrestige);
            }

            if (visible)
            {
                HideRuntimeInfoGroup();
                RefreshFeatureEntries();
                RefreshPrestigeUi();
                RefreshMenuStatusUi();
                RefreshTavernLevelImage();
            }

            EnsureDebugSkipThreeStarButton();
            RefreshDebugSkipThreeStarButtonVisibility();
        }

        /// <summary>
        /// 在面板根下创建「跳去三星酒楼」按钮（金按钮底 + 文案）；位置固定在金币组下方。
        /// </summary>
        private void EnsureDebugSkipThreeStarButton()
        {
            BindNodes();
            if (debugSkipThreeStarButton != null && debugSkipThreeStarRoot != null)
            {
                ApplyDebugSkipThreeStarButtonVisual();
                LayoutDebugSkipThreeStarButton();
                return;
            }

            var existing = transform.Find(DebugSkipButtonName) as RectTransform;
            if (existing != null)
            {
                debugSkipThreeStarRoot = existing;
                debugSkipThreeStarButton = existing.GetComponent<Button>()
                                           ?? existing.gameObject.AddComponent<Button>();
                debugSkipThreeStarButton.onClick.RemoveListener(OnClickDebugSkipThreeStar);
                debugSkipThreeStarButton.onClick.AddListener(OnClickDebugSkipThreeStar);
                ApplyDebugSkipThreeStarButtonVisual();
                LayoutDebugSkipThreeStarButton();
                return;
            }

            var buttonGo = new GameObject(DebugSkipButtonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            debugSkipThreeStarRoot = buttonGo.GetComponent<RectTransform>();
            debugSkipThreeStarRoot.SetParent(transform, false);

            debugSkipThreeStarButton = buttonGo.GetComponent<Button>();
            ApplyDebugSkipThreeStarButtonVisual();
            debugSkipThreeStarButton.onClick.AddListener(OnClickDebugSkipThreeStar);

            var textGo = new GameObject("txt_Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.SetParent(debugSkipThreeStarRoot, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20f, 16f);
            textRect.offsetMax = new Vector2(-20f, -16f);

            var label = textGo.GetComponent<TextMeshProUGUI>();
            label.text = DebugSkipButtonLabel;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 28f;
            label.fontSizeMax = 48f;
            label.color = new Color(0.35f, 0.18f, 0.05f, 1f);
            label.raycastTarget = false;
            if (goldNumText != null && goldNumText.font != null)
            {
                label.font = goldNumText.font;
            }
            else if (tavernNameText != null && tavernNameText.font != null)
            {
                label.font = tavernNameText.font;
            }

            LayoutDebugSkipThreeStarButton();
        }

        private void ApplyDebugSkipThreeStarButtonVisual()
        {
            if (debugSkipThreeStarRoot == null)
            {
                return;
            }

            var image = debugSkipThreeStarRoot.GetComponent<Image>()
                        ?? debugSkipThreeStarRoot.gameObject.AddComponent<Image>();
            image.sprite = GameplayResourceStore.LoadAsset<Sprite>(DebugSkipButtonSpritePath);
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = true;
            if (debugSkipThreeStarButton != null)
            {
                debugSkipThreeStarButton.targetGraphic = image;
            }
        }

        private void LayoutDebugSkipThreeStarButton()
        {
            if (debugSkipThreeStarRoot == null)
            {
                return;
            }

            debugSkipThreeStarRoot.SetParent(transform, false);
            debugSkipThreeStarRoot.SetAsLastSibling();

            // 顶部水平居中锚点，固定本地偏移。
            debugSkipThreeStarRoot.anchorMin = new Vector2(0.5f, 1f);
            debugSkipThreeStarRoot.anchorMax = new Vector2(0.5f, 1f);
            debugSkipThreeStarRoot.pivot = new Vector2(0.5f, 0.5f);
            debugSkipThreeStarRoot.sizeDelta = new Vector2(330f, 150f);
            debugSkipThreeStarRoot.anchoredPosition = new Vector2(300f, -250f);
            debugSkipThreeStarRoot.localRotation = Quaternion.identity;
            if (debugSkipPulseTween == null || !debugSkipPulseTween.IsActive())
            {
                debugSkipThreeStarRoot.localScale = Vector3.one * DebugSkipPulseMinScale;
            }
        }

        private void RefreshDebugSkipThreeStarButtonVisibility()
        {
            if (debugSkipThreeStarRoot == null)
            {
                return;
            }

            var show = ShouldShowDebugSkipThreeStarButton();
            if (debugSkipThreeStarRoot.gameObject.activeSelf != show)
            {
                debugSkipThreeStarRoot.gameObject.SetActive(show);
            }

            if (show)
            {
                LayoutDebugSkipThreeStarButton();
                StartDebugSkipThreeStarPulse();
            }
            else
            {
                StopDebugSkipThreeStarPulse();
            }
        }

        private void StartDebugSkipThreeStarPulse()
        {
            if (debugSkipThreeStarRoot == null || !debugSkipThreeStarRoot.gameObject.activeInHierarchy)
            {
                return;
            }

            if (debugSkipPulseTween != null && debugSkipPulseTween.IsActive())
            {
                return;
            }

            var duration = 1f / Mathf.Max(0.01f, DebugSkipPulseSpeed);
            debugSkipThreeStarRoot.localScale = Vector3.one * DebugSkipPulseMinScale;
            debugSkipPulseTween = debugSkipThreeStarRoot
                .DOScale(DebugSkipPulseMaxScale, duration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        private void StopDebugSkipThreeStarPulse()
        {
            if (debugSkipPulseTween != null)
            {
                debugSkipPulseTween.Kill();
                debugSkipPulseTween = null;
            }

            if (debugSkipThreeStarRoot != null)
            {
                debugSkipThreeStarRoot.localScale = Vector3.one;
            }
        }

        private static bool ShouldShowDebugSkipThreeStarButton()
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null || dataManager.IsVisitingOtherTavern)
            {
                return false;
            }

            var guide = dataManager.GameplayGuideData;
            if (guide != null && guide.skipToThreeStarUsed)
            {
                return false;
            }

            var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            return string.Equals(sceneName, "GamePlay_TavernWJ", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(sceneName, "GamePlay_Tavern2WJ", StringComparison.OrdinalIgnoreCase);
        }

        private void OnClickDebugSkipThreeStar()
        {
            GameAudioManager.PlayButtonClick();
            var dataManager = DataManager.Instance;
            if (dataManager == null)
            {
                return;
            }

            if (!dataManager.DebugSkipToThreeStarFullyBuilt(out var message))
            {
                HudOverlayService.ShowFloatingWarning(
                    string.IsNullOrWhiteSpace(message) ? "跳关失败" : message);
                return;
            }

            RefreshDebugSkipThreeStarButtonVisibility();
            RefreshPanel();
            HudOverlayService.PlayVipEnterTownCinematic(() =>
            {
                Scene.TavernSceneManager.Instance?.RefreshGuideWorldState();
                RefreshPanel();
            });
        }
    }
}
