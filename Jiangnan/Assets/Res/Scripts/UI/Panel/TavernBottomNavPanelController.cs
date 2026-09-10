using System.Collections;
using DG.Tweening;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.Scene;
using JN.Client;
using JN.Client.Tools;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.UI
{
    /// <summary>
    /// 管理酒楼底部导航入口（回城、员工、科技、成就等）。
    /// </summary>
    public class TavernBottomNavPanelController : HudPanelController<TavernHudPanelData>
    {
        [SerializeField] private RectTransform bottomButtonRoot;
        [SerializeField] private Button achievementButton;
        [SerializeField] private GameObject achievementRedDotRoot;
        [SerializeField] private Button townButton;
        [SerializeField] private Button drumUpButton;
        [SerializeField] private TMP_Text drumUpCountDownText;
        [SerializeField] private Button staffButton;
        [SerializeField] private Button techButton;
        [SerializeField] private Button upgradeButton;
        [SerializeField] private Button menuButton;
        [SerializeField] private Image menuBtnIconImage;
        [SerializeField] private Button menuQuickSwitchButton;
        [SerializeField] private TMP_Text menuCountDownText;
        [SerializeField] private Button downStairButton;

        private const string DrumUpLakeSpritePath = "Assets/Res/Resources/Textures/UI/DrumUp/lake.png";
        private const string DrumUpLakeGraySpritePath = "Assets/Res/Resources/Textures/UI/DrumUp/lake_gray.png";
        private const string PopularMenuBtnIconPath = "Assets/Res/Resources/Textures/UI/Buttons/caidan2.png";
        private const string VipMenuBtnIconPath = "Assets/Res/Resources/Textures/UI/Buttons/caidan1.png";
        private static readonly Color MenuSwitchCooldownTint = new Color32(0x64, 0x64, 0x64, 0xFF);

        private GameObject jiaoziGroupRoot;
        private TMP_Text jiaoziCapacityText;
        private CanvasGroup drumUpCanvasGroup;
        private Image drumUpIconImage;
        private Sprite drumUpLakeSprite;
        private Sprite drumUpLakeGraySprite;
        private float nextDrumUpCountdownRefreshUnscaledTime;
        private GameObject techSuggestRoot;
        private RectTransform techSuggestRect;
        private CanvasGroup techSuggestCanvasGroup;
        private Button techSuggestButton;
        private Image techSuggestBtnBg;
        private Image techSuggestBtnIcon;
        private GameObject techProgressRoot;
        private TMP_Text techProgressText;
        private GameObject techSuggestLockRoot;
        private AnimateTexture techSuggestLockAnimation;
        private int techResearchDisplayId;
        private int lastTechSuggestPopResearchId;
        private Tween techSuggestPopTween;
        private Tween techSuggestCompleteTween;
        private Vector2 techSuggestDefaultAnchoredPosition;
        private Vector3 techSuggestDefaultLocalScale = Vector3.one;
        private bool techSuggestDefaultPositionCached;
        private bool techSuggestDefaultScaleCached;
        private bool techSuggestCompleting;
        private Coroutine techSuggestRefreshRoutine;

        private const float TechSuggestPopDuration = 0.32f;
        private const float TechSuggestPopOffsetY = 48f;
        private const float TechSuggestCompleteDuration = 0.38f;
        private const float TechSuggestRefreshInterval = 0.25f;
        private static readonly Vector2 TechSuggestCompletePosition = Vector2.zero;
        private static readonly Vector3 TechSuggestCompleteScale = new(0.35f, 0.35f, 0.35f);

        protected override void OnPanelInit()
        {
            EnsureNodes();
            BindButtons();
        }

        protected override void OnPanelOpen(TavernHudPanelData data)
        {
            EnsureNodes();
            BindButtons();
            Signals.Get<AchievementProgressSignal>().RemoveListener(RefreshAchievementEntry);
            Signals.Get<AchievementProgressSignal>().AddListener(RefreshAchievementEntry);
            Signals.Get<TavernBusinessStateSignal>().RemoveListener(HandleBusinessStateChanged);
            Signals.Get<TavernBusinessStateSignal>().AddListener(HandleBusinessStateChanged);
            Signals.Get<GameplayGuideProgressSignal>().RemoveListener(HandleGuideProgressChanged);
            Signals.Get<GameplayGuideProgressSignal>().AddListener(HandleGuideProgressChanged);
            Signals.Get<TavernRuntimeChangedSignal>().RemoveListener(HandleRuntimeChanged);
            Signals.Get<TavernRuntimeChangedSignal>().AddListener(HandleRuntimeChanged);
            Signals.Get<TavernPrestigeChangedSignal>().RemoveListener(HandlePrestigeChanged);
            Signals.Get<TavernPrestigeChangedSignal>().AddListener(HandlePrestigeChanged);
            Signals.Get<TechResearchCompletedSignal>().RemoveListener(HandleTechResearchCompleted);
            Signals.Get<TechResearchCompletedSignal>().AddListener(HandleTechResearchCompleted);
            RefreshAchievementEntry();
        }

        protected override void OnPanelShow()
        {
            SetManagedNodesVisible(true);
            EnsureNodes();
            BindButtons();
            TryRevealPendingFeatureEntries();
            RefreshPanel();
            transform.SetAsLastSibling();
        }

        private static void TryRevealPendingFeatureEntries()
        {
            TavernFeatureUnlockPresenter.TryRevealAchievementEntry();
            TavernFeatureUnlockPresenter.TryRevealTechEntry();
        }

        protected override void OnPanelClose()
        {
            Signals.Get<AchievementProgressSignal>().RemoveListener(RefreshAchievementEntry);
            Signals.Get<TavernBusinessStateSignal>().RemoveListener(HandleBusinessStateChanged);
            Signals.Get<GameplayGuideProgressSignal>().RemoveListener(HandleGuideProgressChanged);
            Signals.Get<TavernRuntimeChangedSignal>().RemoveListener(HandleRuntimeChanged);
            Signals.Get<TavernPrestigeChangedSignal>().RemoveListener(HandlePrestigeChanged);
            Signals.Get<TechResearchCompletedSignal>().RemoveListener(HandleTechResearchCompleted);
            KillTechSuggestPop();
            KillTechSuggestComplete();
            StopTechSuggestRefreshRoutine();
            techSuggestCompleting = false;
            if (techSuggestButton != null)
            {
                techSuggestButton.interactable = true;
            }

            ResetTechSuggestLockAnimation();
            SetManagedNodesVisible(false);
        }

        public void RefreshPanel()
        {
            EnsureNodes();
            if (bottomButtonRoot == null)
            {
                return;
            }

            bottomButtonRoot.gameObject.SetActive(true);

            // 二楼：底栏保留下楼与菜单，隐藏城镇/拉客/员工/升级等。
            if (IsSecondFloorBottomNavMode())
            {
                ApplySecondFloorBottomNavOnlyDownStair();
                BindButtons();
                return;
            }

            RefreshTownButton();
            RefreshDrumUpButton();
            RefreshStaffEntry();
            RefreshUpgradeEntry();
            RefreshMenuEntry();
            RefreshTechEntry();
            RefreshAchievementEntry();
            RefreshJiaoziCapacityEntry();
            RefreshDownStairEntry();
            BindButtons();
        }

        private void HandleBusinessStateChanged(bool isOpen)
        {
            RefreshPanel();
        }

        private void HandleGuideProgressChanged()
        {
            RefreshPanel();
        }

        private void HandleRuntimeChanged()
        {
            if (IsSecondFloorBottomNavMode())
            {
                ApplySecondFloorBottomNavOnlyDownStair();
                return;
            }

            RefreshStaffEntry();
            RefreshUpgradeEntry();
            RefreshMenuEntry();
            RefreshTownButton();
            RefreshDrumUpButton();
            RefreshAchievementEntry();
            RefreshJiaoziCapacityEntry();
            RefreshDownStairEntry();
        }

        private void HandlePrestigeChanged()
        {
            if (IsSecondFloorBottomNavMode())
            {
                ApplySecondFloorBottomNavOnlyDownStair();
                return;
            }

            // 升星/声望变化后刷新员工、拉客与升级入口。
            RefreshStaffEntry();
            RefreshUpgradeEntry();
            RefreshMenuEntry();
            RefreshTownButton();
            RefreshDrumUpButton();
            RefreshDownStairEntry();
        }

        /// <summary>二楼自家店：底栏只显示下楼与菜单。</summary>
        private static bool IsSecondFloorBottomNavMode()
        {
            return SceneFlowCoordinator.IsOnTavernSecondFloor()
                   && (DataManager.Instance == null || !DataManager.Instance.IsVisitingOtherTavern);
        }

        /// <summary>隐藏底栏其它入口，只留下楼与菜单。</summary>
        private void ApplySecondFloorBottomNavOnlyDownStair()
        {
            EnsureNodes();
            SetNavButtonVisible(townButton, false);
            SetNavButtonVisible(drumUpButton, false);
            SetNavButtonVisible(staffButton, false);
            SetNavButtonVisible(upgradeButton, false);
            SetNavButtonVisible(techButton, false);
            SetNavButtonVisible(achievementButton, false);
            if (jiaoziGroupRoot != null && jiaoziGroupRoot.activeSelf)
            {
                jiaoziGroupRoot.SetActive(false);
            }

            RefreshDrumUpRedDot(false);
            RefreshStaffRedDot(false);
            RefreshUpgradeRedDot(false);
            RefreshMenuEntry();
            RefreshDownStairEntry();
        }

        private static void SetNavButtonVisible(Button button, bool visible)
        {
            if (button == null)
            {
                return;
            }

            if (button.gameObject.activeSelf != visible)
            {
                button.gameObject.SetActive(visible);
            }

            button.interactable = visible;
        }

        private void Update()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (Time.unscaledTime < nextDrumUpCountdownRefreshUnscaledTime)
            {
                return;
            }

            nextDrumUpCountdownRefreshUnscaledTime = Time.unscaledTime + 0.25f;
            if (drumUpButton != null && drumUpButton.gameObject.activeInHierarchy)
            {
                RefreshDrumUpButton(refreshVisualOnly: true);
            }

            RefreshMenuCooldownHud();
        }

        /// <summary>
        /// group_jiaozi：拜访他人酒楼且已购轿子时显示，文案「容量：当前/最大」；自家店隐藏。
        /// </summary>
        private void RefreshJiaoziCapacityEntry()
        {
            EnsureJiaoziNodes();
            if (jiaoziGroupRoot == null)
            {
                return;
            }

            var dataManager = DataManager.Instance;
            var show = dataManager != null
                       && dataManager.IsJiaoziUnlocked()
                       && dataManager.IsVisitingOtherTavern;
            if (jiaoziGroupRoot.activeSelf != show)
            {
                jiaoziGroupRoot.SetActive(show);
            }

            if (!show || jiaoziCapacityText == null)
            {
                return;
            }

            var used = dataManager.GetJiaoziUsedCapacity();
            var max = dataManager.GetJiaoziCapacity();
            jiaoziCapacityText.text = $"容量：{used}/{max}";
        }

        private void EnsureJiaoziNodes()
        {
            if (jiaoziGroupRoot != null && jiaoziCapacityText != null)
            {
                return;
            }

            var hudRoot = transform;
            jiaoziGroupRoot ??= HudBindingUtility.FindChildRecursive(hudRoot, "group_jiaozi")?.gameObject;
            if (jiaoziGroupRoot == null)
            {
                return;
            }

            var content = HudBindingUtility.FindChildRecursive(jiaoziGroupRoot.transform, "txt_content");
            jiaoziCapacityText ??= content != null
                ? content.GetComponent<TMP_Text>() ?? content.GetComponentInChildren<TMP_Text>(true)
                : jiaoziGroupRoot.GetComponentInChildren<TMP_Text>(true);
        }

        private void HandleTechResearchCompleted(string techName)
        {
            EnsureNodes();
            EnsureTechExtraNodes();
            StopTechSuggestRefreshRoutine();

            if (techSuggestRoot != null && techSuggestRect != null)
            {
                if (!techSuggestRoot.activeSelf)
                {
                    techSuggestRoot.SetActive(true);
                    ResetTechSuggestPopVisualState();
                }

                PlayTechSuggestCompleteAnimation(techName);
                return;
            }

            GameAudioManager.PlayUnlock();
            ShowTechUnlockFloatingWarning(techName);
        }

        /// <summary>
        /// 局内员工入口（btn_staff）：
        /// 开业前按引导解锁显示；开业后 lv1 隐藏；
        /// lv2/lv3 在本级名额下厨师与小二尚未招齐时显示，各招满后隐藏。
        /// </summary>
        private static bool ShouldShowStaffEntry(DataManager dataManager)
        {
            if (dataManager == null || dataManager.IsVisitingOtherTavern)
            {
                return false;
            }

            var guide = dataManager.GameplayGuideData;
            if (guide == null
                || !guide.recruitmentUnlocked
                || !dataManager.IsStaffRecruitUiUnlockedByAchievement())
            {
                return false;
            }

            var isOpen = dataManager.TavernData != null && dataManager.TavernData.isOpen;
            if (!isOpen)
            {
                return true;
            }

            // 开业后即为 lv1：不可再招，隐藏员工按钮。
            if (dataManager.GetTavernLevel() < 2)
            {
                return false;
            }

            // LV2/LV3：星级名额下厨师与小二都招齐后隐藏。
            var slotCap = dataManager.GetTavernLevelStaffHireSlotCap();
            return dataManager.GetHiredGuideChefCount() < slotCap
                   || dataManager.GetHiredGuideWaiterCount() < slotCap;
        }

        /// <summary>
        /// 局内生财策入口（btn_tech）强制隐藏；恢复显示时改回业务条件判断。
        /// </summary>
        private static bool ShouldShowTechEntry(DataManager dataManager)
        {
            return false;
        }

        /// <summary>
        /// 局内成就入口（btn_Achieve）强制隐藏；恢复显示时改回业务条件判断。
        /// </summary>
        private static bool ShouldShowAchievementEntry(DataManager dataManager)
        {
            return false;
        }

        private void RefreshTownButton()
        {
            // 仅在拜访他人酒楼时显示回城；自家店用拉客按钮进城镇。
            var visiting = DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern;
            if (townButton != null)
            {
                townButton.gameObject.SetActive(visiting);
                townButton.interactable = visiting;
            }
        }

        /// <summary>
        /// 自家店拉客入口：可拉客用 lake；CD/未解锁用 lake_gray；未解锁不显示红点。
        /// </summary>
        private void RefreshDrumUpButton(bool refreshVisualOnly = false)
        {
            EnsureNodes();
            if (drumUpButton == null)
            {
                return;
            }

            // 拉客入口已迁移到场景挂点 MyDrumUpBtn，底栏轿子按钮常隐。
            if (drumUpButton.gameObject.activeSelf)
            {
                drumUpButton.gameObject.SetActive(false);
            }

            RefreshDrumUpRedDot(false);
        }

        private void RefreshDrumUpRedDot(bool showRedDot)
        {
            if (drumUpButton == null)
            {
                return;
            }

            var redDot = drumUpButton.transform.Find("img_Red")?.gameObject
                         ?? HudBindingUtility.FindChildRecursive(drumUpButton.transform, "img_Red")?.gameObject;
            if (redDot == null)
            {
                return;
            }

            if (redDot.activeSelf != showRedDot)
            {
                redDot.SetActive(showRedDot);
            }
        }

        /// <summary>
        /// 可拉客用 lake，CD/未解锁用 lake_gray；不再靠 CanvasGroup 半透置灰。
        /// </summary>
        private void ApplyDrumUpIconVisual(bool canPull)
        {
            EnsureDrumUpIconNodes();
            if (drumUpIconImage == null)
            {
                return;
            }

            var sprite = canPull ? drumUpLakeSprite : drumUpLakeGraySprite;
            if (sprite != null && drumUpIconImage.sprite != sprite)
            {
                drumUpIconImage.sprite = sprite;
            }

            // 切图后保持不透明，避免与灰图叠半透。
            drumUpCanvasGroup ??= drumUpButton != null ? drumUpButton.GetComponent<CanvasGroup>() : null;
            if (drumUpCanvasGroup != null)
            {
                drumUpCanvasGroup.alpha = 1f;
                drumUpCanvasGroup.blocksRaycasts = true;
                drumUpCanvasGroup.interactable = true;
            }
        }

        private void EnsureDrumUpIconNodes()
        {
            if (drumUpIconImage == null && drumUpButton != null)
            {
                var icon = drumUpButton.transform.Find("img_BtnIcon")
                           ?? HudBindingUtility.FindChildRecursive(drumUpButton.transform, "img_BtnIcon");
                drumUpIconImage = icon != null
                    ? icon.GetComponent<Image>()
                    : drumUpButton.targetGraphic as Image;
            }

            drumUpLakeSprite ??= GameplayResourceStore.LoadAsset<Sprite>(DrumUpLakeSpritePath);
            drumUpLakeGraySprite ??= GameplayResourceStore.LoadAsset<Sprite>(DrumUpLakeGraySpritePath);
        }

        private void EnsureMenuCountDownNode()
        {
            if (menuCountDownText != null || menuButton == null)
            {
                return;
            }

            var countDown = menuButton.transform.Find("txt_countDown")
                            ?? HudBindingUtility.FindChildRecursive(menuButton.transform, "txt_countDown");
            menuCountDownText = countDown != null ? countDown.GetComponent<TMP_Text>() : null;
        }

        private void EnsureMenuIconAndSwitchNodes()
        {
            if (menuButton == null)
            {
                return;
            }

            if (menuBtnIconImage == null)
            {
                var icon = menuButton.transform.Find("img_BtnIcon")
                           ?? HudBindingUtility.FindChildRecursive(menuButton.transform, "img_BtnIcon");
                menuBtnIconImage = icon != null ? icon.GetComponent<Image>() : null;
            }

            if (menuQuickSwitchButton == null)
            {
                var switchNode = menuButton.transform.Find("btn_Switch")
                                 ?? HudBindingUtility.FindChildRecursive(menuButton.transform, "btn_Switch");
                menuQuickSwitchButton = switchNode != null ? switchNode.GetComponent<Button>() : null;
            }

            DisableMenuQuickSwitchClicks();
        }

        /// <summary>btn_Switch 仅作展示，不接收点击。</summary>
        private void DisableMenuQuickSwitchClicks()
        {
            if (menuQuickSwitchButton == null)
            {
                return;
            }

            menuQuickSwitchButton.onClick.RemoveAllListeners();
            menuQuickSwitchButton.interactable = false;
            menuQuickSwitchButton.enabled = false;
            var graphics = menuQuickSwitchButton.GetComponentsInChildren<Graphic>(true);
            for (var index = 0; index < graphics.Length; index++)
            {
                if (graphics[index] != null)
                {
                    graphics[index].raycastTarget = false;
                }
            }
        }

        private static string FormatPullCooldown(float remainingSeconds)
        {
            var total = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            var minutes = total / 60;
            var seconds = total % 60;
            return $"{minutes:00}:{seconds:00}";
        }

        private void RefreshStaffEntry()
        {
            var dataManager = DataManager.Instance;
            var showStaff = ShouldShowStaffEntry(dataManager);
            if (staffButton != null)
            {
                staffButton.gameObject.SetActive(showStaff);
                staffButton.interactable = showStaff;
                var label = staffButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = "员工";
                }
            }

            RefreshStaffRedDot(showStaff);
        }

        /// <summary>
        /// 底部升级入口：始终可点开升级弹窗；可升级时显示红点，不做置灰/tips。
        /// </summary>
        private void RefreshUpgradeEntry()
        {
            EnsureNodes();
            if (upgradeButton == null)
            {
                return;
            }

            var dataManager = DataManager.Instance;
            var visiting = dataManager != null && dataManager.IsVisitingOtherTavern;
            var show = !visiting;
            if (upgradeButton.gameObject.activeSelf != show)
            {
                upgradeButton.gameObject.SetActive(show);
            }

            if (!show)
            {
                RefreshUpgradeRedDot(false);
                return;
            }

            upgradeButton.interactable = true;
            var canUpgrade = dataManager != null && dataManager.CanUpgradeTavernPrestigeLevel();
            RefreshUpgradeRedDot(canUpgrade);
        }

        /// <summary>
        /// 菜单入口：自家二星及以上，且首次上二楼解锁后才显示（一楼/二楼均可）。
        /// </summary>
        private void RefreshMenuEntry()
        {
            EnsureNodes();
            if (menuButton == null)
            {
                return;
            }

            var show = DataManager.Instance != null && DataManager.Instance.ShouldShowTavernMenuEntry();
            SetNavButtonVisible(menuButton, show);
            RefreshMenuBtnIcon();
            RefreshMenuCooldownHud();
        }

        /// <summary>
        /// btn_Menu/img_BtnIcon：大众用 caidan2，贵客用 caidan1。
        /// </summary>
        private void RefreshMenuBtnIcon()
        {
            EnsureMenuIconAndSwitchNodes();
            if (menuBtnIconImage == null)
            {
                return;
            }

            var vipMenu = DataManager.Instance != null && DataManager.Instance.IsVipMenuSelected();
            var path = vipMenu ? VipMenuBtnIconPath : PopularMenuBtnIconPath;
            var sprite = GameplayResourceStore.LoadAsset<Sprite>(path);
            if (sprite != null && menuBtnIconImage.sprite != sprite)
            {
                menuBtnIconImage.sprite = sprite;
            }

            menuBtnIconImage.color = Color.white;
        }

        /// <summary>
        /// btn_Menu 下 txt_countDown：菜单切换冷却倒计时。
        /// </summary>
        private void RefreshMenuCooldownHud()
        {
            EnsureMenuCountDownNode();
            var showButton = menuButton != null && menuButton.gameObject.activeInHierarchy;
            var remaining = showButton && DataManager.Instance != null
                ? DataManager.Instance.GetMenuSwitchCooldownRemainingSeconds()
                : 0f;
            var onCooldown = remaining > 0.01f;
            if (menuCountDownText != null)
            {
                if (onCooldown)
                {
                    if (!menuCountDownText.gameObject.activeSelf)
                    {
                        menuCountDownText.gameObject.SetActive(true);
                    }

                    menuCountDownText.text = FormatPullCooldown(remaining);
                }
                else
                {
                    menuCountDownText.text = string.Empty;
                    if (menuCountDownText.gameObject.activeSelf)
                    {
                        menuCountDownText.gameObject.SetActive(false);
                    }
                }
            }

            RefreshMenuQuickSwitchVisual();
        }

        /// <summary>
        /// 快捷切换图标仅展示：冷却中置灰，不接收点击。
        /// </summary>
        private void RefreshMenuQuickSwitchVisual()
        {
            EnsureMenuIconAndSwitchNodes();
            if (menuQuickSwitchButton == null)
            {
                return;
            }

            DisableMenuQuickSwitchClicks();
            var showButton = menuButton != null && menuButton.gameObject.activeInHierarchy;
            var onCooldown = showButton
                             && DataManager.Instance != null
                             && !DataManager.Instance.IsMenuSwitchCooldownReady();
            var tint = onCooldown ? MenuSwitchCooldownTint : Color.white;
            var graphics = menuQuickSwitchButton.GetComponentsInChildren<Graphic>(true);
            for (var index = 0; index < graphics.Length; index++)
            {
                if (graphics[index] != null)
                {
                    graphics[index].color = tint;
                }
            }
        }

        private void RefreshUpgradeRedDot(bool showRedDot)
        {
            if (upgradeButton == null)
            {
                return;
            }

            var redDot = upgradeButton.transform.Find("img_Red")?.gameObject
                         ?? HudBindingUtility.FindChildRecursive(upgradeButton.transform, "img_Red")?.gameObject;
            if (redDot == null)
            {
                return;
            }

            if (redDot.activeSelf != showRedDot)
            {
                redDot.SetActive(showRedDot);
            }
        }

        private void RefreshStaffRedDot(bool showStaffEntry)
        {
            var staffRedDotRoot = staffButton != null
                ? staffButton.transform.Find("img_Red")?.gameObject
                  ?? HudBindingUtility.FindChildRecursive(staffButton.transform, "img_Red")?.gameObject
                : null;
            if (staffRedDotRoot == null)
            {
                return;
            }

            // 可继续招聘（尚有名额且有候选）时显示红点。
            var show = showStaffEntry
                       && DataManager.Instance != null
                       && DataManager.Instance.ShouldShowStaffHireAvailableRedDot();
            staffRedDotRoot.SetActive(show);
        }

        private void RefreshTechEntry()
        {
            var dataManager = DataManager.Instance;
            var showTech = ShouldShowTechEntry(dataManager);
            if (techButton != null)
            {
                techButton.gameObject.SetActive(showTech);
                techButton.interactable = showTech;
                var label = techButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = "生财策";
                }
            }

            RefreshTechRedDot(showTech);
            RefreshTechResearchProgress(showTech);
        }

        private void RefreshTechRedDot(bool showTechEntry)
        {
            var techRedDotRoot = techButton != null
                ? techButton.transform.Find("img_Red")?.gameObject
                  ?? HudBindingUtility.FindChildRecursive(techButton.transform, "img_Red")?.gameObject
                : null;
            if (techRedDotRoot == null)
            {
                return;
            }

            var show = showTechEntry
                       && DataManager.Instance != null
                       && DataManager.Instance.ShouldShowTechEntryRedDot();
            techRedDotRoot.SetActive(show);
        }

        private void RefreshTechResearchProgress(bool showTechEntry)
        {
            if (techSuggestRoot == null)
            {
                return;
            }

            var dataManager = DataManager.Instance;
            var show = showTechEntry && IsTechResearchActive(dataManager);

            if (show)
            {
                var researchId = dataManager.SaveData.gameplay.researchingTechId;
                ShowTechSuggestResearch(researchId);
                if (techProgressRoot != null)
                {
                    techProgressRoot.SetActive(true);
                }

                StartTechSuggestRefreshRoutine();
                RefreshTechSuggestProgressDisplay(dataManager);
                return;
            }

            StopTechSuggestRefreshRoutine();
            if (!techSuggestCompleting)
            {
                HideTechSuggestResearch();
            }
        }

        private static bool IsTechResearchActive(DataManager dataManager)
        {
            if (dataManager?.SaveData?.gameplay == null || dataManager.SaveData.gameplay.researchingTechId <= 0)
            {
                return false;
            }

            return dataManager.TryGetTechResearchProgress(out _, out _);
        }

        private void ShowTechSuggestResearch(int researchId, bool playPopAnimation = true)
        {
            if (techSuggestRoot == null)
            {
                return;
            }

            EnsureTechExtraNodes();
            EnsureTechSuggestPopNodes();
            CacheTechSuggestDefaultPosition();
            CacheTechSuggestDefaultScale();
            if (techSuggestRect == null)
            {
                techSuggestRoot.SetActive(true);
                return;
            }

            var shouldPop = playPopAnimation
                            && (!techSuggestRoot.activeSelf || lastTechSuggestPopResearchId != researchId);
            techSuggestRoot.SetActive(true);
            lastTechSuggestPopResearchId = researchId;
            if (researchId > 0)
            {
                ResetTechSuggestLockAnimation();
            }

            if (!shouldPop)
            {
                ResetTechSuggestPopVisualState();
                return;
            }

            KillTechSuggestPop();
            var startPos = techSuggestDefaultAnchoredPosition + new Vector2(0f, -TechSuggestPopOffsetY);
            techSuggestRect.anchoredPosition = startPos;
            if (techSuggestCanvasGroup != null)
            {
                techSuggestCanvasGroup.alpha = 0f;
            }

            var popSequence = DOTween.Sequence()
                .Join(techSuggestRect
                    .DOAnchorPos(techSuggestDefaultAnchoredPosition, TechSuggestPopDuration)
                    .SetEase(Ease.OutCubic));
            if (techSuggestCanvasGroup != null)
            {
                popSequence.Join(techSuggestCanvasGroup.DOFade(1f, TechSuggestPopDuration));
            }

            techSuggestPopTween = popSequence;
        }

        private void HideTechSuggestResearch()
        {
            KillTechSuggestPop();
            KillTechSuggestComplete();
            StopTechSuggestRefreshRoutine();
            if (techSuggestButton != null)
            {
                techSuggestButton.interactable = true;
            }

            if (techSuggestRoot != null)
            {
                techSuggestRoot.SetActive(false);
            }

            lastTechSuggestPopResearchId = 0;
            techResearchDisplayId = 0;
            ResetTechSuggestPopVisualState();
            ResetTechSuggestLockAnimation();
        }

        private void EnsureTechSuggestPopNodes()
        {
            if (techSuggestRoot == null)
            {
                return;
            }

            techSuggestRect ??= techSuggestRoot.transform as RectTransform;
            techSuggestCanvasGroup ??= techSuggestRoot.GetComponent<CanvasGroup>();
            if (techSuggestCanvasGroup == null)
            {
                techSuggestCanvasGroup = techSuggestRoot.AddComponent<CanvasGroup>();
            }
        }

        private void CacheTechSuggestDefaultPosition()
        {
            if (techSuggestDefaultPositionCached || techSuggestRect == null)
            {
                return;
            }

            techSuggestDefaultAnchoredPosition = techSuggestRect.anchoredPosition;
            techSuggestDefaultPositionCached = true;
        }

        private void CacheTechSuggestDefaultScale()
        {
            if (techSuggestDefaultScaleCached || techSuggestRect == null)
            {
                return;
            }

            techSuggestDefaultLocalScale = techSuggestRect.localScale;
            techSuggestDefaultScaleCached = true;
        }

        private void ResetTechSuggestPopVisualState()
        {
            if (techSuggestRect != null && techSuggestDefaultPositionCached)
            {
                techSuggestRect.anchoredPosition = techSuggestDefaultAnchoredPosition;
            }

            if (techSuggestRect != null && techSuggestDefaultScaleCached)
            {
                techSuggestRect.localScale = techSuggestDefaultLocalScale;
            }

            if (techSuggestCanvasGroup != null)
            {
                techSuggestCanvasGroup.alpha = 1f;
            }
        }

        private void KillTechSuggestPop()
        {
            if (techSuggestPopTween != null && techSuggestPopTween.IsActive())
            {
                techSuggestPopTween.Kill();
            }

            techSuggestPopTween = null;
        }

        private void KillTechSuggestComplete()
        {
            if (techSuggestCompleteTween != null && techSuggestCompleteTween.IsActive())
            {
                techSuggestCompleteTween.Kill();
            }

            techSuggestCompleteTween = null;
        }

        private void StartTechSuggestRefreshRoutine()
        {
            if (techSuggestRefreshRoutine != null || techSuggestRoot == null || !techSuggestRoot.activeSelf)
            {
                return;
            }

            techSuggestRefreshRoutine = StartCoroutine(TechSuggestRefreshLoop());
        }

        private void StopTechSuggestRefreshRoutine()
        {
            if (techSuggestRefreshRoutine == null)
            {
                return;
            }

            StopCoroutine(techSuggestRefreshRoutine);
            techSuggestRefreshRoutine = null;
        }

        private IEnumerator TechSuggestRefreshLoop()
        {
            var wait = new WaitForSecondsRealtime(TechSuggestRefreshInterval);
            while (techSuggestRoot != null && techSuggestRoot.activeSelf)
            {
                var dataManager = DataManager.Instance;
                if (dataManager == null || !TryFormatTechResearchCountdown(dataManager, out _))
                {
                    StopTechSuggestRefreshRoutine();
                    yield break;
                }

                RefreshTechSuggestProgressDisplay(dataManager);
                yield return wait;
            }

            techSuggestRefreshRoutine = null;
        }

        private void RefreshTechSuggestProgressDisplay(DataManager dataManager)
        {
            if (dataManager == null)
            {
                return;
            }

            ApplyTechSuggestResearchVisuals(dataManager);
            if (techProgressText != null && TryFormatTechResearchCountdown(dataManager, out var countdown))
            {
                techProgressText.text = countdown;
            }

            if (TryGetTechResearchFillAmount(dataManager, out var fillAmount))
            {
                SetTechSuggestIconFill(fillAmount);
            }
        }

        private void PlayTechSuggestCompleteAnimation(string techName)
        {
            if (techSuggestRoot == null || techSuggestRect == null)
            {
                GameAudioManager.PlayUnlock();
                ShowTechUnlockFloatingWarning(techName);
                return;
            }

            techSuggestCompleting = true;
            StopTechSuggestRefreshRoutine();
            KillTechSuggestPop();
            KillTechSuggestComplete();
            EnsureTechExtraNodes();
            EnsureTechSuggestPopNodes();
            CacheTechSuggestDefaultPosition();
            CacheTechSuggestDefaultScale();

            if (techProgressRoot != null)
            {
                techProgressRoot.SetActive(false);
            }

            if (techSuggestCanvasGroup != null)
            {
                techSuggestCanvasGroup.alpha = 1f;
            }

            if (techSuggestButton != null)
            {
                techSuggestButton.interactable = false;
            }

            if (techSuggestLockAnimation != null)
            {
                PrepareTechSuggestLockForUnlock();
                techSuggestLockAnimation.ResetToFirstFrame();
                GameAudioManager.PlayUnlock();
                techSuggestLockAnimation.PlayOnce(() =>
                {
                    if (techSuggestCompleting)
                    {
                        StartTechSuggestShrinkAndHide(techName);
                    }
                });
                return;
            }

            GameAudioManager.PlayUnlock();
            StartTechSuggestShrinkAndHide(techName);
        }

        private void StartTechSuggestShrinkAndHide(string techName)
        {
            if (techSuggestRect == null)
            {
                FinishTechSuggestComplete(techName);
                return;
            }

            KillTechSuggestComplete();
            var sequence = DOTween.Sequence()
                .Join(techSuggestRect
                    .DOAnchorPos(TechSuggestCompletePosition, TechSuggestCompleteDuration)
                    .SetEase(Ease.InCubic)
                    .SetUpdate(true))
                .Join(techSuggestRect
                    .DOScale(TechSuggestCompleteScale, TechSuggestCompleteDuration)
                    .SetEase(Ease.InCubic)
                    .SetUpdate(true));

            if (techSuggestCanvasGroup != null)
            {
                sequence.Join(techSuggestCanvasGroup
                    .DOFade(0f, TechSuggestCompleteDuration)
                    .SetEase(Ease.InQuad)
                    .SetUpdate(true));
            }

            sequence.OnComplete(() => FinishTechSuggestComplete(techName));
            techSuggestCompleteTween = sequence;
        }

        private void FinishTechSuggestComplete(string techName)
        {
            techSuggestCompleting = false;
            techSuggestCompleteTween = null;
            if (techSuggestButton != null)
            {
                techSuggestButton.interactable = true;
            }

            HideTechSuggestResearch();
            ShowTechUnlockFloatingWarning(techName);
            RefreshTechRedDot(ShouldShowTechEntry(DataManager.Instance));
        }

        private void ResetTechSuggestLockAnimation()
        {
            if (techSuggestLockRoot != null)
            {
                techSuggestLockRoot.SetActive(true);
                techSuggestLockRoot.transform.SetAsLastSibling();
            }

            var lockImage = techSuggestLockRoot != null
                ? techSuggestLockRoot.GetComponent<Image>()
                : null;
            if (lockImage != null)
            {
                lockImage.enabled = true;
                lockImage.color = Color.white;
            }

            techSuggestLockAnimation?.ResetToFirstFrame();
        }

        private void PrepareTechSuggestLockForUnlock()
        {
            if (techSuggestLockRoot == null)
            {
                return;
            }

            techSuggestLockRoot.SetActive(true);
            techSuggestLockRoot.transform.SetAsLastSibling();

            var lockImage = techSuggestLockRoot.GetComponent<Image>();
            if (lockImage != null)
            {
                lockImage.enabled = true;
                lockImage.color = Color.white;
            }
        }

        private static void ShowTechUnlockFloatingWarning(string techName)
        {
            var name = string.IsNullOrWhiteSpace(techName) ? "生财策" : techName;
            HudOverlayService.ShowFloatingWarning($"{name}已解锁");
        }

        private void ApplyTechSuggestResearchVisuals(DataManager dataManager)
        {
            var techId = dataManager?.SaveData?.gameplay?.researchingTechId ?? 0;
            if (techId <= 0 || techId == techResearchDisplayId)
            {
                return;
            }

            techResearchDisplayId = techId;
            var tech = TavernTechConfigUtility.Get(techId);
            if (tech == null || string.IsNullOrWhiteSpace(tech.Icon))
            {
                return;
            }

            ApplyTechSuggestIcon(techSuggestBtnBg, tech.Icon, 2);
            ApplyTechSuggestIcon(techSuggestBtnIcon, tech.Icon, 1);
            SetTechSuggestIconFill(0f);
        }

        private static void ApplyTechSuggestIcon(Image image, string iconBase, int variant)
        {
            if (image == null || string.IsNullOrWhiteSpace(iconBase))
            {
                return;
            }

            var sprite = HudOverlayAssetCatalog.LoadTechTreeIcon(iconBase, variant);
            if (sprite == null)
            {
                return;
            }

            image.sprite = sprite;
            image.color = Color.white;
        }

        private void SetTechSuggestIconFill(float fillAmount)
        {
            if (techSuggestBtnIcon == null)
            {
                return;
            }

            techSuggestBtnIcon.type = Image.Type.Filled;
            techSuggestBtnIcon.fillAmount = Mathf.Clamp01(fillAmount);
        }

        private static bool TryGetTechResearchFillAmount(DataManager dataManager, out float fillAmount)
        {
            fillAmount = 0f;
            return dataManager != null && dataManager.TryGetTechResearchFillAmount(out fillAmount);
        }

        private static bool TryFormatTechResearchCountdown(DataManager dataManager, out string countdown)
        {
            countdown = string.Empty;
            if (dataManager?.SaveData?.gameplay == null || dataManager.SaveData.gameplay.researchingTechId <= 0)
            {
                return false;
            }

            if (!dataManager.TryGetTechResearchProgress(out _, out _))
            {
                return false;
            }

            countdown = FormatResearchCountdown(dataManager.GetTechResearchRemainingSeconds());
            return true;
        }

        private static string FormatResearchCountdown(float remainingSeconds)
        {
            var total = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
            var minutes = total / 60;
            var seconds = total % 60;
            return $"{minutes}:{seconds:D2}";
        }

        private void RefreshAchievementEntry()
        {
            EnsureAchievementButton();
            var dataManager = DataManager.Instance;
            var showAchievement = ShouldShowAchievementEntry(dataManager);
            if (achievementButton != null)
            {
                achievementButton.gameObject.SetActive(showAchievement);
                achievementButton.interactable = showAchievement;
                var label = achievementButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = "成就";
                }
            }

            RefreshAchievementRedDot(showAchievement);
            RefreshTechEntry();
        }

        private void RefreshAchievementRedDot(bool showAchievementEntry)
        {
            var redDotRoot = ResolveAchievementRedDotRoot();
            if (redDotRoot == null)
            {
                return;
            }

            var dataManager = DataManager.Instance;
            var showRedDot = showAchievementEntry
                             && dataManager != null
                             && (dataManager.HasClaimableAchievement()
                                 || dataManager.ShouldShowAchievementEntryAttention());
            redDotRoot.SetActive(showRedDot);
        }

        private GameObject ResolveAchievementRedDotRoot()
        {
            EnsureAchievementButton();
            if (achievementButton == null)
            {
                return null;
            }

            return HudBindingUtility.FindChildRecursive(achievementButton.transform, "img_Red")?.gameObject;
        }

        private void EnsureAchievementButton()
        {
            var hudRoot = transform;
            var bottomButtonsRoot = bottomButtonRoot != null
                ? bottomButtonRoot.Find("group_BottomButtons") ?? bottomButtonRoot
                : hudRoot;

            var resolved = bottomButtonsRoot.Find("btn_Achieve")?.GetComponent<Button>()
                           ?? HudBindingUtility.FindChildRecursive(hudRoot, "btn_Achieve")?.GetComponent<Button>();
            if (resolved != null)
            {
                achievementButton = resolved;
            }
        }

        private void EnsureNodes()
        {
            var hudRoot = transform;

            bottomButtonRoot ??= HudBindingUtility.FindChildRecursive(hudRoot, "group_BottomBar") as RectTransform
                                ?? HudBindingUtility.FindChildRecursive(hudRoot, "group_BottomButtons") as RectTransform;

            var bottomButtonsRoot = bottomButtonRoot != null
                ? bottomButtonRoot.Find("group_BottomButtons") ?? bottomButtonRoot
                : hudRoot;

            achievementButton ??= bottomButtonsRoot.Find("btn_Achieve")?.GetComponent<Button>()
                                   ?? HudBindingUtility.FindChildRecursive(hudRoot, "btn_Achieve")?.GetComponent<Button>();

            achievementRedDotRoot ??= achievementButton != null
                ? HudBindingUtility.FindChildRecursive(achievementButton.transform, "img_Red")?.gameObject
                : null;

            townButton ??= bottomButtonsRoot.Find("btn_Town")?.GetComponent<Button>()
                           ?? HudBindingUtility.FindChildRecursive(hudRoot, "btn_Town")?.GetComponent<Button>();

            drumUpButton ??= bottomButtonsRoot.Find("btn_DrumUp")?.GetComponent<Button>()
                             ?? HudBindingUtility.FindChildRecursive(hudRoot, "btn_DrumUp")?.GetComponent<Button>();
            if (drumUpCountDownText == null && drumUpButton != null)
            {
                var countDown = drumUpButton.transform.Find("txt_countDown")
                                ?? HudBindingUtility.FindChildRecursive(drumUpButton.transform, "txt_countDown");
                drumUpCountDownText = countDown != null ? countDown.GetComponent<TMP_Text>() : null;
            }

            staffButton ??= bottomButtonsRoot.Find("btn_staff")?.GetComponent<Button>()
                            ?? HudBindingUtility.FindChildRecursive(hudRoot, "btn_staff")?.GetComponent<Button>()
                            ?? HudBindingUtility.FindChildRecursive(hudRoot, "btn_Staff")?.GetComponent<Button>();

            upgradeButton ??= bottomButtonsRoot.Find("btn_Upgrade")?.GetComponent<Button>()
                              ?? HudBindingUtility.FindChildRecursive(hudRoot, "btn_Upgrade")?.GetComponent<Button>();
            menuButton ??= bottomButtonsRoot.Find("btn_Menu")?.GetComponent<Button>()
                           ?? HudBindingUtility.FindChildRecursive(hudRoot, "btn_Menu")?.GetComponent<Button>();
            EnsureMenuCountDownNode();
            EnsureMenuIconAndSwitchNodes();
            // 去掉历史置灰残留，图标始终白。
            if (upgradeButton != null)
            {
                var icon = upgradeButton.transform.Find("img_BtnIcon")
                           ?? HudBindingUtility.FindChildRecursive(upgradeButton.transform, "img_BtnIcon");
                var iconImage = icon != null ? icon.GetComponent<Image>() : null;
                if (iconImage != null)
                {
                    iconImage.color = Color.white;
                }
            }

            downStairButton ??= bottomButtonsRoot.Find("btn_DownStair")?.GetComponent<Button>()
                                ?? HudBindingUtility.FindChildRecursive(hudRoot, "btn_DownStair")?.GetComponent<Button>();

            techButton ??= bottomButtonsRoot.Find("btn_tech")?.GetComponent<Button>()
                           ?? HudBindingUtility.FindChildRecursive(hudRoot, "btn_tech")?.GetComponent<Button>();

            EnsureJiaoziNodes();
            EnsureTechExtraNodes();
        }

        private void EnsureTechExtraNodes()
        {
            if (techButton == null)
            {
                return;
            }

            techSuggestRoot ??= techButton.transform.Find("btn_tech_suggest")?.gameObject
                                ?? HudBindingUtility.FindChildRecursive(techButton.transform, "btn_tech_suggest")?.gameObject;
            if (techSuggestRoot != null)
            {
                var suggestTransform = techSuggestRoot.transform;
                techSuggestRect ??= suggestTransform as RectTransform;
                techSuggestCanvasGroup ??= techSuggestRoot.GetComponent<CanvasGroup>();
                techSuggestButton ??= techSuggestRoot.GetComponent<Button>();
                techSuggestBtnBg ??= suggestTransform.Find("img_BtnBg")?.GetComponent<Image>();
                techSuggestBtnIcon ??= suggestTransform.Find("img_BtnIcon")?.GetComponent<Image>();
                techProgressRoot ??= suggestTransform.Find("Progress")?.gameObject;
                techSuggestLockRoot ??= suggestTransform.Find("img_Lock")?.gameObject
                                         ?? HudBindingUtility.FindChildRecursive(suggestTransform, "img_Lock")?.gameObject;
                techSuggestLockAnimation ??= techSuggestLockRoot != null
                    ? techSuggestLockRoot.GetComponent<AnimateTexture>()
                      ?? techSuggestLockRoot.GetComponentInChildren<AnimateTexture>(true)
                    : null;
            }

            techProgressText ??= techProgressRoot != null
                ? techProgressRoot.GetComponent<TMP_Text>()
                  ?? techProgressRoot.GetComponentInChildren<TMP_Text>(true)
                : null;
        }

        private void BindButtons()
        {
            BindNavButton(townButton, OnClickTownButton);
            BindNavButton(drumUpButton, OnClickDrumUpButton);
            BindNavButton(staffButton, OnClickStaffButton);
            BindNavButton(upgradeButton, OnClickUpgradeButton);
            BindNavButton(menuButton, OnClickMenuButton);
            BindNavButton(downStairButton, OnClickDownStairButton);
            BindNavButton(techButton, OnClickTechButton);
            BindNavButton(techSuggestButton, OnClickTechButton);
            BindNavButton(achievementButton, OnClickAchievementButton);
        }

        private static void BindNavButton(Button button, UnityEngine.Events.UnityAction onClick)
        {
            if (button == null || onClick == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                GameAudioManager.PlayButtonClick();
                onClick.Invoke();
            });
        }

        private void OnClickStaffButton()
        {
            if (DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern)
            {
                return;
            }

            HudOverlayService.ShowStaffRecruitPanel();
        }

        private void OnClickUpgradeButton()
        {
            if (DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern)
            {
                return;
            }

            HudOverlayService.ShowUpgradeTavernPanel();
        }

        private void OnClickMenuButton()
        {
            if (DataManager.Instance == null || !DataManager.Instance.ShouldShowTavernMenuEntry())
            {
                return;
            }

            HudOverlayService.ShowMenuSwitchPanel();
        }

        /// <summary>
        /// 仅二楼显示下楼；一楼与拜访他人酒楼隐藏。
        /// </summary>
        private void RefreshDownStairEntry()
        {
            EnsureNodes();
            if (downStairButton == null)
            {
                return;
            }

            var show = SceneFlowCoordinator.IsOnTavernSecondFloor()
                       && (DataManager.Instance == null || !DataManager.Instance.IsVisitingOtherTavern);
            if (downStairButton.gameObject.activeSelf != show)
            {
                downStairButton.gameObject.SetActive(show);
            }

            if (show)
            {
                downStairButton.interactable = true;
                // 确保父节点（底栏）可见，避免只 SetActive 子按钮仍看不见。
                if (bottomButtonRoot != null && !bottomButtonRoot.gameObject.activeSelf)
                {
                    bottomButtonRoot.gameObject.SetActive(true);
                }
            }
        }

        private void OnClickDownStairButton()
        {
            if (!SceneFlowCoordinator.IsOnTavernSecondFloor())
            {
                return;
            }

            if (DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern)
            {
                return;
            }

            StartCoroutine(SceneFlowCoordinator.EnterTavernFirstFloorFromSecond());
        }

        private void OnClickTechButton()
        {
            HudOverlayService.ShowTavernTechTreePanel();
        }

        private void OnClickAchievementButton()
        {
            DataManager.Instance?.ClearAchievementEntryAttention();
            HudOverlayService.ShowAchievementCatalogPanel();
        }

        private void OnClickTownButton()
        {
            StartCoroutine(SceneFlowCoordinator.EnterTown());
        }

        private void OnClickDrumUpButton()
        {
            HudOverlayService.HandleOwnTavernDrumUpClick();
        }

        private void SetManagedNodesVisible(bool visible)
        {
            if (bottomButtonRoot != null)
            {
                bottomButtonRoot.gameObject.SetActive(visible);
            }
        }
    }
}
