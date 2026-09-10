using System.Collections;
using JN.Client;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.Messages;
using JN.Client.Tools;
using JN.Client.UI;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace JN.Client.Scene
{
    /// <summary>
    /// 负责建筑物件相关的运行时逻辑。
    /// </summary>
    public class BuildingItemUI : MonoBehaviour
    {
        private const string LandPricePrefabPath = "Assets/Res/Resources/UI/Runtime/LandPrice.prefab";
        private const string AchievementDisplayPrefabPath = "Assets/Res/Resources/UI/Runtime/AchievementDisplay.prefab";
        private const string TavernLevelSpritePathFormat =
            "Assets/Res/Resources/Textures/UI/Panel/UpgradeTavern/lv{0}.png";
        private const string HeadIconSpritePathFormat =
            "Assets/Res/Resources/UI/HeadIcon/{0}.png";
        /// <summary>自家默认头像（Resources 副本，对应 Textures/UI/CreatePlayer/tx.png）。</summary>
        private const string SelfDefaultHeadIconPath =
            "Assets/Res/Resources/Textures/UI/CreatePlayer/tx.png";
        private const int MinHeadIconId = 1;
        private const int MaxHeadIconId = 8;
        private const float AchievementDisplayAnchoredYOffset = 55f;
        private const string SelfEnterBtnLabel = "我的酒楼";
        private const string OtherEnterBtnLabel = "进店";
        private const string SelfUserName = "我";
        /// <summary>fieldId 2/3 在画面上方，User/Level 额外上移避免被下方建筑 HUD 遮挡。</summary>
        private const float UpperFieldHudExtraOffsetY = 70f;
        private static readonly Color SelfEnterBtnLabelColor = new(0.2f, 0.85f, 0.25f, 1f);
        private static readonly Color OtherEnterBtnLabelColor = Color.white;

        private static int ResolveSelfPlayerId()
        {
            if (JN.Client.Manager.DataManager.Instance?.PlayerData == null)
            {
                return 0;
            }

            return int.TryParse(JN.Client.Manager.DataManager.Instance.PlayerData.playerId, out var playerId)
                ? playerId
                : 0;
        }

        [Header("UI Nodes")]
        [SerializeField] private GameObject userObj;
        [SerializeField] private GameObject hotMarkObj;
        [SerializeField] private Image headIconImage;
        [SerializeField] private GameObject timeObj;
        [SerializeField] private Button openingBtn;
        [SerializeField] private Button enterBtn;
        [SerializeField] private Button userBtn;
        [SerializeField] private TMP_Text unameText;
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private GameObject functionObj;
        [SerializeField] private GameObject dushEffect;
        [SerializeField] private GameObject hammerEffect;
        [SerializeField] private GameObject fireworkEffect;
        [SerializeField] private GameObject openingSuccess;
        [SerializeField] private GameObject openingNew;
        [SerializeField] private GameObject landPriceObj;
        [SerializeField] private TMP_Text landPriceText;
        [SerializeField] private Button landPriceButton;
        [SerializeField] private GameObject landPriceRecommendObj;
        [Header("酒楼等级")]
        [SerializeField] private GameObject levelObj;
        [SerializeField] private Image levelImage;
        [Header("位置设置")]
        [SerializeField] private Vector3 level1Offset = new(0f, 15f, 0f);
        [SerializeField] private Vector3 level2Offset = new(0f, 18f, 0f);
        [SerializeField] private Vector3 level3Offset = new(0f, 21f, 0f);
        [SerializeField] private Vector3 landPriceWorldOffset = new(0f, 1.2f, 0f);
        [SerializeField] private Vector3 uiScale = new(1.35f, 1.35f, 1f);
        [SerializeField] private float buildEffectScaleMultiplier = 1.25f;

        private GameObject achievementDisplayRoot;
        private Image achievementDisplayBg;
        private TMP_Text achievementDisplayName;
        private RectTransform rectTransform;
        private Tile targetTile;
        private BuildingInfo buildingInfo;
        private float countdownTime;
        private Sprite openingNewDefaultSprite;
        private Vector3 dushEffectDefaultScale = Vector3.one;
        private Vector3 hammerEffectDefaultScale = Vector3.one;
        private bool hasInitialized;
        private int cachedLevelSprite = int.MinValue;
        private Vector2 userDefaultAnchoredPosition;
        private bool userDefaultAnchoredPositionCached;
        private Vector2 levelDefaultAnchoredPosition;
        private bool levelDefaultAnchoredPositionCached;

        /// <summary>
        /// 初始化组件引用和运行时状态。
        /// </summary>
        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            Signals.Get<TavernPrestigeChangedSignal>().RemoveListener(HandleTavernPrestigeChanged);
            Signals.Get<TavernPrestigeChangedSignal>().AddListener(HandleTavernPrestigeChanged);
        }

        private void OnDisable()
        {
            Signals.Get<TavernPrestigeChangedSignal>().RemoveListener(HandleTavernPrestigeChanged);
        }

        private void HandleTavernPrestigeChanged()
        {
            // 自家星级变化时刷新城镇头顶等级图。
            if (buildingInfo != null && buildingInfo.playerId == ResolveSelfPlayerId())
            {
                RefreshLevelDisplay();
            }
        }

        /// <summary>
        /// 处理绑定相关逻辑。
        /// </summary>
        /// <param name="tile">参数值。</param>
        public void Bind(Tile tile)
        {
            targetTile = tile;
        }

        /// <summary>
        /// 用于 Town 建筑 HUD 深度排序的世界坐标（取地块根节点，不含 UI 偏移）。
        /// </summary>
        public Vector3 GetWorldSortPosition()
        {
            return targetTile != null ? targetTile.transform.position : transform.position;
        }

        /// <summary>
        /// 获取场景锚点位置。空地价格标签贴着地基中心，建筑入口仍抬到建筑高度。
        /// </summary>
        /// <returns>返回方法执行后的结果。</returns>
        public Vector3 GetWorldAnchorPosition()
        {
            if (targetTile == null)
            {
                return Vector3.zero;
            }

            if (CanShowLandPrice())
            {
                return targetTile.transform.position + landPriceWorldOffset;
            }

            return targetTile.transform.position + GetOffsetByBuildingLevel();
        }

        /// <summary>
        /// 设置锚点ed位置。
        /// </summary>
        /// <param name="anchoredPosition">坐标。</param>
        public void SetAnchoredPosition(Vector2 anchoredPosition)
        {
            EnsureInitialized();
            if (rectTransform == null)
            {
                return;
            }

            rectTransform.anchoredPosition = anchoredPosition;
        }

        /// <summary>
        /// 设置显隐。
        /// </summary>
        /// <param name="visible">参数值。</param>
        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 设置数据。
        /// </summary>
        /// <param name="info">参数值。</param>
        public void SetData(BuildingInfo info)
        {
            EnsureInitialized();
            buildingInfo = info;
            StopAllCoroutines();
            ResetUI();

            if (info == null)
            {
                return;
            }

            if (info.playerId == 0)
            {
                RefreshLandPrice();
                RefreshLevelDisplay();
                ApplyUpperFieldHudOffset(0);
                return;
            }

            if (info.playerId == ResolveSelfPlayerId() && info.buildingLevel <= 0)
            {
                SetOpeningNewSprite(openingNewDefaultSprite);
                SetNodeActive(openingNew, true);
                RefreshLevelDisplay();
                ApplyUpperFieldHudOffset(info.tileId);
                return;
            }

            var selfPlayerId = ResolveSelfPlayerId();
            var isSelfBuilding = info.playerId == selfPlayerId;
            var isOtherBuilding = info.playerId > 0 && !isSelfBuilding;

            // User：自家显示「我」，他人显示店名；Enter 统一隐藏，进店走 User。
            if (isSelfBuilding || isOtherBuilding)
            {
                SetNodeActive(userObj, true);
                if (unameText != null)
                {
                    unameText.text = isSelfBuilding
                        ? SelfUserName
                        : (string.IsNullOrWhiteSpace(info.name) ? "他人酒楼" : info.name);
                }

                if (isOtherBuilding)
                {
                    RefreshHotMarkNode(info.tileId);
                    RefreshHeadIconDisplay(info.tileId, useConfigHeadIcon: true);
                }
                else
                {
                    SetNodeActive(ResolveHotMarkObj(), false);
                    // 自家酒楼：显示默认头像，不读 TownBuilding.headIconId。
                    RefreshHeadIconDisplay(info.tileId, useConfigHeadIcon: false);
                }
            }
            else
            {
                SetNodeActive(ResolveHotMarkObj(), false);
                SetNodeActive(ResolveHeadIconImage(), false);
            }

            var isConstructing = info.status == 1 || info.buildingTime > 0;
            if (isConstructing)
            {
                // 建造中：只播锤子/烟雾与倒计时，不显示进店名牌。
                SetNodeActive(userObj, false);
                SetNodeActive(enterBtn, false);
                SetNodeActive(ResolveLevelObj(), false);
                SetNodeActive(timeObj, true);
                SetBuildEffectsActive(true);
                if (info.buildingTime > 0)
                {
                    countdownTime = info.buildingTime;
                    StartCoroutine(Countdown());
                }
            }

            if (info.status == 2)
            {
                // Function/Enter 隐藏；建成酒楼统一用 User 进店。
                SetNodeActive(functionObj, false);
                SetNodeActive(enterBtn, false);
            }

            if (info.celebrationTime > 0)
            {
                SetNodeActive(fireworkEffect, true);
            }

            RefreshLandPrice();
            RefreshAchievementDisplay();
            RefreshLevelDisplay();
            ApplyUpperFieldHudOffset(info.tileId);
        }

        /// <summary>
        /// fieldId 2/3 的 User 名牌与 Level 等级图同步上移，减轻被下方建筑 HUD 遮挡。
        /// </summary>
        private void ApplyUpperFieldHudOffset(int tileId)
        {
            var extraY = tileId == 2 || tileId == 3 ? UpperFieldHudExtraOffsetY : 0f;
            ApplyAnchoredOffsetY(userObj != null ? userObj.transform as RectTransform : null,
                ref userDefaultAnchoredPosition,
                ref userDefaultAnchoredPositionCached,
                extraY);
            ResolveLevelReferences();
            var levelRoot = ResolveLevelObj();
            ApplyAnchoredOffsetY(
                levelRoot != null ? levelRoot.transform as RectTransform : null,
                ref levelDefaultAnchoredPosition,
                ref levelDefaultAnchoredPositionCached,
                extraY);
        }

        private static void ApplyAnchoredOffsetY(
            RectTransform rect,
            ref Vector2 defaultAnchoredPosition,
            ref bool defaultCached,
            float extraY)
        {
            if (rect == null)
            {
                return;
            }

            if (!defaultCached)
            {
                defaultAnchoredPosition = rect.anchoredPosition;
                defaultCached = true;
            }

            rect.anchoredPosition = defaultAnchoredPosition + new Vector2(0f, extraY);
        }

        /// <summary>
        /// 设置 EnterBtn 子节点 TMP 文案与字色。
        /// </summary>
        private void ApplyEnterBtnLabel(string label, Color color)
        {
            if (enterBtn == null)
            {
                return;
            }

            var labelText = enterBtn.GetComponentInChildren<TMP_Text>(true);
            if (labelText == null)
            {
                return;
            }

            labelText.text = label;
            labelText.color = color;
        }

        private void EnsureInitialized()
        {
            if (hasInitialized)
            {
                return;
            }

            rectTransform = GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rectTransform.localScale = uiScale;
            }

            BindButton(openingBtn, HandleOpeningButtonClick);
            BindButton(userBtn, HandlePrimaryAction);
            BindButton(enterBtn, HandleEnterTavern);
            var openingNewButton = openingNew != null ? openingNew.GetComponent<Button>() : null;
            if (openingNewButton != null && openingNewButton != openingBtn)
            {
                BindButton(openingNewButton, HandleOpeningButtonClick);
            }

            openingNewDefaultSprite = openingNew != null ? openingNew.GetComponent<Image>()?.sprite : null;
            dushEffectDefaultScale = dushEffect != null ? dushEffect.transform.localScale : Vector3.one;
            hammerEffectDefaultScale = hammerEffect != null ? hammerEffect.transform.localScale : Vector3.one;
            ResolveLandPriceReferences();
            ResolveHotMarkObj();
            ResolveHeadIconImage();
            ResolveLevelReferences();
            hasInitialized = true;
        }

        /// <summary>
        /// User/hot：仅 TownBuilding.hotMark≠0 的他人建筑显示。
        /// </summary>
        private void RefreshHotMarkNode(int tileId)
        {
            var hot = ResolveHotMarkObj();
            var show = TownBuildingConfigUtility.IsHotByFieldId(tileId);
            SetNodeActive(hot, show);
        }

        /// <summary>
        /// 他人：按 TownBuilding.headIconId 换头像；自家：默认 CreatePlayer/tx.png。
        /// </summary>
        private void RefreshHeadIconDisplay(int tileId, bool useConfigHeadIcon)
        {
            var headImage = ResolveHeadIconImage();
            if (headImage == null)
            {
                return;
            }

            Sprite sprite = null;
            if (useConfigHeadIcon)
            {
                var headIconId = TownBuildingConfigUtility.GetHeadIconIdByFieldId(tileId);
                if (headIconId >= MinHeadIconId && headIconId <= MaxHeadIconId)
                {
                    sprite = GameplayResourceStore.LoadAsset<Sprite>(
                        string.Format(HeadIconSpritePathFormat, headIconId));
                }
            }
            else
            {
                // 工程路径 Assets/Res/Textures/UI/CreatePlayer/tx.png 的 Resources 可读副本。
                sprite = GameplayResourceStore.LoadAsset<Sprite>(SelfDefaultHeadIconPath);
            }

            if (sprite == null)
            {
                SetNodeActive(headImage, false);
                return;
            }

            headImage.sprite = sprite;
            headImage.preserveAspect = true;
            SetNodeActive(headImage, true);
            headImage.enabled = true;
        }

        private GameObject ResolveHotMarkObj()
        {
            if (hotMarkObj != null)
            {
                return hotMarkObj;
            }

            if (userObj != null)
            {
                var hot = userObj.transform.Find("hot");
                if (hot != null)
                {
                    hotMarkObj = hot.gameObject;
                }
            }

            return hotMarkObj;
        }

        private Image ResolveHeadIconImage()
        {
            if (headIconImage != null)
            {
                return headIconImage;
            }

            if (userObj != null)
            {
                var imageBtn = userObj.transform.Find("ImageBtn");
                if (imageBtn != null)
                {
                    headIconImage = imageBtn.GetComponent<Image>();
                }
            }

            if (headIconImage == null)
            {
                var found = transform.Find("User/ImageBtn")
                            ?? HudBindingUtility.FindChildRecursive(transform, "ImageBtn");
                if (found != null)
                {
                    headIconImage = found.GetComponent<Image>();
                }
            }

            return headIconImage;
        }

        /// <summary>
        /// 重置界面。
        /// </summary>
        public void ResetUI()
        {
            SetNodeActive(openingBtn, false);
            SetNodeActive(enterBtn, false);
            SetNodeActive(openingNew, false);
            SetNodeActive(timeObj, false);
            SetNodeActive(userObj, false);
            SetNodeActive(ResolveHotMarkObj(), false);
            SetNodeActive(ResolveHeadIconImage(), false);
            ApplyUpperFieldHudOffset(0);
            SetNodeActive(dushEffect, false);
            SetNodeActive(hammerEffect, false);
            SetNodeActive(fireworkEffect, false);
            SetNodeActive(openingSuccess, false);
            SetNodeActive(functionObj, false);
            SetLandPriceActive(false);
            SetAchievementDisplayActive(false);
            SetNodeActive(ResolveLevelObj(), false);
        }

        /// <summary>
        /// Level 节点：自家用声望酒楼等级，他人用 TownBuilding/地块配置 buildingLevel。
        /// </summary>
        private void RefreshLevelDisplay()
        {
            ResolveLevelReferences();
            var levelRoot = ResolveLevelObj();
            if (levelRoot == null)
            {
                return;
            }

            if (!TryResolveDisplayTavernLevel(out var displayLevel))
            {
                SetNodeActive(levelRoot, false);
                return;
            }

            // 0 星用 lv0；最高 clamp 到 4。
            var spriteLevel = Mathf.Clamp(displayLevel, 0, 4);
            if (levelImage != null
                && (cachedLevelSprite != spriteLevel || levelImage.sprite == null))
            {
                var sprite = GameplayResourceStore.LoadAsset<Sprite>(
                    string.Format(TavernLevelSpritePathFormat, spriteLevel));
                if (sprite != null)
                {
                    levelImage.sprite = sprite;
                    levelImage.preserveAspect = true;
                    levelImage.raycastTarget = false;
                    cachedLevelSprite = spriteLevel;
                }
            }

            SetNodeActive(levelRoot, true);
        }

        /// <summary>
        /// 解析应展示的等级：自己 → GetTavernLevel；别人 → 配置 buildingLevel。
        /// </summary>
        private bool TryResolveDisplayTavernLevel(out int displayLevel)
        {
            displayLevel = 0;
            if (buildingInfo == null || buildingInfo.playerId == 0)
            {
                return false;
            }

            var selfPlayerId = ResolveSelfPlayerId();
            if (buildingInfo.playerId == selfPlayerId)
            {
                // 点击建造后立刻写入 buildingLevel，但未落成前不显示星级（含 lv0 无星）。
                if (buildingInfo.status != 2 || buildingInfo.buildingTime > 0)
                {
                    return false;
                }

                if (buildingInfo.buildingLevel <= 0)
                {
                    return false;
                }

                displayLevel = DataManager.Instance != null
                    ? DataManager.Instance.GetTavernLevel()
                    : buildingInfo.buildingLevel;
                return true;
            }

            // 他人酒楼：TownBuilding 配置等级（写入 BuildingInfo.buildingLevel）。
            if (buildingInfo.buildingLevel <= 0)
            {
                return false;
            }

            displayLevel = buildingInfo.buildingLevel;
            return true;
        }

        private GameObject ResolveLevelObj()
        {
            if (levelObj != null)
            {
                return levelObj;
            }

            var level = transform.Find("Level");
            if (level != null)
            {
                levelObj = level.gameObject;
            }

            return levelObj;
        }

        private void ResolveLevelReferences()
        {
            ResolveLevelObj();
            if (levelImage == null && levelObj != null)
            {
                levelImage = levelObj.GetComponent<Image>();
            }
        }

        /// <summary>
        /// 处理倒计时显示。
        /// </summary>
        /// <returns>协程迭代器。</returns>
        private IEnumerator Countdown()
        {
            while (countdownTime > 0f)
            {
                countdownTime -= Time.deltaTime;
                var displaySeconds = Mathf.CeilToInt(countdownTime);
                if (timeText != null)
                {
                    timeText.text = $"00:{displaySeconds:D2}";
                }

                yield return null;
            }

            if (timeText != null)
            {
                timeText.text = "00:00";
            }

            SetNodeActive(timeObj, false);
            SetBuildEffectsActive(false);
            if (buildingInfo == null)
            {
                yield break;
            }

            buildingInfo.buildingTime = 0;
            buildingInfo.status = 2;
            targetTile?.MarkPlayCompleteEffectOnNextVisual();
            if (JN.Client.Manager.DataManager.Instance != null)
            {
                JN.Client.Manager.DataManager.Instance.UpsertBuildingInfo(buildingInfo);
                if (buildingInfo.playerId == ResolveSelfPlayerId())
                {
                    JN.Client.Manager.DataManager.Instance.SetActiveOwnedBuilding(buildingInfo.tileId, buildingInfo.buildingLevel);
                }
            }

            TileManager.Instance.UpdateTile(buildingInfo.tileId, buildingInfo);
            TileManager.Instance.RefreshAllTileViews();
            Tile.NotifyTownOwnedBuildingCompleted();
        }

        /// <summary>
        /// 处理主要操作。
        /// </summary>
        private void HandlePrimaryAction()
        {
            GameAudioManager.PlayButtonClick();
            targetTile?.HandlePrimaryActionFromUI();
        }

        /// <summary>
        /// 点击建造按钮：开始 3 秒建造动画，落成后再出酒楼模型与光柱。
        /// </summary>
        private void HandleOpeningButtonClick()
        {
            GameAudioManager.PlayButtonClick();
            targetTile?.TryStartDefaultLevel1BuildFromUI();
        }

        /// <summary>
        /// 处理进入酒楼操作。
        /// </summary>
        private void HandleEnterTavern()
        {
            if (targetTile != null)
            {
                targetTile.EnterTavernFromUI();
            }
        }

        /// <summary>
        /// 处理绑定按钮相关逻辑。
        /// </summary>
        /// <param name="button">按钮对象。</param>
        /// <param name="callback">回调函数。</param>
        private static void BindButton(Button button, UnityEngine.Events.UnityAction callback)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                GameAudioManager.PlayButtonClick();
                callback();
            });
        }

        /// <summary>
        /// 设置节点显隐状态。
        /// </summary>
        /// <param name="target">目标对象。</param>
        /// <param name="active">参数值。</param>
        private static void SetNodeActive(Object target, bool active)
        {
            switch (target)
            {
                case null:
                    return;
                case Component component:
                    component.gameObject.SetActive(active);
                    break;
                case GameObject gameObject:
                    gameObject.SetActive(active);
                    break;
            }
        }

        /// <summary>
        /// 切换地块购买或建筑建造按钮的图标。
        /// </summary>
        /// <param name="sprite">需要显示的按钮图标。</param>
        private void SetOpeningNewSprite(Sprite sprite)
        {
            if (openingNew == null || sprite == null)
            {
                return;
            }

            var image = openingNew.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
        }

        /// <summary>
        /// 刷新地块购买价格显示。
        /// </summary>
        private void RefreshLandPrice()
        {
            var canShow = CanShowLandPrice();
            SetLandPriceActive(canShow);
            if (!canShow)
            {
                return;
            }

            ResolveLandPriceReferences();
            if (landPriceText != null)
            {
                var cost = DataManager.Instance.GetTownLandPurchaseCost(targetTile != null ? targetTile.tileId : 0);
                landPriceText.text = cost > 0 ? cost.ToString() : "免费";
            }

            if (landPriceRecommendObj != null)
            {
                var tileId = targetTile != null ? targetTile.tileId : 0;
                landPriceRecommendObj.SetActive(tileId == 2 || tileId == 3);
            }
        }

        /// <summary>
        /// 当前 HUD 是否有内容需要展示（空地块且不可再买时整项隐藏）。
        /// </summary>
        public bool ShouldDisplayHud()
        {
            if (buildingInfo == null || buildingInfo.playerId == 0)
            {
                return CanShowLandPrice();
            }

            return true;
        }

        /// <summary>
        /// 当前地块是否需要显示购买价格。
        /// </summary>
        /// <returns>显示时返回 true。</returns>
        private bool CanShowLandPrice()
        {
            if (targetTile == null || buildingInfo != null && buildingInfo.playerId != 0)
            {
                return false;
            }

            if (DataManager.Instance == null || !DataManager.Instance.IsSelfTownBuildingField(targetTile.tileId))
            {
                return false;
            }

            var selfPlayerId = ResolveSelfPlayerId();
            // 首块地免费时仍显示可点入口（文案显示「免费」），避免既无价格牌又点不了地。
            return !DataManager.Instance.IsTownLandCountAtLimit(selfPlayerId, out _);
        }

        /// <summary>
        /// 显示或隐藏地块价格 UI。
        /// </summary>
        /// <param name="active">是否显示。</param>
        private void SetLandPriceActive(bool active)
        {
            ResolveLandPriceReferences();
            if (landPriceObj != null)
            {
                landPriceObj.SetActive(active);
            }
        }

        /// <summary>
        /// 只负责解析并绑定 LandPrice 引用，不在代码中调整任何布局参数。
        /// </summary>
        private void ResolveLandPriceReferences()
        {
            var loadedFromResources = landPriceObj != null && landPriceObj.name == "LandPrice";
            if (!loadedFromResources)
            {
                if (landPriceObj != null)
                {
                    Destroy(landPriceObj);
                    landPriceObj = null;
                    landPriceText = null;
                    landPriceButton = null;
                    landPriceRecommendObj = null;
                }

                var existing = transform.Find("LandPrice");
                if (existing != null)
                {
                    Destroy(existing.gameObject);
                }
            }

            if (landPriceObj == null)
            {
                var prefab = GameplayResourceStore.LoadAsset<GameObject>(LandPricePrefabPath);
                if (prefab != null)
                {
                    landPriceObj = Instantiate(prefab, transform, false);
                    landPriceObj.name = "LandPrice";
                }
            }

            if (landPriceObj == null)
            {
                return;
            }

            landPriceText ??= landPriceObj.GetComponentInChildren<TMP_Text>(true);
            landPriceButton ??= landPriceObj.GetComponent<Button>();
            if (landPriceRecommendObj == null)
            {
                var recommend = landPriceObj.transform.Find("img_Recommend");
                landPriceRecommendObj = recommend != null ? recommend.gameObject : null;
            }

            BindButton(landPriceButton, HandlePrimaryAction);
        }

        /// <summary>
        /// 显示或隐藏建造中的烟雾与锤子特效，并统一放大显示。
        /// </summary>
        /// <param name="active">是否显示建造特效。</param>
        private void SetBuildEffectsActive(bool active)
        {
            SetScaledEffectActive(dushEffect, dushEffectDefaultScale, active);
            SetScaledEffectActive(hammerEffect, hammerEffectDefaultScale, active);
        }

        /// <summary>
        /// 按初始缩放倍率显示建造特效。
        /// </summary>
        /// <param name="effect">特效节点。</param>
        /// <param name="defaultScale">预制体中的原始缩放。</param>
        /// <param name="active">是否显示。</param>
        private void SetScaledEffectActive(GameObject effect, Vector3 defaultScale, bool active)
        {
            if (effect == null)
            {
                return;
            }

            effect.transform.localScale = defaultScale * buildEffectScaleMultiplier;
            if (!active)
            {
                effect.SetActive(false);
                return;
            }

            effect.SetActive(true);
            var frameAnimation = effect.GetComponent<AnimateTexture>();
            frameAnimation?.PlayLoop();
        }

        /// <summary>
        /// 获取按等级偏移建筑等级。
        /// </summary>
        /// <returns>返回方法执行后的结果。</returns>
        private Vector3 GetOffsetByBuildingLevel()
        {
            var level = buildingInfo != null ? buildingInfo.buildingLevel : 0;
            return level switch
            {
                1 => level1Offset,
                2 => level2Offset,
                3 => level3Offset,
                _ => level1Offset
            };
        }

        /// <summary>
        /// 在已建成酒楼上展示成就标签：自家读全局展示 Id，其他玩家读地块存档。
        /// </summary>
        private void RefreshAchievementDisplay()
        {
            if (!ShouldShowAchievementDisplay())
            {
                SetAchievementDisplayActive(false);
                return;
            }

            EnsureAchievementDisplayNodes();
            var achievementId = ResolveBuildingAchievementDisplayId();
            var achievement = AchievementConfigUtility.Get(achievementId);
            if (achievement == null)
            {
                SetAchievementDisplayActive(false);
                return;
            }

            if (achievementDisplayName != null)
            {
                achievementDisplayName.text = achievement.Name;
                AchievementDisplayAssetCatalog.ApplyAchievementNameColor(achievementDisplayName, achievement);
            }

            AchievementDisplayAssetCatalog.ApplyAchievementBackground(achievementDisplayBg, achievement);

            SetAchievementDisplayActive(true);
        }

        private int ResolveBuildingAchievementDisplayId()
        {
            if (buildingInfo == null)
            {
                return 0;
            }

            if (buildingInfo.playerId == ResolveSelfPlayerId())
            {
                return DataManager.Instance != null ? DataManager.Instance.GetDisplayedAchievementId() : 0;
            }

            return buildingInfo.displayedAchievementId;
        }

        private bool ShouldShowAchievementDisplay()
        {
            // 城镇建筑头顶成就称号暂时关闭。
            return false;
        }

        private void EnsureAchievementDisplayNodes()
        {
            ResolveAchievementDisplayReferences();
        }

        /// <summary>
        /// 解析成就称号预制体引用；布局请在 AchievementDisplay.prefab 中调整。
        /// </summary>
        private void ResolveAchievementDisplayReferences()
        {
            var loadedFromResources = achievementDisplayRoot != null
                                      && achievementDisplayRoot.name == "AchievementDisplay";
            if (!loadedFromResources)
            {
                if (achievementDisplayRoot != null)
                {
                    Destroy(achievementDisplayRoot);
                    achievementDisplayRoot = null;
                    achievementDisplayBg = null;
                    achievementDisplayName = null;
                }

                var existing = transform.Find("AchievementDisplay");
                if (existing != null)
                {
                    Destroy(existing.gameObject);
                }
            }

            if (achievementDisplayRoot == null)
            {
                var prefab = GameplayResourceStore.LoadAsset<GameObject>(AchievementDisplayPrefabPath);
                if (prefab != null)
                {
                    achievementDisplayRoot = Instantiate(prefab, transform, false);
                    achievementDisplayRoot.name = "AchievementDisplay";
                }
            }

            if (achievementDisplayRoot == null)
            {
                return;
            }

            achievementDisplayBg ??= achievementDisplayRoot.transform.Find("img_Bg")?.GetComponent<Image>();
            achievementDisplayName ??= achievementDisplayRoot.transform.Find("img_Bg/txt_Name")?.GetComponent<TMP_Text>()
                                       ?? achievementDisplayRoot.GetComponentInChildren<TMP_Text>(true);
            ApplyAchievementDisplayLayout();
        }

        private void ApplyAchievementDisplayLayout()
        {
            if (achievementDisplayRoot == null)
            {
                return;
            }

            if (achievementDisplayRoot.transform is RectTransform rect)
            {
                var anchoredPosition = rect.anchoredPosition;
                anchoredPosition.y = AchievementDisplayAnchoredYOffset;
                rect.anchoredPosition = anchoredPosition;
            }
        }

        private void SetAchievementDisplayActive(bool active)
        {
            if (achievementDisplayRoot != null)
            {
                achievementDisplayRoot.SetActive(active);
            }
        }
    }
}
