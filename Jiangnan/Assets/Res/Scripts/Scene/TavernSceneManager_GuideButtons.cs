using cfg;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.Model;
using JN.Client.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace JN.Client.Scene
{
    public partial class TavernSceneManager
    {
        #region Guide Buttons

        private const string GuideCounterPurchaseHudKey = "GuideCounterPurchaseHud";
        private const string GuideKitchenPurchaseHudKeyPrefix = "GuideKitchenPurchaseHud_";
        private const string GuideShopkeeperEmployHudKey = "GuideShopkeeperEmployHud";
        private const string GuideChefEmployHudKey = "GuideChefEmployHud";
        private const string GuideWaiterEmployHudKey = "GuideWaiterEmployHud";
        private const string GuideChefEmployHudKeyPrefix = "GuideChefEmployHud_";
        private const string GuideWaiterEmployHudKeyPrefix = "GuideWaiterEmployHud_";
        private const int WorldEmployMaxShopkeeper = 1;
        private const int WorldEmployMaxChef = 3;
        private const int WorldEmployMaxWaiter = 4;
        private const int DefaultWorldEmployCost = 300;
        private const float RecruitGuideButtonScaleMultiplier = 2f;
        private const float PurchaseGuideButtonScaleMultiplier = 1.5f;
        private const string CustomerEnterQueueFillSpritePath = "Assets/Res/Resources/Textures/UI/Icons 1/customerEnterProgressFillRed.png";

        private Transform chefEmployAnchorRoot;
        private readonly Transform[] chefEmployAnchors = new Transform[WorldEmployMaxChef];

        /// <summary>
        /// 确保场景购买和招聘按钮已经创建。
        /// </summary>
        private void EnsureGuideWorldButtons()
        {
            if (guideCounterButton == null)
            {
                guideCounterButton = CreateGuideWorldButtonFromPrefab(
                    GuideCounterButtonPrefabResourcePath,
                    "BuyCounterButton",
                    guideCounterBuildBase != null ? guideCounterBuildBase.transform : guideCounterObject != null ? guideCounterObject.transform : null,
                    new Vector3(0f, 0.22f, 0f),
                    string.Empty,
                    HandleBuyCounter);
                ScalePurchaseGuideButton(guideCounterButton);
            }

            if (guideStoveButton == null)
            {
                guideStoveButton = CreateGuideWorldButtonFromPrefab(
                    GuideStoveButtonPrefabResourcePath,
                    "BuyStoveButton",
                    guideStoveBuildBase != null ? guideStoveBuildBase.transform : guideStoveObject != null ? guideStoveObject.transform : null,
                    new Vector3(0f, 0.22f, 0f),
                    string.Empty,
                    () => HandleBuyKitchenItem("stove"));
                ScalePurchaseGuideButton(guideStoveButton);

                if (guideKitchenAnchors.Count > 0)
                {
                    guideKitchenAnchors[0].button = guideStoveButton;
                }
            }

            EnsureGuideKitchenButtons();

            // 场景掌柜/厨师/小二旧版世界按钮创建已关闭（原先也会被强制 SetActive(false)）。
            // if (guideShopkeeperButton == null)
            // {
            //     guideShopkeeperButton = CreateGuideWorldButton(
            //         "HireShopkeeperButton",
            //         guideCounterObject != null ? guideCounterObject.transform : null,
            //         new Vector3(0f, 1.35f, 0f),
            //         string.Empty,
            //         HandleHireShopkeeper);
            //     SetGuideButtonSprite(guideShopkeeperButton, GuideRecruitShopkeeperSpritePath);
            //     ScaleRecruitGuideButton(guideShopkeeperButton);
            // }
            //
            // if (guideChefButton == null)
            // {
            //     guideChefButton = CreateGuideWorldButton(
            //         "HireChefButton",
            //         guideStoveObject != null ? guideStoveObject.transform : null,
            //         new Vector3(0f, 1.2f, 0f),
            //         string.Empty,
            //         HandleHireChef);
            //     SetGuideButtonSprite(guideChefButton, GuideRecruitChefSpritePath);
            //     ScaleRecruitGuideButton(guideChefButton);
            // }
            //
            // if (guideWaiterButton == null)
            // {
            //     guideWaiterButton = CreateGuideWorldButton(
            //         "HireWaiterButton",
            //         guideCounterObject != null ? guideCounterObject.transform : null,
            //         new Vector3(-0.85f, 1.2f, -0.95f),
            //         string.Empty,
            //         HandleHireWaiter);
            //     SetGuideButtonSprite(guideWaiterButton, GuideRecruitWaiterSpritePath);
            //     ScaleRecruitGuideButton(guideWaiterButton);
            // }

            EnsureGuideBuildBaseColliders();
        }

        /// <summary>
        /// 确保门口顾客倒计时标签已经创建。
        /// </summary>
        private void EnsureGuideWorldLabels()
        {
            if (nextCustomerTimerLabel?.rectTransform != null)
            {
                nextCustomerTimerLabel.rectTransform.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 创建跟随场景目标的引导按钮。
        /// </summary>
        /// <param name="name">名称。</param>
        /// <param name="target">目标对象。</param>
        /// <param name="worldOffset">参数值。</param>
        /// <param name="label">参数值。</param>
        /// <param name="onClick">参数值。</param>
        /// <returns>返回匹配到的对象引用。</returns>
        private GuideWorldButton CreateGuideWorldButton(string name, Transform target, Vector3 worldOffset, string label, UnityEngine.Events.UnityAction onClick)
        {
            return CreateGuideWorldButtonFromPrefab(GuideWorldButtonPrefabResourcePath, name, target, worldOffset, label, onClick);
        }

        /// <summary>
        /// 从 预制体 创建跟随场景目标的引导按钮。
        /// </summary>
        /// <param name="resourcePath">资源路径。</param>
        /// <param name="name">名称。</param>
        /// <param name="target">目标对象。</param>
        /// <param name="worldOffset">参数值。</param>
        /// <param name="label">参数值。</param>
        /// <param name="onClick">参数值。</param>
        /// <returns>返回匹配到的对象引用。</returns>
        private GuideWorldButton CreateGuideWorldButtonFromPrefab(string resourcePath, string name, Transform target, Vector3 worldOffset, string label, UnityEngine.Events.UnityAction onClick)
        {
            if (canvasParent == null || target == null)
            {
                return null;
            }

            if (resourcePath == GuideStoveButtonPrefabResourcePath || resourcePath == GuideCounterButtonPrefabResourcePath)
            {
                label = string.Empty;
            }

            var prefab = GameplayResourceStore.LoadAsset<GameObject>($"Assets/Res/Resources/{resourcePath}.prefab");
            if (prefab == null)
            {
                return CreateGuideWorldButton(name, target, worldOffset, label, onClick);
            }

            var instance = Instantiate(prefab, canvasParent, false);
            if (instance == null)
            {
                return CreateGuideWorldButton(name, target, worldOffset, label, onClick);
            }

            instance.name = name;

            var rectTransform = instance.GetComponent<RectTransform>();
            var button = instance.GetComponent<Button>();
            var image = button != null ? button.GetComponent<Image>() : instance.GetComponent<Image>();
            var tmpText = FindGuideButtonTmpText(instance.transform);
            var text = tmpText == null ? instance.GetComponentInChildren<Text>(true) : null;
            if (rectTransform == null || button == null || (tmpText == null && text == null))
            {
                Destroy(instance);
                return CreateGuideWorldButton(name, target, worldOffset, label, onClick);
            }

            button.onClick.RemoveAllListeners();
            if (onClick != null)
            {
                button.onClick.AddListener(() =>
                {
                    GameAudioManager.PlayButtonClick();
                    onClick.Invoke();
                });
            }

            SetGuideButtonTextInternal(tmpText, text, label);

            var guideButton = new GuideWorldButton
            {
                rectTransform = rectTransform,
                button = button,
                image = image,
                text = text,
                tmpText = tmpText,
                target = target,
                worldOffset = worldOffset,
                scale = Vector3.one
            };

            guideWorldButtons.Add(guideButton);
            return guideButton;
        }

        /// <summary>
        /// 用指定图片资源替换招聘按钮底图。
        /// </summary>
        /// <param name="guideButton">需要替换图片的按钮。</param>
        /// <param name="spritePath">Sprite 资源路径。</param>
        private static void SetGuideButtonSprite(GuideWorldButton guideButton, string spritePath)
        {
            if (guideButton == null || guideButton.image == null || string.IsNullOrEmpty(spritePath))
            {
                return;
            }

            var sprite = LoadGuideButtonSprite(spritePath);
            if (sprite == null)
            {
                return;
            }

            guideButton.image.sprite = sprite;
            guideButton.image.color = Color.white;
            guideButton.image.type = Image.Type.Simple;
            guideButton.image.preserveAspect = true;
        }

        /// <summary>
        /// 在编辑器环境中按路径读取招聘按钮图片。
        /// </summary>
        /// <param name="spritePath">Sprite 资源路径。</param>
        /// <returns>读取成功时返回 Sprite，否则返回 null。</returns>
        private static Sprite LoadGuideButtonSprite(string spritePath)
        {
            if (GuideButtonSpriteCache.TryGetValue(spritePath, out var cachedSprite))
            {
                return cachedSprite;
            }

            var sprite = GameplayResourceStore.LoadAsset<Sprite>(spritePath);
            GuideButtonSpriteCache[spritePath] = sprite;
            return sprite;
        }

        /// <summary>
        /// 将招聘按钮放大一倍，增强场景内点击识别度。
        /// </summary>
        /// <param name="guideButton">需要缩放的招聘按钮。</param>
        private static void ScaleRecruitGuideButton(GuideWorldButton guideButton)
        {
            if (guideButton?.rectTransform == null)
            {
                return;
            }

            guideButton.rectTransform.sizeDelta *= RecruitGuideButtonScaleMultiplier;
            guideButton.scale = Vector3.one * RecruitGuideButtonScaleMultiplier;
        }

        /// <summary>
        /// 将带价格的购买按钮放大，提升价格可读性和点击热区。
        /// </summary>
        private static void ScalePurchaseGuideButton(GuideWorldButton guideButton)
        {
            if (guideButton?.rectTransform == null)
            {
                return;
            }

            guideButton.rectTransform.sizeDelta *= PurchaseGuideButtonScaleMultiplier;
            guideButton.scale = Vector3.one * PurchaseGuideButtonScaleMultiplier;
        }

        /// <summary>
        /// 优先查找按钮里用于显示金额的 文本组件 文本。
        /// </summary>
        /// <param name="root">参数值。</param>
        /// <returns>返回匹配到的对象引用。</returns>
        private static TMP_Text FindGuideButtonTmpText(Transform root)
        {
            if (root == null)
            {
                return null;
            }

            var tmpTexts = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (var tmpText in tmpTexts)
            {
                if (tmpText != null && tmpText.name == "txt_CoinNum")
                {
                    return tmpText;
                }
            }

            return tmpTexts.Length > 0 ? tmpTexts[0] : null;
        }

        /// <summary>
        /// 更新引导按钮显示文本。
        /// </summary>
        /// <param name="guideButton">数据编号。</param>
        /// <param name="content">参数值。</param>
        private static void SetGuideButtonText(GuideWorldButton guideButton, string content)
        {
            if (guideButton == null)
            {
                return;
            }

            SetGuideButtonTextInternal(guideButton.tmpText, guideButton.text, content);
        }

        /// <summary>
        /// 按文本组件类型写入按钮文案。
        /// </summary>
        /// <param name="tmpText">参数值。</param>
        /// <param name="text">参数值。</param>
        /// <param name="content">参数值。</param>
        private static void SetGuideButtonTextInternal(TMP_Text tmpText, Text text, string content)
        {
            if (tmpText != null)
            {
                tmpText.text = content;
                return;
            }

            if (text != null)
            {
                text.text = content;
            }
        }

        /// <summary>
        /// 把引导按钮调整成只显示价格的轻量样式。
        /// </summary>
        /// <param name="guideButton">数据编号。</param>
        /// <param name="size">参数值。</param>
        private static void ApplyPriceOnlyButtonStyle(GuideWorldButton guideButton, Vector2 size)
        {
            if (guideButton == null || guideButton.rectTransform == null || guideButton.text == null || guideButton.button == null)
            {
                return;
            }

            guideButton.rectTransform.sizeDelta = size;

            var image = guideButton.button.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(0f, 0f, 0f, 0f);
                image.raycastTarget = true;
            }

            guideButton.text.fontSize = 22;
            guideButton.text.alignment = TextAnchor.MiddleCenter;
            guideButton.text.color = new Color(1f, 0.95f, 0.82f, 1f);
        }

        /// <summary>
        /// 创建跟随场景目标的引导标签。
        /// </summary>
        /// <param name="name">名称。</param>
        /// <param name="target">目标对象。</param>
        /// <param name="worldOffset">参数值。</param>
        /// <param name="label">参数值。</param>
        /// <returns>返回方法执行后的结果。</returns>
        private GuideWorldLabel CreateGuideWorldLabel(string name, Transform target, Vector3 worldOffset, string label)
        {
            if (canvasParent == null || target == null)
            {
                return null;
            }

            var prefab = GameplayResourceStore.LoadAsset<GameObject>($"Assets/Res/Resources/{GuideWorldLabelPrefabResourcePath}.prefab");
            if (prefab == null)
            {
                return null;
            }

            var instance = Instantiate(prefab, canvasParent, false);
            if (instance == null)
            {
                return null;
            }

            instance.name = name;

            var rectTransform = instance.GetComponent<RectTransform>();
            var text = instance.GetComponentInChildren<Text>(true);
            if (rectTransform == null || text == null)
            {
                Destroy(instance);
                return null;
            }

            text.text = label;

            var guideLabel = new GuideWorldLabel
            {
                rectTransform = rectTransform,
                text = text,
                target = target,
                worldOffset = worldOffset
            };

            guideWorldLabels.Add(guideLabel);
            return guideLabel;
        }

        /// <summary>
        /// 创建门口顾客进入倒计时进度表现。
        /// </summary>
        /// <param name="name">节点名称。</param>
        /// <param name="target">跟随目标。</param>
        /// <param name="worldOffset">世界偏移。</param>
        /// <returns>进度表现引用。</returns>
        private GuideWorldLabel CreateCustomerEnterProgressLabel(string name, Transform target, Vector3 worldOffset)
        {
            if (canvasParent == null || target == null)
            {
                return null;
            }

            var prefab = GameplayResourceStore.LoadAsset<GameObject>("Assets/Res/Resources/UI/Runtime/CustomerEnterProgress.prefab");
            if (prefab == null)
            {
                return CreateGuideWorldLabel(name, target, worldOffset, string.Empty);
            }

            var instance = Instantiate(prefab, canvasParent, false);
            if (instance == null)
            {
                return CreateGuideWorldLabel(name, target, worldOffset, string.Empty);
            }

            instance.name = name;
            var rectTransform = instance.GetComponent<RectTransform>();
            var canvasGroup = instance.GetComponent<CanvasGroup>();
            var progressBackground = instance.transform.Find("img_ProgressBg")?.GetComponent<Image>();
            var progressFill = instance.transform.Find("img_ProgressBg/img_ProgressFill")?.GetComponent<Image>();
            var queueBackground = instance.transform.Find("img_QueueBg")?.GetComponent<Image>();
            var tmpText = instance.transform.Find("txt_Time")?.GetComponent<TMP_Text>() ?? instance.GetComponentInChildren<TMP_Text>(true);
            var text = instance.GetComponentInChildren<Text>(true);
            if (rectTransform == null || progressBackground == null || progressFill == null)
            {
                Destroy(instance);
                return CreateGuideWorldLabel(name, target, worldOffset, string.Empty);
            }

            rectTransform.localScale = Vector3.one * 2f;
            progressBackground.gameObject.SetActive(true);
            progressFill.fillAmount = 0f;
            if (queueBackground != null)
            {
                queueBackground.gameObject.SetActive(false);
            }

            if (tmpText != null)
            {
                tmpText.text = "-- s";
                tmpText.gameObject.SetActive(true);
            }

            if (text != null)
            {
                text.text = string.Empty;
                text.gameObject.SetActive(false);
            }

            var guideLabel = new GuideWorldLabel
            {
                rectTransform = rectTransform,
                text = text,
                tmpText = tmpText,
                progressBackground = progressBackground,
                progressFill = progressFill,
                queueBackground = queueBackground,
                canvasGroup = canvasGroup,
                target = target,
                worldOffset = worldOffset,
                scale = Vector3.one * 2f,
                defaultProgressSprite = progressFill.sprite,
                queuedProgressSprite = GameplayResourceStore.LoadAsset<Sprite>(CustomerEnterQueueFillSpritePath)
            };

            guideWorldLabels.Add(guideLabel);
            return guideLabel;
        }

        /// <summary>
        /// 根据新手任务进度刷新按钮显隐和价格。
        /// </summary>
        /// <param name="guide">数据编号。</param>
        private void RefreshGuideWorldButtons(GameplayGuideSaveData guide)
        {
            var isBusinessOpen = DataManager.Instance != null
                                 && DataManager.Instance.TavernData != null
                                 && DataManager.Instance.TavernData.isOpen;
            var showCounterButton = DataManager.Instance.ShouldShowGuideBasicEquipmentPurchase("counter");
            if (guideCounterButton != null)
            {
                guideCounterButton.rectTransform.gameObject.SetActive(false);
            }
            RefreshGuidePurchaseActionHud(
                GuideCounterPurchaseHudKey,
                showCounterButton,
                guideCounterBuildBase != null ? guideCounterBuildBase.transform : guideCounterObject != null ? guideCounterObject.transform : null,
                GetGuideFacilityCostByKey("counter"),
                HandleBuyCounter,
                showDeliveryIcon: guideCounterDeliveryPending);

            var showStoveButton = ShouldShowGuideKitchenButton("stove");
            if (guideStoveButton != null)
            {
                guideStoveButton.rectTransform.gameObject.SetActive(false);
            }
            RefreshGuidePurchaseActionHud(
                $"{GuideKitchenPurchaseHudKeyPrefix}stove",
                showStoveButton,
                guideStoveBuildBase != null ? guideStoveBuildBase.transform : guideStoveObject != null ? guideStoveObject.transform : null,
                GetGuideFacilityCostByKey("stove"),
                () => HandleBuyKitchenItem("stove"),
                showDeliveryIcon: guideStoveDeliveryPending || guidePendingKitchenItems.Contains("stove"));

            for (var i = 1; i < guideKitchenAnchors.Count; i++)
            {
                var itemKey = guideKitchenAnchors[i].itemKey;
                var showKitchenButton = ShouldShowGuideKitchenButton(itemKey);
                var stoveButton = guideKitchenAnchors[i].button;
                if (stoveButton != null && stoveButton.rectTransform != null)
                {
                    stoveButton.rectTransform.gameObject.SetActive(false);
                }

                var capturedItemKey = itemKey;
                RefreshGuidePurchaseActionHud(
                    $"{GuideKitchenPurchaseHudKeyPrefix}{itemKey}",
                    showKitchenButton,
                    guideKitchenAnchors[i].buildBase != null ? guideKitchenAnchors[i].buildBase.transform : guideKitchenAnchors[i].sceneObject != null ? guideKitchenAnchors[i].sceneObject.transform : null,
                    GetGuideFacilityCostByKey(capturedItemKey),
                    () => HandleBuyKitchenItem(capturedItemKey),
                    showDeliveryIcon: guidePendingKitchenItems.Contains(capturedItemKey));
            }

            if (guideShopkeeperButton != null)
            {
                guideShopkeeperButton.rectTransform.gameObject.SetActive(false);
                ClearGuideButtonText(guideShopkeeperButton);
            }

            if (guideChefButton != null)
            {
                guideChefButton.rectTransform.gameObject.SetActive(false);
                ClearGuideButtonText(guideChefButton);
            }

            if (guideWaiterButton != null)
            {
                guideWaiterButton.rectTransform.gameObject.SetActive(false);
                ClearGuideButtonText(guideWaiterButton);
            }

            RefreshGuideRecruitBases(guide, isBusinessOpen, GuidePresentationAdapter.BuildWorldPresentation(TavernGuideService.Instance));
        }

        /// <summary>
        /// 判断厨房购买按钮是否应该在当前任务阶段显示。
        /// </summary>
        /// <param name="itemKey">厨房物件键值。</param>
        /// <returns>应该显示购买按钮时返回 true。</returns>
        private bool ShouldShowGuideKitchenButton(string itemKey)
        {
            if (DataManager.Instance == null || DataManager.Instance.TavernData == null)
            {
                return false;
            }

            // 厨房桌子1 跟灶台显示、不单独购买；桌子2 仍屏蔽建造入口。
            if (itemKey == "kitchen_table_1" || itemKey == "kitchen_table_2")
            {
                return false;
            }

            if (itemKey == "cabinet_1" || itemKey == "cabinet_2" || itemKey == "cabinet_3" || itemKey == "cabinet_4"
                || itemKey == "jiaozi" || itemKey == "stairs")
            {
                return DataManager.Instance.ShouldShowGuideBasicEquipmentPurchase(itemKey);
            }

            if (itemKey == "stove" || itemKey == "furnace")
            {
                return DataManager.Instance.ShouldShowGuideKitchenEquipmentPurchase(itemKey);
            }

            return false;
        }

        /// <summary>
        /// 判断引导建造是否仍在配送或落位表现中（仅用于招聘等需等搬运结束的流程）。
        /// 桌位搬运/升级不再阻塞其它建造点击，各桌位自行用 IsTableUpgrading 控制。
        /// </summary>
        public bool HasGuideBuildPlacementPending()
        {
            return guideCounterDeliveryPending
                   || guideStoveDeliveryPending
                   || guidePendingKitchenItems.Count > 0;
        }

        /// <summary>
        /// 标记或清除引导桌位建造落位状态。
        /// </summary>
        public void MarkGuideTablePlacementPending(int tableId, bool pending)
        {
            if (tableId <= 0)
            {
                return;
            }

            if (pending)
            {
                guidePendingTablePlacementIds.Add(tableId);
            }
            else
            {
                guidePendingTablePlacementIds.Remove(tableId);
            }

            DataManager.Instance?.MarkGuideBuildPlacementPending($"table_{tableId}", pending);
        }

        /// <summary>
        /// 桌位是否处于首次建造的搬运落位中。
        /// </summary>
        public bool IsGuideTablePlacementPending(int tableId)
        {
            return tableId > 0 && guidePendingTablePlacementIds.Contains(tableId);
        }

        /// <summary>
        /// 清空招聘图片按钮上的文字，避免复用按钮预制体时残留价格或描述。
        /// </summary>
        /// <param name="guideButton">招聘按钮。</param>
        private static void ClearGuideButtonText(GuideWorldButton guideButton)
        {
            SetGuideButtonText(guideButton, string.Empty);
            if (guideButton?.rectTransform == null)
            {
                return;
            }

            foreach (var tmpText in guideButton.rectTransform.GetComponentsInChildren<TMP_Text>(true))
            {
                tmpText.text = string.Empty;
            }

            foreach (var text in guideButton.rectTransform.GetComponentsInChildren<Text>(true))
            {
                text.text = string.Empty;
            }
        }

        /// <summary>
        /// 刷新门口下一位顾客倒计时文本。
        /// </summary>
        private void RefreshNextCustomerTimerLabel()
        {
            if (nextCustomerTimerLabel == null || nextCustomerTimerLabel.rectTransform == null)
            {
                return;
            }

            var shouldShow = DataManager.Instance != null && DataManager.Instance.TavernData.isOpen;
            nextCustomerTimerLabel.rectTransform.gameObject.SetActive(shouldShow);
            if (!shouldShow)
            {
                if (nextCustomerTimerLabel.queueBackground != null)
                {
                    nextCustomerTimerLabel.queueBackground.gameObject.SetActive(false);
                }

                if (nextCustomerTimerLabel.progressFill != null)
                {
                    nextCustomerTimerLabel.progressFill.sprite = nextCustomerTimerLabel.defaultProgressSprite;
                    nextCustomerTimerLabel.progressFill.fillAmount = 0f;
                }

                if (nextCustomerTimerLabel.tmpText != null)
                {
                    nextCustomerTimerLabel.tmpText.text = "-- s";
                }

                return;
            }

            var customerSpawnInterval = GetEffectiveCustomerSpawnInterval();
            var progress = customerSpawnInterval <= 0.01f
                ? 1f
                : 1f - Mathf.Clamp01(nextCustomerSpawnRemaining / customerSpawnInterval);
            var remainingSeconds = Mathf.Max(0, Mathf.CeilToInt(nextCustomerSpawnRemaining));
            var queueCount = GetQueueCustomerCount();
            var hasQueue = queueCount > 0;

            if (nextCustomerTimerLabel.progressBackground != null)
            {
                nextCustomerTimerLabel.progressBackground.gameObject.SetActive(true);
            }

            if (nextCustomerTimerLabel.queueBackground != null)
            {
                nextCustomerTimerLabel.queueBackground.gameObject.SetActive(hasQueue);
            }

            if (nextCustomerTimerLabel.progressFill != null)
            {
                nextCustomerTimerLabel.progressFill.sprite = hasQueue && nextCustomerTimerLabel.queuedProgressSprite != null
                    ? nextCustomerTimerLabel.queuedProgressSprite
                    : nextCustomerTimerLabel.defaultProgressSprite;
                nextCustomerTimerLabel.progressFill.fillAmount = hasQueue ? 1f : progress;
            }

            if (nextCustomerTimerLabel.tmpText != null)
            {
                nextCustomerTimerLabel.tmpText.text = hasQueue ? $"{queueCount}人排队中" : $"{remainingSeconds} s";
                nextCustomerTimerLabel.tmpText.gameObject.SetActive(true);
            }

            if (nextCustomerTimerLabel.text != null)
            {
                nextCustomerTimerLabel.text.text = string.Empty;
                nextCustomerTimerLabel.text.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 为厨房购买点创建对应价格按钮。
        /// </summary>
        private void EnsureGuideKitchenButtons()
        {
            for (var i = 1; i < guideKitchenAnchors.Count; i++)
            {
                var anchor = guideKitchenAnchors[i];
                if (anchor == null || anchor.button != null)
                {
                    continue;
                }

                var button = CreateGuideWorldButtonFromPrefab(
                    GuideStoveButtonPrefabResourcePath,
                    i == 0 ? "BuyStoveButton" : $"BuyStoveButton_{i}",
                    anchor.buildBase != null ? anchor.buildBase.transform : anchor.sceneObject != null ? anchor.sceneObject.transform : null,
                    new Vector3(0f, 0.22f, 0f),
                    string.Empty,
                    () => HandleBuyKitchenItem(anchor.itemKey));

                if (button != null)
                {
                    ScalePurchaseGuideButton(button);
                    anchor.button = button;
                }
            }
        }

        /// <summary>
        /// 为场景中的购买提示底板补齐碰撞体，支持直接点击底板购买。
        /// 酒柜/柜子：触碰区域贴合设施模型体积，不再使用地面建造板。
        /// </summary>
        private void EnsureGuideBuildBaseColliders()
        {
            EnsureGuideBuildBaseCollider(guideCounterBuildBase);
            EnsureGuideBuildBaseCollider(guideStoveBuildBase);
            EnsureGuideBuildBaseCollider(guideShopkeeperRecruitBase);
            EnsureGuideBuildBaseCollider(guideChefRecruitBase);
            EnsureGuideBuildBaseCollider(guideWaiterRecruitBase);

            for (var index = 0; index < guideKitchenAnchors.Count; index++)
            {
                var anchor = guideKitchenAnchors[index];
                if (anchor == null)
                {
                    continue;
                }

                if (IsCabinetOrWineCabinetGuideKey(anchor.itemKey))
                {
                    // 禁用地面「xx建造」碰撞，避免点到地面。
                    SetGuideClickCollidersEnabled(anchor.buildBase, false);
                    var canClickModel = ShouldShowGuideKitchenButton(anchor.itemKey)
                                        && anchor.sceneObject != null
                                        && anchor.sceneObject.activeInHierarchy;
                    if (canClickModel)
                    {
                        EnsureGuideBuildBaseCollider(anchor.sceneObject);
                    }
                    else
                    {
                        SetGuideClickCollidersEnabled(anchor.sceneObject, false);
                    }

                    continue;
                }

                EnsureGuideBuildBaseCollider(anchor.buildBase);
            }
        }

        private static bool IsCabinetOrWineCabinetGuideKey(string itemKey)
        {
            return itemKey == "cabinet_1"
                   || itemKey == "cabinet_2"
                   || itemKey == "cabinet_3"
                   || itemKey == "cabinet_4";
        }

        /// <summary>轿子/楼梯等：点地面建造板购买（与灶台一致）。</summary>
        private static bool UsesGuideBuildBaseClick(string itemKey)
        {
            return itemKey == "jiaozi" || itemKey == "stairs";
        }

        /// <summary>
        /// 启用/禁用物体上用于点击的 Collider（仅自身，不含子节点）。
        /// </summary>
        private static void SetGuideClickCollidersEnabled(GameObject root, bool enabled)
        {
            if (root == null)
            {
                return;
            }

            var colliders = root.GetComponents<Collider>();
            for (var index = 0; index < colliders.Length; index++)
            {
                if (colliders[index] != null)
                {
                    colliders[index].enabled = enabled;
                }
            }
        }

        /// <summary>
        /// 场景雇佣 HUD：已改回底栏员工按钮招募，此处仅清理并隐藏旧挂点。
        /// </summary>
        private void RefreshGuideRecruitBases(GameplayGuideSaveData guide, bool isBusinessOpen, GuideWorldPresentation presentation)
        {
            if (guideShopkeeperRecruitBase != null)
            {
                guideShopkeeperRecruitBase.SetActive(false);
            }

            if (guideChefRecruitBase != null)
            {
                guideChefRecruitBase.SetActive(false);
            }

            if (guideWaiterRecruitBase != null)
            {
                guideWaiterRecruitBase.SetActive(false);
            }

            ClearAllWorldEmployActionHuds();
        }

        /// <summary>
        /// 注册或更新单个场景雇佣 HUD。
        /// </summary>
        private static void RefreshWorldEmployActionHud(
            string employKey,
            Transform target,
            StaffPosition position,
            int cost,
            System.Action onEmploy)
        {
            if (target == null)
            {
                HudOverlayService.UnregisterEmployActionHud(employKey);
                return;
            }

            HudOverlayService.RegisterEmployActionHud(employKey, target, position, cost, onEmploy);
        }

        /// <summary>
        /// 清理全部场景雇佣 HUD（含旧递进槽位键）。
        /// </summary>
        private static void ClearAllWorldEmployActionHuds()
        {
            HudOverlayService.UnregisterEmployActionHud(GuideShopkeeperEmployHudKey);
            HudOverlayService.UnregisterEmployActionHud(GuideChefEmployHudKey);
            HudOverlayService.UnregisterEmployActionHud(GuideWaiterEmployHudKey);
            for (var index = 1; index <= WorldEmployMaxChef; index++)
            {
                HudOverlayService.UnregisterEmployActionHud($"{GuideChefEmployHudKeyPrefix}{index}");
            }

            for (var index = 1; index <= WorldEmployMaxWaiter; index++)
            {
                HudOverlayService.UnregisterEmployActionHud($"{GuideWaiterEmployHudKeyPrefix}{index}");
            }
        }

        /// <summary>
        /// 点击场景雇佣图标：按职位直接雇佣并播放入场。
        /// </summary>
        private void HandleWorldDirectHire(StaffPosition position)
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null)
            {
                return;
            }

            var cost = position switch
            {
                StaffPosition.Shopkeeper => TbConfigRuntime.GetShopkeeperEmployCost(DefaultWorldEmployCost),
                StaffPosition.Chef => TbConfigRuntime.GetChefEmployCost(DefaultWorldEmployCost),
                _ => TbConfigRuntime.GetWaiterEmployCost(DefaultWorldEmployCost)
            };

            if (!dataManager.TryDirectHireByPosition(position, cost, out var message, out var hiredStaffId))
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    HudOverlayService.ShowFloatingWarning(message);
                }

                return;
            }

            PlayWorldHireEnterPresentation(position, hiredStaffId);
        }

        /// <summary>
        /// 场景 NewEmployBtn：直招招聘界面对应固定槽员工（费用读 Staff 表）。
        /// </summary>
        private void HandleWorldDirectHireFixedStaff(int staffId, StaffPosition position)
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null)
            {
                return;
            }

            if (!dataManager.TryHireConfiguredStaff(staffId, out var message, out var hiredStaffId, overrideCost: null))
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    HudOverlayService.ShowFloatingWarning(message);
                }

                return;
            }

            PlayWorldHireEnterPresentation(position, hiredStaffId);
        }

        private void PlayWorldHireEnterPresentation(StaffPosition position, int hiredStaffId)
        {
            switch (position)
            {
                case StaffPosition.Shopkeeper:
                    GameAudioManager.PlayRecruitShopkeeper();
                    PlayGuideShopkeeperEnterFromBottomRecruit(hiredStaffId);
                    break;
                case StaffPosition.Chef:
                    GameAudioManager.PlayRecruitChef();
                    PlayGuideChefEnterFromBottomRecruit(hiredStaffId);
                    break;
                default:
                    GameAudioManager.PlayRecruitWaiter();
                    PlayGuideWaiterEnterFromBottomRecruit(hiredStaffId);
                    break;
            }
        }

        /// <summary>
        /// 在厨师站位创建运行时挂点，供雇佣 HUD 跟随。
        /// </summary>
        private Transform EnsureChefEmployAnchor(int slotIndex)
        {
            var safeIndex = Mathf.Clamp(slotIndex, 0, WorldEmployMaxChef - 1);
            if (chefEmployAnchors[safeIndex] != null)
            {
                chefEmployAnchors[safeIndex].position = ResolveGuideChefHomePosition(safeIndex);
                return chefEmployAnchors[safeIndex];
            }

            if (chefEmployAnchorRoot == null)
            {
                var rootObject = new GameObject("ChefEmployAnchors");
                chefEmployAnchorRoot = rootObject.transform;
                chefEmployAnchorRoot.SetParent(transform, false);
            }

            var anchorObject = new GameObject($"ChefEmployAnchor_{safeIndex + 1}");
            var anchor = anchorObject.transform;
            anchor.SetParent(chefEmployAnchorRoot, false);
            anchor.position = ResolveGuideChefHomePosition(safeIndex);
            chefEmployAnchors[safeIndex] = anchor;
            return anchor;
        }

        /// <summary>
        /// 按建造点显隐刷新对应的世界价格层。
        /// </summary>
        private void RefreshGuidePurchaseActionHud(
            string purchaseKey,
            bool visible,
            Transform target,
            int cost,
            System.Action onPurchase,
            bool showDeliveryIcon = false)
        {
            if (DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern)
            {
                visible = false;
                showDeliveryIcon = false;
            }

            if ((!visible && !showDeliveryIcon) || target == null)
            {
                HudOverlayService.UnregisterPurchaseActionHud(purchaseKey);
                return;
            }

            var purchaseUi = HudOverlayService.RegisterPurchaseActionHud(
                purchaseKey,
                target,
                showDeliveryIcon ? null : onPurchase);
            if (showDeliveryIcon)
            {
                purchaseUi?.SetDeliveryPurchaseIcon(true);
                return;
            }

            purchaseUi?.SetDeliveryPurchaseIcon(false);
            purchaseUi?.SetUnlockPrompt(true, cost);
        }

        private const float GuideBuildBaseColliderMinSize = 0.2f;
        private const float GuideBuildBaseColliderPadding = 1.15f;

        /// <summary>
        /// 为单个购买/招聘提示底板补齐碰撞体。
        /// 贴地 Sprite（X=90°）须用本地 sprite.bounds，不能直接把世界 AABB 尺寸写入 BoxCollider。
        /// </summary>
        private static void EnsureGuideBuildBaseCollider(GameObject buildBase)
        {
            if (buildBase == null)
            {
                return;
            }

            if (!TryGetGuideBuildBaseLocalBounds(buildBase, out var localBounds))
            {
                return;
            }

            var boxCollider = buildBase.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = buildBase.AddComponent<BoxCollider>();
            }

            boxCollider.isTrigger = true;
            boxCollider.center = localBounds.center;
            boxCollider.size = new Vector3(
                Mathf.Max(GuideBuildBaseColliderMinSize, localBounds.size.x * GuideBuildBaseColliderPadding),
                Mathf.Max(GuideBuildBaseColliderMinSize, localBounds.size.y * GuideBuildBaseColliderPadding),
                Mathf.Max(GuideBuildBaseColliderMinSize, localBounds.size.z * GuideBuildBaseColliderPadding));
        }

        /// <summary>
        /// 计算底板在本地空间的包围盒，优先使用 SpriteRenderer.sprite.bounds。
        /// </summary>
        private static bool TryGetGuideBuildBaseLocalBounds(GameObject buildBase, out Bounds localBounds)
        {
            localBounds = default;

            var spriteRenderer = buildBase.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                localBounds = spriteRenderer.sprite.bounds;
                return true;
            }

            var renderers = buildBase.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }

            var localMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var localMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var hasAny = false;
            var buildTransform = buildBase.transform;

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                var worldBounds = renderer.bounds;
                var worldCenter = worldBounds.center;
                var worldExtents = worldBounds.extents;
                for (var cornerX = -1; cornerX <= 1; cornerX += 2)
                {
                    for (var cornerY = -1; cornerY <= 1; cornerY += 2)
                    {
                        for (var cornerZ = -1; cornerZ <= 1; cornerZ += 2)
                        {
                            var worldCorner = worldCenter + new Vector3(
                                worldExtents.x * cornerX,
                                worldExtents.y * cornerY,
                                worldExtents.z * cornerZ);
                            var localCorner = buildTransform.InverseTransformPoint(worldCorner);
                            localMin = Vector3.Min(localMin, localCorner);
                            localMax = Vector3.Max(localMax, localCorner);
                        }
                    }
                }

                hasAny = true;
            }

            if (!hasAny)
            {
                return false;
            }

            localBounds = new Bounds((localMin + localMax) * 0.5f, localMax - localMin);
            return true;
        }


        /// <summary>
        /// 命中场景购买底板时执行对应购买。
        /// </summary>
        private bool TryHandleGuideBuildBaseClick(Collider hitCollider)
        {
            if (hitCollider == null || DataManager.Instance == null)
            {
                return false;
            }

            if (DataManager.Instance.TavernData == null
                || !DataManager.Instance.AllowsFacilityPurchaseNow())
            {
                return false;
            }

            if (TryHandleGuideRecruitBaseClick(hitCollider))
            {
                return true;
            }

            if (guideCounterBuildBase != null && hitCollider.GetComponentInParent<Transform>() != null
                && hitCollider.transform.IsChildOf(guideCounterBuildBase.transform))
            {
                HandleBuyCounter();
                return true;
            }

            if (guideStoveBuildBase != null && hitCollider.transform.IsChildOf(guideStoveBuildBase.transform))
            {
                HandleBuyKitchenItem("stove");
                return true;
            }

            for (var index = 0; index < guideKitchenAnchors.Count; index++)
            {
                var anchor = guideKitchenAnchors[index];
                if (anchor == null)
                {
                    continue;
                }

                // 酒柜/柜子：点设施模型；轿子/楼梯：点建造板；其余：点地面建造板。
                if (IsCabinetOrWineCabinetGuideKey(anchor.itemKey))
                {
                    if (anchor.sceneObject == null
                        || !hitCollider.transform.IsChildOf(anchor.sceneObject.transform))
                    {
                        continue;
                    }
                }
                else if (UsesGuideBuildBaseClick(anchor.itemKey))
                {
                    var clickOnBuildBase = anchor.buildBase != null
                                           && hitCollider.transform.IsChildOf(anchor.buildBase.transform);
                    var clickOnModel = anchor.sceneObject != null
                                       && hitCollider.transform.IsChildOf(anchor.sceneObject.transform);
                    if (!clickOnBuildBase && !clickOnModel)
                    {
                        continue;
                    }
                }
                else
                {
                    if (anchor.buildBase == null
                        || !hitCollider.transform.IsChildOf(anchor.buildBase.transform))
                    {
                        continue;
                    }
                }

                HandleBuyKitchenItem(anchor.itemKey);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 命中招聘地块时执行对应招聘。
        /// </summary>
        private bool TryHandleGuideRecruitBaseClick(Collider hitCollider)
        {
            if (DataManager.Instance == null
                || DataManager.Instance.TavernData == null
                || DataManager.Instance.IsVisitingOtherTavern
                || DataManager.Instance.IsOnboardingGuideActive())
            {
                return false;
            }

            var guide = DataManager.Instance.GameplayGuideData;
            if (guide == null
                || !guide.recruitmentUnlocked
                || !DataManager.Instance.IsStaffRecruitUiUnlockedByAchievement())
            {
                return false;
            }

            // 场景掌柜/厨师/小二招聘地块点击已关闭。
            // if (guideShopkeeperRecruitBase != null
            //     && hitCollider.transform.IsChildOf(guideShopkeeperRecruitBase.transform)
            //     && DataManager.Instance.CanHireMoreGuideShopkeeper())
            // {
            //     HandleHireShopkeeper();
            //     return true;
            // }
            //
            // if (guideChefRecruitBase != null
            //     && hitCollider.transform.IsChildOf(guideChefRecruitBase.transform)
            //     && DataManager.Instance.CanHireMoreGuideChef())
            // {
            //     HandleHireChef();
            //     return true;
            // }
            //
            // if (guideWaiterRecruitBase != null
            //     && hitCollider.transform.IsChildOf(guideWaiterRecruitBase.transform)
            //     && DataManager.Instance.CanHireMoreGuideWaiter())
            // {
            //     HandleHireWaiter();
            //     return true;
            // }

            return false;
        }

        #endregion
    }
}
