using System;
using System.Collections;
using System.Collections.Generic;
using cfg;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.Model;
using JN.Client.Tools;
using JN.Client.UI;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
namespace JN.Client.Scene
{
    /// <summary>
    /// 负责酒楼场景相关的运行时逻辑。
    /// </summary>
    public partial class TavernSceneManager
    {
        #region Guide Constants And State
        private const string GuideCounterCarrierPrefabPath = "Assets/Res/Resources/Equipment/Carriers/P_Equipment_CounterCarrier.prefab";
        private const string GuideStoveCarrierPrefabPath = "Assets/Res/Resources/Equipment/Carriers/P_Equipment_GuideStoveCarrier.prefab";
        private const string GuideFurnaceCarrierPrefabPath = "Assets/Res/Resources/Equipment/Carriers/P_Equipment_GuideFurnaceCarrier.prefab";
        private const string GuideWineCabinetCarrierPrefabPath = "Assets/Res/Resources/Equipment/Carriers/P_Equipment_GuideWineCabinetCarrier.prefab";
        private const string GuideWineCabinet2CarrierPrefabPath = "Assets/Res/Resources/Equipment/Carriers/P_Equipment_GuideWineCabinet2Carrier.prefab";
        private const string GuideCabinetCarrierPrefabPath = "Assets/Res/Resources/Equipment/Carriers/P_Equipment_GuideCabinetCarrier.prefab";
        private const string GuideKitchenTable1CarrierPrefabPath = "Assets/Res/Resources/Equipment/Carriers/P_Equipment_GuideKitchenTable1Carrier.prefab";
        private const string GuideKitchenTable2CarrierPrefabPath = "Assets/Res/Resources/Equipment/Carriers/P_Equipment_GuideKitchenTable2Carrier.prefab";
        private const string GuideBuildingSuccessEffectPrefabPath = "Assets/Res/Resources/Effect/UIEffect_BuildingSuccess.prefab";
        private const string GuideCounterButtonPrefabResourcePath = "UI/Buttons/BuyCounterButton";
        private const string GuideStoveButtonPrefabResourcePath = "UI/Buttons/BuyStoveButton";
        private const string GuideWorldButtonPrefabResourcePath = "UI/Guides/GuideWorldButton";
        private const string GuideWorldLabelPrefabResourcePath = "UI/Guides/GuideWorldLabel";
        private const string CustomerEnterProgressPrefabResourcePath = "UI/Runtime/CustomerEnterProgress";
        private const string GuideShopkeeperVisualKey = "Shopkeeper";
        private const string GuideChefVisualKey = "Chef";
        private const string GuideWaiterVisualKey = "Waiter";
        private const string GuideShopkeeperMarkerName = "P_Character_WaiterF01_Shopkeeper";
        private const string GuideChefMarkerName = "P_Character_Chef03_Chef";
        private const string WallLevelMaterialNamePrefix = "wallLv";
        private const string WallLevelMaterialPathFormat =
            "Assets/Res/Resources/Models/Objects/wall/Materials/wallLv{0}.mat";
        private const string GuideWaiterMarkerName = "P_Character_Waiter03_Waiter";
        private const string GuideRecruitChefSpritePath = "Assets/Res/Resources/Textures/UI/Panel/TavernScene/img_RecruitChef_Btn.png";
        private const string GuideRecruitShopkeeperSpritePath = "Assets/Res/Resources/Textures/UI/Panel/TavernScene/img_RecruitTavernKeeper_Btn.png";
        private const string GuideRecruitWaiterSpritePath = "Assets/Res/Resources/Textures/UI/Panel/TavernScene/img_RecruitWaiter_Btn.png";
        private const string QueuePointNameToken = "QueuePoint";
        private static readonly Dictionary<string, GameObject> GuideCarrierPrefabCache = new();
        private static readonly Dictionary<string, Sprite> GuideButtonSpriteCache = new();
        private static GameObject guideBuildingSuccessEffectPrefab;
        private bool guideCounterDeliveryPending;
        private bool guideStoveDeliveryPending;
        #endregion

        #region Scene Cache

        /// <summary>
        /// 把存档里的桌位状态恢复到当前场景。
        /// </summary>
        private void ApplySavedTableStates()
        {
            foreach (var tableEntry in AllTables)
            {
                tableEntry.Value.ApplySaveState(DataManager.Instance.GetTableData(tableEntry.Key));
            }
        }

        /// <summary>
        /// 拜访他人店：只应用解锁/等级，强制已解锁桌为 Idle（本地模拟语义），不依赖也不写回自家营业桌态。
        /// </summary>
        private void ApplyUnlockedTablesOnly()
        {
            foreach (var tableEntry in AllTables)
            {
                var tableId = tableEntry.Key;
                var tableData = DataManager.Instance.GetTableData(tableId);
                if (tableData == null)
                {
                    continue;
                }

                // 内存中切到 Idle/Locked 供拜访播种；SetTableRuntimeState 在拜访中不落盘。
                var visitState = tableData.isUnlocked
                    ? TavernTableRuntimeState.Idle
                    : TavernTableRuntimeState.Locked;
                DataManager.Instance.SetTableRuntimeState(tableId, visitState);

                var viewData = new TavernTableSaveData
                {
                    tableId = tableData.tableId,
                    isUnlocked = tableData.isUnlocked,
                    level = tableData.level,
                    runtimeState = (int)visitState,
                    totalServedCustomers = tableData.totalServedCustomers,
                    totalIncome = tableData.totalIncome
                };
                tableEntry.Value.ApplySaveState(viewData);
            }
        }

        /// <summary>
        /// 缓存场景或配置里的顾客模板。
        /// </summary>
        private void CacheCustomerTemplates()
        {
            customerTemplates.Clear();
            vipCustomerTemplates.Clear();
            rareCustomerTemplates.Clear();
            if (customerEntryPoint == null)
            {
                CacheCustomerPrefabsFromReferences();
                return;
            }

            foreach (Transform child in customerEntryPoint)
            {
                foreach (Transform grandChild in child)
                {
                    if (!IsCustomerTemplate(grandChild))
                    {
                        continue;
                    }

                    grandChild.gameObject.SetActive(false);
                    customerTemplates.Add(grandChild.gameObject);
                }
            }

            if (customerTemplates.Count == 0)
            {
                CacheCustomerPrefabsFromReferences();
            }
            else
            {
                CacheVipCustomerPrefabsFromReferences();
            }
        }

        /// <summary>
        /// 判断节点是否可作为顾客模板使用。
        /// </summary>
        /// <param name="candidate">数据编号。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private static bool IsCustomerTemplate(Transform candidate)
        {
            return candidate != null && candidate.name.Contains("Customer");
        }

        /// <summary>
        /// 缓存桌面菜品表现 预制体。
        /// </summary>
        private void CacheDishPrefabs()
        {
            dishPrefabs.Clear();
            platePrefab = LoadDishPrefab("Assets/Res/Resources/Models/Objects/plate/plate_P.prefab");

            var productPrefab1 = LoadDishPrefab("Assets/Res/Resources/Models/Objects/vegetable/vegetable01_P.prefab");
            var productPrefab2 = LoadDishPrefab("Assets/Res/Resources/Models/Objects/vegetable/vegetable02_P.prefab");
            var productPrefab3 = LoadDishPrefab("Assets/Res/Resources/Models/Objects/vegetable/vegetable03_P.prefab");
            var productPrefab4 = LoadDishPrefab("Assets/Res/Resources/Models/Objects/vegetable/vegetable04_P.prefab");

            if (productPrefab1 != null) dishPrefabs.Add(productPrefab1);
            if (productPrefab2 != null) dishPrefabs.Add(productPrefab2);
            if (productPrefab3 != null) dishPrefabs.Add(productPrefab3);
            if (productPrefab4 != null) dishPrefabs.Add(productPrefab4);
        }

        /// <summary>
        /// 查找顾客入口、出口和物件搬运起点。
        /// 排队锚点用 PeopleStartPoint；进店出生点用 EnterStartPoint（找不到再回退 Door / 入口）。
        /// </summary>
        private void ResolveSceneAnchors()
        {
            customerEntryPoint = FindSceneTransformByName("PeopleStartPoint");
            customerSpawnPoint = FindSceneTransformByName("EnterStartPoint")
                                 ?? FindSceneTransformByName("Door")
                                 ?? customerEntryPoint;
            // 离店仍走 Door，与进店出生点分开。
            customerExitPoint = FindSceneTransformByName("Door") ?? customerSpawnPoint ?? customerEntryPoint;
            CacheQueuePointAnchors();
            ResolveWaiterAttractPoint();
            objectMovePoint = FindSceneTransformByName("ObjectMovePoint")
                             ?? FindSceneTransformByName("PeopleStartPoint")
                             ?? FindSceneTransformByName("TableMoveCheckPoint");
            sceneObjectsRoot = FindSceneTransformByName("Objects");
        }

        /// <summary>
        /// 缓存入口下配置的显式排队目标点。
        /// </summary>
        private void CacheQueuePointAnchors()
        {
            queuePointAnchors.Clear();
            if (customerEntryPoint == null)
            {
                return;
            }

            foreach (Transform child in customerEntryPoint)
            {
                if (child == null || !IsQueuePointAnchor(child))
                {
                    continue;
                }

                queuePointAnchors.Add(child);
            }

            queuePointAnchors.Sort(CompareQueuePointAnchors);
        }

        /// <summary>
        /// 缓存 PeopleStartPoint/LaPoint 作为小二拉客站位。
        /// </summary>
        private void ResolveWaiterAttractPoint()
        {
            waiterAttractPoint = null;
            if (customerEntryPoint != null)
            {
                waiterAttractPoint = customerEntryPoint.Find(WaiterAttractPointName);
            }

            if (waiterAttractPoint == null)
            {
                waiterAttractPoint = FindSceneTransformByName(WaiterAttractPointName);
            }
        }

        /// <summary>
        /// 判断节点是否为排队目标点。
        /// </summary>
        private static bool IsQueuePointAnchor(Transform candidate)
        {
            return candidate != null
                   && candidate.name.IndexOf(QueuePointNameToken, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 按名称里的序号与层级顺序排序排队目标点。
        /// </summary>
        private static int CompareQueuePointAnchors(Transform left, Transform right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            var leftOrder = ExtractQueuePointOrder(left.name, left.GetSiblingIndex());
            var rightOrder = ExtractQueuePointOrder(right.name, right.GetSiblingIndex());
            var comparison = leftOrder.CompareTo(rightOrder);
            return comparison != 0 ? comparison : left.GetSiblingIndex().CompareTo(right.GetSiblingIndex());
        }

        /// <summary>
        /// 从目标点名称中提取排序序号，未配置数字时按 siblingIndex 回退。
        /// </summary>
        private static int ExtractQueuePointOrder(string name, int siblingIndex)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return 100000 + Mathf.Max(0, siblingIndex);
            }

            var digitStart = -1;
            for (var index = 0; index < name.Length; index++)
            {
                if (!char.IsDigit(name[index]))
                {
                    continue;
                }

                digitStart = index;
                break;
            }

            if (digitStart < 0)
            {
                return 100000 + Mathf.Max(0, siblingIndex);
            }

            var order = 0;
            for (var index = digitStart; index < name.Length; index++)
            {
                if (!char.IsDigit(name[index]))
                {
                    break;
                }

                order = order * 10 + (name[index] - '0');
            }

            return order;
        }

        /// <summary>
        /// 查找新手引导阶段的柜台、灶台和厨房物件。
        /// </summary>
        private void ResolveGuideSceneObjects()
        {
            HideGuideSceneCarrier("P_Equipment_CounterCarrier");
            HideGuideSceneCarrier("P_Equipment_StoveCarrier");

            guideCounterObject = FindGuideSceneObject("P_Equipment_Counter") ?? FindGuideTargetObject("P_Equipment_Counter") ?? FindGuideTargetObject("Counter");
            guideCounterBuildBase = FindGuideSceneObject("柜台建造") ?? FindGuideTargetObject("柜台建造");
            foodTableObject = FindGuideSceneObject("FoodTable") ?? FindGuideTargetObject("FoodTable");
            guideShopkeeperRecruitBase = FindGuideSceneObject("掌柜招聘");
            guideChefRecruitBase = FindGuideSceneObject("厨师招聘");
            guideWaiterRecruitBase = FindGuideSceneObject("小二招聘");

            guideStoveSceneObjects.Clear();
            guideKitchenAnchors.Clear();
            AddGuideKitchenAnchor("stove", "灶台", "BigStove", "灶台建造", GuideStoveCarrierPrefabPath);
            AddGuideKitchenAnchor("furnace", "炉子", "SmallStove", "炉子建造", GuideFurnaceCarrierPrefabPath);
            AddGuideKitchenAnchor("cabinet_1", "柜子", "柜子", "柜子建造", GuideCabinetCarrierPrefabPath);
            AddGuideKitchenAnchor("cabinet_2", "柜子2", "柜子2", "柜子2建造", GuideCabinetCarrierPrefabPath);
            AddGuideKitchenAnchor("cabinet_3", "酒柜", "酒柜", "酒柜建造", GuideWineCabinetCarrierPrefabPath);
            AddGuideKitchenAnchor("cabinet_4", "水缸堆", "水缸堆", "水缸堆建造", GuideWineCabinet2CarrierPrefabPath);
            AddGuideKitchenAnchor("jiaozi", "轿子", "jiaozi", "轿子建造", string.Empty);
            // 楼梯模型 louti（LoutiCustom/_Alpha）+ 建造底板「楼梯建造」。
            AddGuideKitchenAnchor("stairs", "楼梯", "louti", "楼梯建造", string.Empty);
            AddGuideKitchenAnchor("kitchen_table_1", "厨房桌子1", "厨房桌子1", "厨房桌子1建造", GuideKitchenTable1CarrierPrefabPath);
            AddGuideKitchenAnchor("kitchen_table_2", "厨房桌子2", "厨房桌子2", "厨房桌子2建造", GuideKitchenTable2CarrierPrefabPath);
            AddGuideSceneObject(guideStoveSceneObjects, "BigStove");
            guideSteamerObject = FindGuideSceneObject("Steamer_1") ?? FindGuideSceneObject("Steamer") ?? FindGuideTargetObject("Steamer_1") ?? FindGuideTargetObject("Steamer");

            guideStoveObject = guideStoveSceneObjects.Count > 0
                ? guideStoveSceneObjects[0]
                : FindGuideTargetObject("BigStove")
                  ?? FindGuideTargetObject("P_Equipment_Stove")
                  ?? FindGuideTargetObject("Stove01_P")
                  ?? FindGuideTargetObject("SmallStove")
                  ?? FindGuideTargetObject("Wok")
                  ?? FindGuideTargetObject("Steamer");
            guideStoveBuildBase = guideKitchenAnchors.Count > 0 ? guideKitchenAnchors[0].buildBase : FindGuideSceneObject("灶台建造") ?? FindGuideTargetObject("灶台建造");

            CacheInteriorBarrierSceneObjects();
        }

        #endregion

        #region Business And Guide State

        /// <summary>
        /// 响应酒楼营业状态变化并启动或停止顾客流程。
        /// </summary>
        /// <param name="is打开">参数值。</param>
        private void HandleBusinessStateChanged(bool isOpen)
        {
            if (isOpen)
            {
                if (!hasNavMesh)
                {
                    hasNavMesh = TryGetNavMeshPosition(customerEntryPoint != null ? customerEntryPoint.position : Vector3.zero, out _);
                }

                if (!hasNavMesh)
                {
                    Debug.LogWarning("[TavernSceneManager] 当前场景没有可用的 NavMesh，已跳过顾客生成。");
                }
                else
                {
                    StartBusinessLoop();
                }

                RefreshCounterRandomReward();
            }
            else
            {
                StopBusinessLoop();
                ResetCounterRandomReward();
            }

            RefreshGuideWorldState();
            RefreshAllTableRuntimeState();
            RefreshBackgroundCrowdVolume();
        }

        /// <summary>
        /// 刷新引导物件、员工展示和世界按钮显隐（供升星过场等外部回调）。
        /// </summary>
        internal void RefreshGuideWorldState()
        {
            var guideService = TavernGuideService.Instance;
            var worldPresentation = GuidePresentationAdapter.BuildWorldPresentation(guideService);
            var guide = DataManager.Instance.GameplayGuideData;
            var isBusinessOpen = DataManager.Instance.TavernData.isOpen;
            EnsureGuideWorldButtons();

            // 柜台：未购显示半透预览与建造入口；已购显示建成态。
            var counterPurchased = guide != null && guide.purchasedCounter;
            var showCounterBuild = DataManager.Instance != null
                                   && DataManager.Instance.ShouldShowGuideBasicEquipmentPurchase("counter")
                                   && !guideCounterDeliveryPending
                                   && (DataManager.Instance == null || !DataManager.Instance.IsVisitingOtherTavern);
            if (guideCounterBuildBase != null)
            {
                guideCounterBuildBase.SetActive(showCounterBuild);
            }

            if (guideCounterObject != null)
            {
                var showCounterModel = (counterPurchased || showCounterBuild) && !guideCounterDeliveryPending;
                guideCounterObject.SetActive(showCounterModel);
                if (showCounterModel)
                {
                    if (counterPurchased)
                    {
                        FacilityBuildVisualUtility.ApplyBuiltState(guideCounterObject);
                    }
                    else
                    {
                        FacilityBuildVisualUtility.ApplyPreviewState(guideCounterObject);
                    }
                }
            }

            foreach (var kitchenAnchor in guideKitchenAnchors)
            {
                // 厨房桌子1/2：保持隐藏（模型与建造底板），不影响其它设施装饰。
                if (kitchenAnchor.itemKey == "kitchen_table_1" || kitchenAnchor.itemKey == "kitchen_table_2")
                {
                    if (kitchenAnchor.buildBase != null)
                    {
                        kitchenAnchor.buildBase.SetActive(false);
                    }

                    if (kitchenAnchor.sceneObject != null)
                    {
                        kitchenAnchor.sceneObject.SetActive(false);
                    }

                    continue;
                }

                var isPending = guidePendingKitchenItems.Contains(kitchenAnchor.itemKey)
                                || (kitchenAnchor.itemKey == "stove" && guideStoveDeliveryPending);
                var isPurchased = DataManager.Instance.IsGuideKitchenItemPurchased(kitchenAnchor.itemKey);
                var showBuildBase = ShouldShowGuideKitchenButton(kitchenAnchor.itemKey)
                                    && (DataManager.Instance == null || !DataManager.Instance.IsVisitingOtherTavern);
                // 首次开业后营业中也可显示未购设施预览（与随时可购买一致）。
                var canShowBuild = DataManager.Instance != null
                                   && DataManager.Instance.AllowsFacilityPurchaseNow()
                                   && showBuildBase
                                   && !isPending;
                if (kitchenAnchor.buildBase != null)
                {
                    kitchenAnchor.buildBase.SetActive(canShowBuild);
                }

                if (kitchenAnchor.sceneObject != null)
                {
                    // 已购常驻显示；未购则在允许购买时半透预览（含营业中）。
                    var showKitchenModel = (isPurchased && !isPending) || canShowBuild;
                    kitchenAnchor.sceneObject.SetActive(showKitchenModel);
                    if (showKitchenModel)
                    {
                        var includeChildren = kitchenAnchor.itemKey != "jiaozi";
                        if (isPurchased && !isPending)
                        {
                            FacilityBuildVisualUtility.ApplyBuiltState(kitchenAnchor.sceneObject, includeChildren);
                        }
                        else
                        {
                            FacilityBuildVisualUtility.ApplyPreviewState(kitchenAnchor.sceneObject, includeChildren);
                        }
                    }
                }
            }

            // 酒柜/柜子碰撞体依赖模型显隐，须在预览激活后再刷新。
            EnsureGuideBuildBaseColliders();

            if (foodTableObject != null)
            {
                var showFoodTable = DataManager.Instance.IsGuideKitchenItemPurchased("stove")
                                    && !guidePendingKitchenItems.Contains("stove")
                                    && !guideStoveDeliveryPending;
                foodTableObject.SetActive(showFoodTable);
                if (showFoodTable)
                {
                    FacilityBuildVisualUtility.ApplyBuiltState(foodTableObject);
                }
                else
                {
                    ClearPreparedDishesForBusinessEnd();
                }
            }

            if (guideSteamerObject != null)
            {
                var furnaceReady = DataManager.Instance.IsGuideKitchenItemPurchased("furnace")
                                   && !guidePendingKitchenItems.Contains("furnace");
                guideSteamerObject.SetActive(furnaceReady);
                if (furnaceReady)
                {
                    FacilityBuildVisualUtility.ApplyBuiltState(guideSteamerObject);
                }
            }

            // 桌子1/2 已整体隐藏，不再按名字全局关摆件，避免误伤其它设施装饰。
            RefreshKitchenTableLinkedProps("kitchen_table_1");
            RefreshKitchenTableLinkedProps("kitchen_table_2");
            ForceApplyPurchasedKitchenBuildVisuals();
            RefreshGuideRecruitBases(guide, isBusinessOpen, worldPresentation);

            HidePreRecruitSceneStaffModels();
            var chefIds = DataManager.Instance.GetOwnedStaffIdsByPosition(StaffPosition.Chef);
            var waiterIds = DataManager.Instance.GetOwnedStaffIdsByPosition(StaffPosition.Waiter, includeTemporary: true);
            var chefCount = chefIds.Count;
            var waiterCount = waiterIds.Count;
            // 员工 ID 走代码常量，不读引导任务表。
            const int defaultChefStaffId = 4;
            const int defaultWaiterStaffId = 5;
            const int defaultShopkeeperStaffId = 1;
            var chefStaffId = chefCount > 0 ? chefIds[0] : defaultChefStaffId;
            var waiterStaffId = waiterCount > 0 ? waiterIds[0] : defaultWaiterStaffId;
            var shopkeeperStaffId = defaultShopkeeperStaffId;
            EnsureGuideStaffVisualCount(GuideChefVisualKey, StaffRole.Chef, chefIds);
            EnsureGuideStaffVisualCount(GuideWaiterVisualKey, StaffRole.Waiter, waiterIds);
            RefreshGuideStoveFireEffects(chefCount);

            // 营业中也必须刷新员工站位/缩放。
            // 进店自动恢复开业时若 early return，会导致：厨师停在预制体默认姿态、前台 GuideVisual 根本不创建。
            RefreshGuideStaffVisualAtSceneMarker(
                GuideShopkeeperVisualKey,
                StaffRole.Waiter,
                guide.hiredShopkeeper,
                GuideShopkeeperMarkerName,
                "WaiterF1",
                guideCounterObject,
                new Vector3(0.06f, -0.27f, -0.4f),
                shopkeeperStaffId,
                180f);
            RefreshGuideStaffVisualAtSceneMarker(
                GuideChefVisualKey,
                StaffRole.Chef,
                chefCount > 0,
                GuideChefMarkerName,
                "Chef3",
                guideStoveObject,
                new Vector3(0.7f, 0f, 0.6f),
                chefStaffId);
            RefreshGuideStaffVisualAtSceneMarker(
                GuideWaiterVisualKey,
                StaffRole.Waiter,
                waiterCount > 0,
                GuideWaiterMarkerName,
                "WaiterF1_1",
                guideCounterObject,
                new Vector3(6f, -0.27f, 2.37f),
                waiterStaffId,
                97.5f);
            LayoutAdditionalGuideStaffVisuals(
                GuideChefVisualKey,
                GuideChefMarkerName,
                "Chef3",
                guideStoveObject != null ? guideStoveObject.transform : null,
                new Vector3(0.7f, 0f, 0.6f),
                0f);
            LayoutAdditionalGuideStaffVisuals(
                GuideWaiterVisualKey,
                GuideWaiterMarkerName,
                "WaiterF1_1",
                guideCounterObject != null ? guideCounterObject.transform : null,
                new Vector3(6f, -0.27f, 2.37f),
                97.5f);

            RefreshGuideWorldButtons(guide);

            if (guideCounterButton != null && guideCounterButton.rectTransform != null && guideCounterButton.rectTransform.gameObject.activeSelf)
            {
                SetGuideButtonText(guideCounterButton, $"{GetGuideFacilityCostByKey("counter")}");
            }

            foreach (var kitchenAnchor in guideKitchenAnchors)
            {
                if (kitchenAnchor.button != null && kitchenAnchor.button.rectTransform != null && kitchenAnchor.button.rectTransform.gameObject.activeSelf)
                {
                    SetGuideButtonText(kitchenAnchor.button, $"{GetGuideFacilityCostByKey(kitchenAnchor.itemKey)}");
                }
            }

            RefreshInteriorBarrierState();
            RefreshInteriorExpandHud();
            RefreshWallLevelMaterials();
            // 轿子显隐/半透与拜访拉客共用同一节点，引导刷新后需再对齐一次。
            DataManager.Instance?.TryGrantJiaoziUnlockedByProgress(dispatchSignals: false);
            RefreshVisitJiaoziVisibility();
            RefreshUpStairButton();
            RefreshMyDrumUpButton();
            SyncVipPrivateRoomBubblesWithSecondFloor();
        }

        /// <summary>
        /// 声望变化：仅升星时做完整世界刷新；结账等「只涨声望」不再 RefreshGuideWorldState，
        /// 否则会把小二瞬移回雇佣默认站位。
        /// </summary>
        private void HandleTavernPrestigeChanged()
        {
            var level = DataManager.Instance != null ? DataManager.Instance.GetTavernLevel() : 1;
            level = Mathf.Max(1, level);
            if (lastHandledTavernLevel < 0)
            {
                lastHandledTavernLevel = level;
                RefreshGuideWorldState();
                ApplySavedTableStates();
                RefreshUpStairButton();
                RefreshMyDrumUpButton();
                return;
            }

            if (level > lastHandledTavernLevel)
            {
                lastHandledTavernLevel = level;
                RefreshGuideWorldState();
                ApplySavedTableStates();
                RefreshUpStairButton();
                RefreshMyDrumUpButton();
                TryTriggerPeakWaveAfterTavernUpgrade();
                return;
            }

            lastHandledTavernLevel = level;
            RefreshUpStairButton();
            RefreshMyDrumUpButton();
        }

        /// <summary>
        /// 按当前酒楼等级替换场景中所有 wallLv* 材质（如 wallLv1 → wallLv2）。
        /// </summary>
        private void RefreshWallLevelMaterials()
        {
            if (DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern)
            {
                return;
            }

            var level = DataManager.Instance != null ? DataManager.Instance.GetTavernLevel() : 1;
            level = Mathf.Clamp(level <= 0 ? 1 : level, 1, DataManager.MaxTavernLevel);
            var targetMaterial = LoadWallLevelMaterial(level);
            if (targetMaterial == null)
            {
                Debug.LogWarning($"[TavernSceneManager] 未找到墙体材质 wallLv{level}，无法按酒楼等级替换。");
                return;
            }

            var renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null)
                {
                    continue;
                }

                var sharedMaterials = renderer.sharedMaterials;
                if (sharedMaterials == null || sharedMaterials.Length == 0)
                {
                    continue;
                }

                var changed = false;
                for (var i = 0; i < sharedMaterials.Length; i++)
                {
                    if (!IsWallLevelMaterial(sharedMaterials[i]))
                    {
                        continue;
                    }

                    if (sharedMaterials[i] == targetMaterial)
                    {
                        continue;
                    }

                    sharedMaterials[i] = targetMaterial;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = sharedMaterials;
                }
            }
        }

        private static Material LoadWallLevelMaterial(int level)
        {
            var path = string.Format(WallLevelMaterialPathFormat, level);
            return GameplayResourceStore.LoadAsset<Material>(path);
        }

        private static bool IsWallLevelMaterial(Material material)
        {
            if (material == null)
            {
                return false;
            }

            var name = material.name;
            const string instanceSuffix = " (Instance)";
            if (name.EndsWith(instanceSuffix, StringComparison.Ordinal))
            {
                name = name[..^instanceSuffix.Length];
            }

            return name.StartsWith(WallLevelMaterialNamePrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// 按当前员工数量重新排布额外招聘出来的厨师和小二。
        /// </summary>
        /// <param name="visualKey">员工表现键。</param>
        /// <param name="markerName">主标记点名称。</param>
        /// <param name="legacyMarkerName">兼容旧标记点名称。</param>
        /// <param name="fallbackAnchor">备用锚点。</param>
        /// <param name="fallbackOffset">备用偏移。</param>
        /// <param name="fallbackYawDegrees">备用额外朝向。</param>
        private void LayoutAdditionalGuideStaffVisuals(string visualKey, string markerName, string legacyMarkerName, Transform fallbackAnchor, Vector3 fallbackOffset, float fallbackYawDegrees)
        {
            var visuals = GetGuideStaffVisuals(visualKey);
            if (visuals == null || visuals.Length <= 1)
            {
                return;
            }

            var marker = FindSceneTransformByName(markerName) ?? FindSceneTransformByName(legacyMarkerName);
            for (var index = 1; index < visuals.Length; index++)
            {
                var visual = visuals[index];
                if (visual == null)
                {
                    continue;
                }

                // 正在播放入场动画的员工不要被位置同步覆盖，否则会闪回到锚点。
                if (staffVisualsBeingAnimated.Contains(visual))
                {
                    continue;
                }

                // 营业中小二/厨师已在场景活动：不强制拉回默认站位。
                if (ShouldPreserveGuideStaffRuntimeWorldPose(visualKey, visual))
                {
                    continue;
                }

                if (marker != null)
                {
                    visual.transform.position = ResolveGuideStaffMarkerPosition(visualKey, marker, index);
                    if (visualKey == GuideChefVisualKey)
                    {
                        visual.transform.rotation = ResolveGuideChefHomeRotation();
                    }
                    else if (visualKey == GuideShopkeeperVisualKey)
                    {
                        visual.transform.rotation = ResolveGuideShopkeeperHomeRotation();
                    }
                    else if (visualKey == GuideWaiterVisualKey
                             && TryResolveGuideWaiterHomePose(index, out _, out var waiterRot, out _))
                    {
                        visual.transform.rotation = waiterRot;
                    }
                    else
                    {
                        visual.transform.rotation = marker.rotation;
                    }

                    var scaleSource = marker;
                    if (visualKey == GuideWaiterVisualKey)
                    {
                        // 缩放用原小二站位标记，不跟雇佣挂点。
                        scaleSource = FindSceneTransformByName(GuideWaiterMarkerName)
                                      ?? FindSceneTransformByName("WaiterF1_1")
                                      ?? marker;
                    }
                    else if (visualKey == GuideChefVisualKey)
                    {
                        scaleSource = FindGuideChefHomeMarker() ?? marker;
                    }

                    visual.transform.localScale = ResolveGuideStaffVisualScale(visualKey, scaleSource.lossyScale);
                    continue;
                }

                if (visualKey == GuideChefVisualKey)
                {
                    if (TryResolveGuideChefHomePose(index, out var chefHome, out var chefRot, out var chefScale))
                    {
                        visual.transform.SetPositionAndRotation(chefHome, chefRot);
                        visual.transform.localScale = ResolveGuideStaffVisualScale(visualKey, chefScale);
                    }

                    continue;
                }

                if (visualKey == GuideShopkeeperVisualKey)
                {
                    visual.transform.position = ResolveGuideShopkeeperHomePosition();
                    visual.transform.rotation = ResolveGuideShopkeeperHomeRotation();
                    visual.transform.localScale = ResolveGuideStaffVisualScale(
                        visualKey,
                        ResolveGuideShopkeeperHomeScale());
                    continue;
                }

                if (visualKey == GuideWaiterVisualKey
                    && TryResolveGuideWaiterHomePose(index, out var waiterHome, out var waiterHomeRot, out var waiterScale))
                {
                    visual.transform.SetPositionAndRotation(waiterHome, waiterHomeRot);
                    visual.transform.localScale = ResolveGuideStaffVisualScale(visualKey, waiterScale);
                    continue;
                }

                if (fallbackAnchor != null)
                {
                    var stackOffset = GetGuideStaffStackOffset(visualKey, index);
                    UpdateGuideStaffTransform(visual.transform, fallbackAnchor, fallbackOffset + stackOffset, fallbackYawDegrees);
                }
            }
        }

        /// <summary>
        /// 懒加载灶台下的火焰特效节点。
        /// </summary>
        private void EnsureGuideStoveFireObjectsResolved()
        {
            if (guideStoveObject == null)
            {
                return;
            }

            for (var index = 0; index < GuideStoveFireObjectNames.Length; index++)
            {
                if (guideStoveFireObjects[index] != null)
                {
                    continue;
                }

                guideStoveFireObjects[index] = FindChildGameObjectByName(
                    guideStoveObject.transform,
                    GuideStoveFireObjectNames[index]);
            }
        }

        /// <summary>
        /// 按当前厨师数量显示灶台火焰，默认全隐藏，最多 3 个。
        /// </summary>
        /// <param name="chefCount">已招聘厨师数量。</param>
        private void RefreshGuideStoveFireEffects(int chefCount)
        {
            EnsureGuideStoveFireObjectsResolved();
            var activeCount = Mathf.Clamp(chefCount, 0, guideStoveFireObjects.Length);
            for (var index = 0; index < guideStoveFireObjects.Length; index++)
            {
                var fireObject = guideStoveFireObjects[index];
                if (fireObject != null)
                {
                    fireObject.SetActive(index < activeCount);
                }
            }
        }

        /// <summary>
        /// 按厨房桌子的购买状态刷新附属摆件显隐与材质。
        /// 厨房桌子1/2 已整体隐藏，跳过附属摆件逻辑，避免按名字误关场景其它装饰。
        /// </summary>
        private void RefreshKitchenTableLinkedProps(string itemKey, params string[] sceneObjectNames)
        {
            if (itemKey == "kitchen_table_1" || itemKey == "kitchen_table_2")
            {
                return;
            }

            if (sceneObjectNames == null || sceneObjectNames.Length == 0)
            {
                return;
            }

            var isPending = guidePendingKitchenItems.Contains(itemKey);
            var isPurchased = DataManager.Instance.IsGuideKitchenItemPurchased(itemKey);
            var isVisible = isPurchased && !isPending;
            for (var index = 0; index < sceneObjectNames.Length; index++)
            {
                var target = FindGuideSceneObject(sceneObjectNames[index]) ?? FindGuideTargetObject(sceneObjectNames[index]);
                if (target == null)
                {
                    continue;
                }

                var isChildOfKitchenTable = IsKitchenTableChild(itemKey, target.transform);
                if (!isChildOfKitchenTable)
                {
                    target.SetActive(isVisible);
                }

                if (!isVisible && !isChildOfKitchenTable)
                {
                    continue;
                }

                if (isPurchased && !isPending)
                {
                    FacilityBuildVisualUtility.ApplyBuiltState(target);
                }
                else if (target.activeInHierarchy || isChildOfKitchenTable)
                {
                    FacilityBuildVisualUtility.ApplyPreviewState(target);
                }
            }
        }

        /// <summary>
        /// 已购买且已落位的厨房设施（含子节点）强制套用建成态。开店后仍会执行。
        /// </summary>
        private void ForceApplyPurchasedKitchenBuildVisuals()
        {
            for (var i = 0; i < guideKitchenAnchors.Count; i++)
            {
                var kitchenAnchor = guideKitchenAnchors[i];
                if (kitchenAnchor == null || kitchenAnchor.sceneObject == null)
                {
                    continue;
                }

                if (kitchenAnchor.itemKey == "kitchen_table_1" || kitchenAnchor.itemKey == "kitchen_table_2")
                {
                    continue;
                }

                var isPending = guidePendingKitchenItems.Contains(kitchenAnchor.itemKey)
                                || (kitchenAnchor.itemKey == "stove" && guideStoveDeliveryPending);
                if (isPending || !DataManager.Instance.IsGuideKitchenItemPurchased(kitchenAnchor.itemKey))
                {
                    continue;
                }

                // 已建成设施开店后保持显示。
                if (!kitchenAnchor.sceneObject.activeSelf)
                {
                    kitchenAnchor.sceneObject.SetActive(true);
                }

                FacilityBuildVisualUtility.ApplyBuiltState(
                    kitchenAnchor.sceneObject,
                    includeChildren: kitchenAnchor.itemKey != "jiaozi");
            }

            if (guideCounterObject != null
                && DataManager.Instance != null
                && DataManager.Instance.GameplayGuideData != null
                && DataManager.Instance.GameplayGuideData.purchasedCounter
                && !guideCounterDeliveryPending)
            {
                if (!guideCounterObject.activeSelf)
                {
                    guideCounterObject.SetActive(true);
                }

                FacilityBuildVisualUtility.ApplyBuiltState(guideCounterObject);
            }

            if (foodTableObject != null
                && DataManager.Instance.IsGuideKitchenItemPurchased("stove")
                && !guidePendingKitchenItems.Contains("stove")
                && !guideStoveDeliveryPending)
            {
                if (!foodTableObject.activeSelf)
                {
                    foodTableObject.SetActive(true);
                }

                FacilityBuildVisualUtility.ApplyBuiltState(foodTableObject);
            }

            if (guideSteamerObject != null
                && DataManager.Instance.IsGuideKitchenItemPurchased("furnace")
                && !guidePendingKitchenItems.Contains("furnace"))
            {
                if (!guideSteamerObject.activeSelf)
                {
                    guideSteamerObject.SetActive(true);
                }

                FacilityBuildVisualUtility.ApplyBuiltState(guideSteamerObject);
            }
        }

        /// <summary>
        /// 判断物件是否挂在指定厨房桌子场景节点下。
        /// </summary>
        private bool IsKitchenTableChild(string itemKey, Transform target)
        {
            if (target == null || string.IsNullOrEmpty(itemKey))
            {
                return false;
            }

            for (var i = 0; i < guideKitchenAnchors.Count; i++)
            {
                var anchor = guideKitchenAnchors[i];
                if (anchor == null || anchor.itemKey != itemKey || anchor.sceneObject == null)
                {
                    continue;
                }

                return target.IsChildOf(anchor.sceneObject.transform);
            }

            return false;
        }

        #endregion

        #region Interior Barrier

        private const string InteriorWallNodeName = "wall01";
        private const string InteriorExpandAnchorName = "扩建";
        private const float InteriorLv1CameraMinX = -3f;
        private const float InteriorLv2CameraMinX = -5.3f;
        private const float GuideFirstEnterCameraX = -1f;
        private const float HireStaffEnterCameraX = -0.9f;
        private const float InteriorWallExpandTargetX = -2.1f;
        private const float InteriorWallExpandTargetY = -1f;
        private const float InteriorWallExpandAnimDuration = 1f;
        /// <summary>
        /// 扩建 HUD 世界偏移。挂点会同步到 wall01 位置，墙身中心偏高，用负 Y 压低按钮。
        /// 不改 TableAreaUI 预制体本地坐标。
        /// </summary>
        private static readonly Vector3 InteriorExpandHudWorldOffset = new(0f, 0f, 0f);

        private GameObject interiorWallNode;
        private Transform interiorExpandAnchor;
        private bool interiorWallExpandAnimating;
        private Coroutine interiorWallExpandRoutine;

        /// <summary>
        /// 缓存左侧阻挡墙 wall01。
        /// </summary>
        private void CacheInteriorBarrierSceneObjects()
        {
            interiorWallNode ??= FindGuideSceneObject(InteriorWallNodeName)
                                 ?? FindGuideTargetObject(InteriorWallNodeName)
                                 ?? FindSceneGameObjectByName(InteriorWallNodeName);
        }

        /// <summary>
        /// 付费扩建完成后隐藏 wall01，并放宽相机 MinX。
        /// </summary>
        private void RefreshInteriorBarrierState()
        {
            CacheInteriorBarrierSceneObjects();
            ApplyInteriorCameraMinX();

            var expanded = DataManager.Instance != null && DataManager.Instance.IsInteriorWallExpanded();
            var showWall = !expanded || interiorWallExpandAnimating;
            if (interiorWallNode != null)
            {
                interiorWallNode.SetActive(showWall);
            }
        }

        /// <summary>
        /// 二星及以上且未扩建时，在 Objects/扩建 挂点显示扩建按钮。
        /// </summary>
        private void RefreshInteriorExpandHud()
        {
            var dataManager = DataManager.Instance;
            var shouldShow = dataManager != null && dataManager.ShouldShowInteriorWallExpandButton();
            if (!shouldShow || interiorWallExpandAnimating)
            {
                HudOverlayService.UnregisterInteriorWallExpandHud();
                return;
            }

            var anchor = ResolveInteriorExpandAnchor();
            if (anchor == null)
            {
                HudOverlayService.UnregisterInteriorWallExpandHud();
                return;
            }

            // 挂点用场景「扩建」节点自身坐标，不再每帧对齐 wall01（否则场景改 X/Y/Z 无效）。
            var cost = TbConfigRuntime.GetTavernExpandCost();
            var expandUi = HudOverlayService.RegisterInteriorWallExpandHud(anchor, cost, OnClickInteriorWallExpand);
            expandUi?.SetWorldOffset(InteriorExpandHudWorldOffset);
        }

        /// <summary>
        /// 仅在运行时新建挂点时，用 wall01 给一个初始落点；已有场景节点不改写。
        /// </summary>
        private void SeedInteriorExpandAnchorFromWallIfNeeded()
        {
            if (interiorExpandAnchor == null)
            {
                return;
            }

            CacheInteriorBarrierSceneObjects();
            if (interiorWallNode == null)
            {
                return;
            }

            interiorExpandAnchor.position = interiorWallNode.transform.position;
        }

        private Transform ResolveInteriorExpandAnchor()
        {
            var objectsRoot = sceneObjectsRoot != null
                ? sceneObjectsRoot
                : FindSceneTransformByName("Objects");
            if (interiorExpandAnchor != null)
            {
                if (interiorExpandAnchor.parent != objectsRoot && objectsRoot != null)
                {
                    interiorExpandAnchor.SetParent(objectsRoot, true);
                }

                return interiorExpandAnchor;
            }

            if (objectsRoot != null)
            {
                var child = objectsRoot.Find(InteriorExpandAnchorName);
                if (child != null)
                {
                    interiorExpandAnchor = child;
                    return interiorExpandAnchor;
                }
            }

            var direct = FindSceneTransformByName(InteriorExpandAnchorName);
            if (direct != null)
            {
                interiorExpandAnchor = direct;
                if (objectsRoot != null && interiorExpandAnchor.parent != objectsRoot)
                {
                    interiorExpandAnchor.SetParent(objectsRoot, true);
                }

                return interiorExpandAnchor;
            }

            if (objectsRoot == null)
            {
                return null;
            }

            CacheInteriorBarrierSceneObjects();
            var anchorObject = new GameObject(InteriorExpandAnchorName);
            interiorExpandAnchor = anchorObject.transform;
            interiorExpandAnchor.SetParent(objectsRoot, false);
            SeedInteriorExpandAnchorFromWallIfNeeded();

            return interiorExpandAnchor;
        }

        private void OnClickInteriorWallExpand()
        {
            if (interiorWallExpandAnimating)
            {
                return;
            }

            var dataManager = DataManager.Instance;
            if (dataManager == null)
            {
                return;
            }

            if (!dataManager.TryPurchaseInteriorWallExpand(out var message))
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    HudOverlayService.ShowFloatingWarning(message);
                }

                return;
            }

            GameAudioManager.PlayFacilityPurchaseSuccess();
            HudOverlayService.UnregisterInteriorWallExpandHud();
            ApplyInteriorCameraMinX();
            if (interiorWallExpandRoutine != null)
            {
                StopCoroutine(interiorWallExpandRoutine);
            }

            interiorWallExpandRoutine = StartCoroutine(PlayInteriorWallExpandAnimation());
        }

        private IEnumerator PlayInteriorWallExpandAnimation()
        {
            interiorWallExpandAnimating = true;
            CacheInteriorBarrierSceneObjects();
            if (interiorWallNode == null)
            {
                interiorWallExpandAnimating = false;
                interiorWallExpandRoutine = null;
                RefreshInteriorBarrierState();
                yield break;
            }

            interiorWallNode.SetActive(true);
            var wallTransform = interiorWallNode.transform;
            var startPos = wallTransform.localPosition;
            // 终点：本地 X 仍滑到扩建位，Y 落到 -1，Z 保持。
            var endPos = new Vector3(InteriorWallExpandTargetX, InteriorWallExpandTargetY, startPos.z);
            var elapsed = 0f;
            while (elapsed < InteriorWallExpandAnimDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / InteriorWallExpandAnimDuration);
                wallTransform.localPosition = Vector3.Lerp(startPos, endPos, t);
                yield return null;
            }

            wallTransform.localPosition = endPos;
            interiorWallNode.SetActive(false);
            interiorWallExpandAnimating = false;
            interiorWallExpandRoutine = null;
            RefreshInteriorBarrierState();
            ApplySavedTableStates();
        }

        /// <summary>
        /// 按墙体扩建状态设置 CameraController.MinX（未扩建 -3，已扩建 -5.3）。
        /// </summary>
        private void ApplyInteriorCameraMinX()
        {
            if (CameraController.Instance == null)
            {
                return;
            }

            var expanded = DataManager.Instance != null && DataManager.Instance.IsInteriorWallExpanded();
            CameraController.Instance.SetMinX(expanded ? InteriorLv2CameraMinX : InteriorLv1CameraMinX);
        }

        /// <summary>
        /// 新手引导首次进自家店：相机世界 X 落到 -1（Y/Z、拖拽范围不变）。
        /// </summary>
        private void ApplyGuideFirstEnterCameraX()
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null || dataManager.IsVisitingOtherTavern)
            {
                return;
            }

            var guide = dataManager.GameplayGuideData;
            if (guide == null
                || guide.dialogFirstEnterShown
                || dataManager.GetBusinessOpenCount() > 0)
            {
                return;
            }

            CameraController.Instance?.SetWorldX(GuideFirstEnterCameraX);
        }

        /// <summary>
        /// HireStaff_enter 对话结束后：相机世界 X 落到 -0.9（Y/Z、拖拽范围不变）。
        /// </summary>
        public static void ApplyHireStaffEnterCameraX()
        {
            if (DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern)
            {
                return;
            }

            CameraController.Instance?.SetWorldX(HireStaffEnterCameraX);
        }

        private static readonly Vector3 UpStairButtonWorldOffset = new(0f, 0.25f, 0f);

        /// <summary>
        /// 自家已购楼梯显示上楼按钮（挂点「楼梯建造」）；拜访 / 二楼隐藏。
        /// </summary>
        private void RefreshUpStairButton()
        {
            var dataManager = DataManager.Instance;
            var shouldShow = dataManager != null
                             && !dataManager.IsVisitingOtherTavern
                             && dataManager.IsStairsUnlocked()
                             && !SceneFlowCoordinator.IsOnTavernSecondFloor();

            if (!shouldShow)
            {
                ClearUpStairButton();
                return;
            }

            var anchor = ResolveStairsBuildAnchor();
            if (anchor == null)
            {
                ClearUpStairButton();
                return;
            }

            if (upStairButtonRoot != null)
            {
                return;
            }

            upStairButtonRoot = HudOverlayService.ShowUpStairButton(
                anchor,
                UpStairButtonWorldOffset,
                OnClickUpStairButton);
        }

        private Transform ResolveStairsBuildAnchor()
        {
            var direct = FindSceneTransformByName("楼梯建造");
            if (direct != null)
            {
                return direct;
            }

            var objectsRoot = FindSceneTransformByName("Objects");
            return objectsRoot != null ? objectsRoot.Find("楼梯建造") : null;
        }

        private void ClearUpStairButton()
        {
            if (upStairButtonRoot == null)
            {
                return;
            }

            HudOverlayService.ReleaseWorldHudItem(upStairButtonRoot);
            upStairButtonRoot = null;
        }

        private void OnClickUpStairButton()
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null || dataManager.IsVisitingOtherTavern || dataManager.GetTavernLevel() < 3)
            {
                return;
            }

            // 上楼按钮在成功进入二楼流程时再销毁，点击未通过校验时保留。
            StartCoroutine(SceneFlowCoordinator.EnterTavernSecondFloor());
        }

        private static readonly Vector3 MyDrumUpButtonWorldOffset = new(0f, 0.35f, 0f);

        /// <summary>
        /// 自家已购轿子显示拉客按钮（挂点「轿子建造」）；拜访 / 二楼 / 卸客中隐藏。
        /// </summary>
        private void RefreshMyDrumUpButton()
        {
            var dataManager = DataManager.Instance;
            var unloading = homeUnloadPhase != HomeUnloadPhase.None
                            || IsUnloadingPulledCustomersAtHome();
            var shouldShow = dataManager != null
                             && !dataManager.IsVisitingOtherTavern
                             && dataManager.IsJiaoziUnlocked()
                             && !SceneFlowCoordinator.IsOnTavernSecondFloor()
                             && !unloading;

            if (!shouldShow)
            {
                ClearMyDrumUpButton();
                return;
            }

            var anchor = ResolveJiaoziBuildAnchor();
            if (anchor == null)
            {
                ClearMyDrumUpButton();
                return;
            }

            if (myDrumUpButtonRoot != null)
            {
                return;
            }

            myDrumUpButtonRoot = HudOverlayService.ShowMyDrumUpButton(
                anchor,
                MyDrumUpButtonWorldOffset,
                HudOverlayService.HandleOwnTavernDrumUpClick);
        }

        private Transform ResolveJiaoziBuildAnchor()
        {
            var direct = FindSceneTransformByName("轿子建造");
            if (direct != null)
            {
                return direct;
            }

            for (var index = 0; index < guideKitchenAnchors.Count; index++)
            {
                var anchor = guideKitchenAnchors[index];
                if (anchor == null || anchor.itemKey != "jiaozi")
                {
                    continue;
                }

                if (anchor.buildBase != null)
                {
                    return anchor.buildBase.transform;
                }

                if (anchor.sceneObject != null)
                {
                    return anchor.sceneObject.transform;
                }
            }

            var objectsRoot = FindSceneTransformByName("Objects");
            if (objectsRoot != null)
            {
                var underObjects = objectsRoot.Find("轿子建造");
                if (underObjects != null)
                {
                    return underObjects;
                }
            }

            return FindSceneTransformByName("jiaozi");
        }

        private void ClearMyDrumUpButton()
        {
            if (myDrumUpButtonRoot == null)
            {
                return;
            }

            HudOverlayService.ReleaseWorldHudItem(myDrumUpButtonRoot);
            myDrumUpButtonRoot = null;
        }

        #endregion
    }
}
