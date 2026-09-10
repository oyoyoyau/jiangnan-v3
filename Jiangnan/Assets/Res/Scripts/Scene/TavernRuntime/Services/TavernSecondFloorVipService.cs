using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using JN.Client.Manager;
using UnityEngine;
using UnityEngine.AI;

namespace JN.Client.Scene
{
    /// <summary>
    /// 二楼贵客：存档快照（坐下/上菜/已吃/已结账）、落座、挂点解析；
    /// 会话由 <see cref="TavernSecondFloorVipSessionController"/> 驱动。
    /// </summary>
    public static class TavernSecondFloorVipService
    {
        public const string VipChairObjectName = "贵客椅子";
        public const string VipTableObjectName = "贵客桌子";
        public const string ShopInteriorName = "Shop_Interior";
        public const string WaiterPointName = "waiterPoint";
        public const string ChefPointName = "chefPoint";
        public const string BigStoveName = "BigStove";
        /// <summary>二楼小二取餐导航点。</summary>
        public const string FoodPointName = "foodPoint";
        /// <summary>二楼厨师出餐摆放根节点（子节点 Point1~5）。</summary>
        public const string FoodMakePointName = "FoodMakePoint";
        public const int FoodMakePointCount = 5;
        /// <summary>二楼厨房出餐桌：首道成品兜底摆放（Shop_Interior/桌子）。</summary>
        public const string KitchenTableNodeName = "桌子";
        public const string VipEndPointName = "VipEndPoint";
        public const string WaiterCharacterName = "P_Character_Waiter03";
        public const string SecondFloorWaiterVisualName = "Waiter_SecondFloorVip";
        /// <summary>与一楼 Guide 小二相同：SO_Staff Waiter03（staffId=5）。</summary>
        public const string Waiter03StaffId = "5";
        public const string ChefCharacterName = "P_Character_Chef03";
        public const int ProductPlacementCount = 6;

        private const string PlatePrefabPath = "Assets/Res/Resources/Models/Objects/plate/plate_P.prefab";
        private static readonly string[] DishPrefabPaths =
        {
            "Assets/Res/Resources/Models/Objects/vegetable/vegetable01_P.prefab",
            "Assets/Res/Resources/Models/Objects/vegetable/vegetable02_P.prefab",
            "Assets/Res/Resources/Models/Objects/vegetable/vegetable03_P.prefab",
            "Assets/Res/Resources/Models/Objects/vegetable/vegetable04_P.prefab",
        };

        /// <summary>一楼缓存的贵客预制体资源引用（跨场景仍有效）。</summary>
        public static GameObject CachedVipCustomerPrefab { get; private set; }

        private static GameObject spawnedSecondFloorVip;
        private static GameObject platePrefabCache;
        private static readonly List<GameObject> dishPrefabCache = new();

        public static GameObject SpawnedVipRoot => spawnedSecondFloorVip;

        public static void CacheVipPrefab(GameObject prefab)
        {
            if (prefab != null)
            {
                CachedVipCustomerPrefab = prefab;
            }
        }

        public static bool HasSecondFloorVipGuest()
        {
            var dataManager = DataManager.Instance;
            return dataManager != null
                   && dataManager.SaveData?.tavern != null
                   && dataManager.SaveData.tavern.hasSecondFloorVipGuest;
        }

        public static void SetSecondFloorVipGuest(bool present, bool saveImmediately = true)
        {
            var dataManager = DataManager.Instance;
            if (dataManager?.SaveData?.tavern == null)
            {
                return;
            }

            if (dataManager.SaveData.tavern.hasSecondFloorVipGuest == present)
            {
                if (!present)
                {
                    ClearSecondFloorVipSnapshot(saveImmediately: false);
                }

                if (saveImmediately)
                {
                    dataManager.SaveGame();
                }

                return;
            }

            dataManager.SaveData.tavern.hasSecondFloorVipGuest = present;
            if (!present)
            {
                ClearSecondFloorVipSnapshot(saveImmediately: false);
            }

            if (saveImmediately)
            {
                dataManager.SaveGame();
            }
        }

        /// <summary>写入二楼贵客会话快照（坐下/上菜/已吃/已结账/可乐）。</summary>
        public static void WriteSecondFloorVipSnapshot(
            bool seated,
            int servedDishCount,
            int eatenDishCount,
            int checkoutDoneCount,
            bool colaServed = false,
            bool saveImmediately = true)
        {
            var tavern = DataManager.Instance?.SaveData?.tavern;
            if (tavern == null || !tavern.hasSecondFloorVipGuest)
            {
                return;
            }

            tavern.secondFloorVipSeated = seated;
            tavern.secondFloorVipServedDishCount = Mathf.Clamp(servedDishCount, 0, ProductPlacementCount);
            tavern.secondFloorVipEatenDishCount = Mathf.Clamp(eatenDishCount, 0, ProductPlacementCount);
            tavern.secondFloorVipCheckoutDoneCount = Mathf.Clamp(checkoutDoneCount, 0, ProductPlacementCount);
            // 已吃不能超过已上菜；已结账不能超过已吃。
            tavern.secondFloorVipEatenDishCount = Mathf.Min(
                tavern.secondFloorVipEatenDishCount,
                tavern.secondFloorVipServedDishCount);
            tavern.secondFloorVipCheckoutDoneCount = Mathf.Min(
                tavern.secondFloorVipCheckoutDoneCount,
                tavern.secondFloorVipEatenDishCount);
            tavern.secondFloorVipColaServed = colaServed;

            if (saveImmediately)
            {
                DataManager.Instance.SaveGame();
            }
        }

        public static void ClearSecondFloorVipSnapshot(bool saveImmediately = true)
        {
            var tavern = DataManager.Instance?.SaveData?.tavern;
            if (tavern == null)
            {
                return;
            }

            tavern.secondFloorVipSeated = false;
            tavern.secondFloorVipServedDishCount = 0;
            tavern.secondFloorVipEatenDishCount = 0;
            tavern.secondFloorVipCheckoutDoneCount = 0;
            tavern.secondFloorVipColaServed = false;
            if (saveImmediately)
            {
                DataManager.Instance.SaveGame();
            }
        }

        public static bool TryReadSecondFloorVipSnapshot(
            out bool seated,
            out int servedDishCount,
            out int eatenDishCount,
            out int checkoutDoneCount)
        {
            seated = false;
            servedDishCount = 0;
            eatenDishCount = 0;
            checkoutDoneCount = 0;
            var tavern = DataManager.Instance?.SaveData?.tavern;
            if (tavern == null || !tavern.hasSecondFloorVipGuest)
            {
                return false;
            }

            seated = tavern.secondFloorVipSeated;
            servedDishCount = Mathf.Clamp(tavern.secondFloorVipServedDishCount, 0, ProductPlacementCount);
            eatenDishCount = Mathf.Clamp(tavern.secondFloorVipEatenDishCount, 0, servedDishCount);
            checkoutDoneCount = Mathf.Clamp(tavern.secondFloorVipCheckoutDoneCount, 0, eatenDishCount);
            return true;
        }

        /// <summary>
        /// 进入二楼：生成贵客。若存档已坐下则直接落座，否则从 VipEndPoint 走进来。
        /// </summary>
        public static bool TrySpawnSeatedVipOnSecondFloor()
        {
            if (spawnedSecondFloorVip != null)
            {
                return true;
            }

            if (!HasSecondFloorVipGuest())
            {
                return false;
            }

            var prefab = CachedVipCustomerPrefab;
            if (prefab == null)
            {
                Debug.LogWarning("[TavernSecondFloorVip] 无贵客预制体缓存，请先在一楼刷出过贵客再上楼。");
                return false;
            }

            if (!TryResolveVipSeatPose(out var seatPosition, out var seatRotation, out var chair))
            {
                Debug.LogWarning(
                    $"[TavernSecondFloorVip] 未找到落座点「{VipChairObjectName}」或可用桌位，无法生成二楼贵客。");
                return false;
            }

            TryReadSecondFloorVipSnapshot(out var alreadySeated, out _, out _, out _);

            var seatedWorldPosition = SecondFloorVipSeatDriver.SeatedWorldPosition;
            var enterPoint = ResolveNamedTransform(VipEndPointName);
            var spawnPosition = alreadySeated
                ? seatedWorldPosition
                : (enterPoint != null ? enterPoint.position : seatedWorldPosition);
            // 落座朝向不跟椅子；已坐用世界朝前，进场用入口朝向。
            var spawnRotation = alreadySeated
                ? Quaternion.identity
                : (enterPoint != null ? enterPoint.rotation : Quaternion.identity);

            var instance = UnityEngine.Object.Instantiate(prefab, spawnPosition, spawnRotation);
            instance.name = $"{prefab.name}_SecondFloorVip";
            instance.SetActive(true);

            // Destroy 是帧末才生效；必须立刻拆掉运行时顾客逻辑，否则会持续改 Animator/Agent，导致无法坐下。
            StripCustomerRuntimeBehaviours(instance);

            var driver = instance.GetComponent<SecondFloorVipSeatDriver>()
                         ?? instance.AddComponent<SecondFloorVipSeatDriver>();
            if (alreadySeated)
            {
                driver.Bind(chair, seatedWorldPosition, Quaternion.identity);
            }
            else
            {
                driver.BindForEnter(chair, seatedWorldPosition, Quaternion.identity, spawnPosition, spawnRotation);
            }

            spawnedSecondFloorVip = instance;
            return true;
        }

        /// <summary>移除会驱动寻路/动画的顾客运行时组件，避免与二楼落座驱动冲突。</summary>
        private static void StripCustomerRuntimeBehaviours(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var runtimes = root.GetComponentsInChildren<TavernCustomerRuntimeController>(true);
            for (var index = 0; index < runtimes.Length; index++)
            {
                if (runtimes[index] != null)
                {
                    runtimes[index].enabled = false;
                    UnityEngine.Object.DestroyImmediate(runtimes[index]);
                }
            }
        }

        public static void ClearSpawnedVipRuntime()
        {
            if (spawnedSecondFloorVip != null)
            {
                UnityEngine.Object.Destroy(spawnedSecondFloorVip);
                spawnedSecondFloorVip = null;
            }
        }

        public static SecondFloorVipSeatDriver GetVipSeatDriver()
        {
            return spawnedSecondFloorVip != null
                ? spawnedSecondFloorVip.GetComponent<SecondFloorVipSeatDriver>()
                : null;
        }

        public static Transform ResolveNamedTransform(string targetName)
        {
            if (string.IsNullOrWhiteSpace(targetName))
            {
                return null;
            }

            var transforms = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < transforms.Length; index++)
            {
                var current = transforms[index];
                if (current != null && current.name == targetName)
                {
                    return current;
                }
            }

            return null;
        }

        public static GameObject ResolveNamedGameObject(string targetName)
        {
            var transform = ResolveNamedTransform(targetName);
            return transform != null ? transform.gameObject : null;
        }

        /// <summary>
        /// 解析小二取餐点 foodPoint；找不到时回退厨房桌 / BigStove。
        /// </summary>
        public static Transform ResolveFoodPickupPoint()
        {
            return ResolveNamedTransform(FoodPointName)
                   ?? ResolveKitchenDishTable()
                   ?? ResolveNamedTransform(BigStoveName);
        }

        /// <summary>
        /// 解析 Shop_Interior/桌子（二楼出餐桌）；找不到时回退场景同名节点。
        /// </summary>
        public static Transform ResolveKitchenDishTable()
        {
            var shop = ResolveNamedTransform(ShopInteriorName);
            if (shop != null)
            {
                var nested = FindChildRecursive(shop, KitchenTableNodeName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return ResolveNamedTransform(KitchenTableNodeName);
        }

        /// <summary>
        /// 收集 FoodMakePoint/Point1~5；不足时按名称排序兜底子节点。
        /// </summary>
        public static Transform[] CollectFoodMakePoints()
        {
            var result = new Transform[FoodMakePointCount];
            var root = ResolveNamedTransform(FoodMakePointName);
            if (root == null)
            {
                return result;
            }

            var resolvedCount = 0;
            for (var index = 0; index < FoodMakePointCount; index++)
            {
                var pointName = $"Point{index + 1}";
                var point = root.Find(pointName) ?? FindChildRecursive(root, pointName);
                if (point != null)
                {
                    result[index] = point;
                    resolvedCount++;
                }
            }

            if (resolvedCount >= FoodMakePointCount)
            {
                return result;
            }

            var fallbackPoints = new List<Transform>();
            for (var childIndex = 0; childIndex < root.childCount; childIndex++)
            {
                var child = root.GetChild(childIndex);
                if (child != null)
                {
                    fallbackPoints.Add(child);
                }
            }

            fallbackPoints.Sort((left, right) =>
                string.Compare(left.name, right.name, StringComparison.Ordinal));
            for (var index = 0; index < result.Length && index < fallbackPoints.Count; index++)
            {
                result[index] ??= fallbackPoints[index];
            }

            return result;
        }

        /// <summary>dishIndex(0~5) 对应 FoodMakePoint/Point1~5（超出则夹到末点）。</summary>
        public static int ResolveFoodMakePointIndex(int dishIndex)
        {
            return Mathf.Clamp(dishIndex, 0, FoodMakePointCount - 1);
        }

        /// <summary>解析指定连做菜品对应的出餐挂点。</summary>
        public static Transform ResolveFoodMakePoint(int dishIndex)
        {
            var points = CollectFoodMakePoints();
            var pointIndex = ResolveFoodMakePointIndex(dishIndex);
            return pointIndex >= 0 && pointIndex < points.Length ? points[pointIndex] : null;
        }

        /// <summary>
        /// 收集 ProductPlacement_0..5：优先取 TableArea 子节点（二楼已从贵客桌子挪出）。
        /// </summary>
        public static List<Transform> CollectProductPlacements()
        {
            var result = new List<Transform>(ProductPlacementCount);
            var found = new Dictionary<int, Transform>();
            var regex = new Regex(@"^ProductPlacement_(\d+)$", RegexOptions.CultureInvariant);

            // 1) 优先：TableArea 下的挂点（避免吃到贵客桌子奇怪旋转/缩放）
            var tables = UnityEngine.Object.FindObjectsByType<TableArea>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var t = 0; t < tables.Length; t++)
            {
                var table = tables[t];
                if (table == null)
                {
                    continue;
                }

                CollectPlacementsUnder(table.transform, regex, found);
            }

            // 2) 兜底：全场景按名查找
            if (found.Count < ProductPlacementCount)
            {
                var transforms = UnityEngine.Object.FindObjectsByType<Transform>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                for (var index = 0; index < transforms.Length; index++)
                {
                    TryRegisterPlacement(transforms[index], regex, found, onlyIfMissing: true);
                }
            }

            for (var slot = 0; slot < ProductPlacementCount; slot++)
            {
                if (found.TryGetValue(slot, out var placement) && placement != null)
                {
                    result.Add(placement);
                }
            }

            return result;
        }

        private static void CollectPlacementsUnder(
            Transform root,
            Regex regex,
            Dictionary<int, Transform> found)
        {
            if (root == null)
            {
                return;
            }

            TryRegisterPlacement(root, regex, found, onlyIfMissing: false);
            for (var i = 0; i < root.childCount; i++)
            {
                CollectPlacementsUnder(root.GetChild(i), regex, found);
            }
        }

        private static void TryRegisterPlacement(
            Transform current,
            Regex regex,
            Dictionary<int, Transform> found,
            bool onlyIfMissing)
        {
            if (current == null)
            {
                return;
            }

            var match = regex.Match(current.name);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var slotIndex))
            {
                return;
            }

            if (onlyIfMissing && found.ContainsKey(slotIndex))
            {
                return;
            }

            found[slotIndex] = current;
        }

        public static GameObject GetPlatePrefab()
        {
            if (platePrefabCache != null)
            {
                return platePrefabCache;
            }

            platePrefabCache = GameplayResourceStore.LoadAsset<GameObject>(PlatePrefabPath);
            return platePrefabCache;
        }

        public static GameObject GetDishPrefab(int dishIndex)
        {
            EnsureDishPrefabCache();
            if (dishPrefabCache.Count == 0)
            {
                return null;
            }

            var index = Mathf.Abs(dishIndex) % dishPrefabCache.Count;
            return dishPrefabCache[index];
        }

        /// <summary>
        /// 在指定挂点生成盘+菜；dishPrefab 为空时仅空盘。
        /// ProductPlacement 已挂在 TableArea 下：用挂点位置，朝向/世界缩放直立为 1（忽略残留的桌子旋转）。
        /// </summary>
        public static GameObject CreatePlateVisualAt(Transform placement, GameObject dishPrefab)
        {
            if (placement == null)
            {
                return null;
            }

            const float dishOnPlateYOffset = 0.025f;
            var plate = GetPlatePrefab();
            if (plate == null && TavernSceneManager.Instance != null)
            {
                plate = TavernSceneManager.Instance.GetPlatePrefab();
            }

            if (plate == null)
            {
                if (dishPrefab == null)
                {
                    Debug.LogWarning("[TavernSecondFloorVip] 盘/菜预制体均未加载，无法摆盘。");
                    return null;
                }

                var dishOnly = UnityEngine.Object.Instantiate(dishPrefab);
                dishOnly.name = dishPrefab.name;
                PlaceVisualAtAnchor(dishOnly.transform, placement);
                return dishOnly;
            }

            var plateInstance = UnityEngine.Object.Instantiate(plate);
            plateInstance.name = dishPrefab == null ? "EmptyPlate_Runtime" : $"DiningPlate_{dishPrefab.name}";
            PlaceVisualAtAnchor(plateInstance.transform, placement);

            if (dishPrefab == null)
            {
                return plateInstance;
            }

            var dishInstance = UnityEngine.Object.Instantiate(dishPrefab, plateInstance.transform, false);
            dishInstance.name = dishPrefab.name;
            dishInstance.transform.localPosition = Vector3.up * dishOnPlateYOffset;
            dishInstance.transform.localRotation = Quaternion.identity;
            dishInstance.transform.localScale = Vector3.one;
            return plateInstance;
        }

        /// <summary>
        /// 挂到 ProductPlacement：位置跟挂点；旋转强制世界 0（桌子残留 -90°X 会导致菜躺着看不见）；
        /// 世界缩放校正为 1。
        /// </summary>
        private static void PlaceVisualAtAnchor(Transform visual, Transform placement)
        {
            visual.SetParent(placement, false);
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.rotation = Quaternion.identity;

            var parentScale = placement.lossyScale;
            visual.localScale = new Vector3(
                1f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                1f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                1f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
        }

        /// <summary>小二手上挂盘+菜：对齐一楼 Waiter_GuideVisual（挂根节点，不用手骨）。</summary>
        public static GameObject CreateWaiterCarryPlate(GameObject waiter, GameObject dishPrefab)
        {
            if (waiter == null || dishPrefab == null)
            {
                return null;
            }

            var plate = GetPlatePrefab();
            if (plate == null && TavernSceneManager.Instance != null)
            {
                plate = TavernSceneManager.Instance.GetPlatePrefab();
            }

            if (plate == null)
            {
                return null;
            }

            const float dishOnPlateYOffset = 0.025f;
            const float carryPlateScale = 2.5f;
            // 一楼 GuideVisual 强制挂根节点 + 该偏移；挂 Prop_R 会错位。
            var attachPoint = waiter.transform;
            var plateInstance = UnityEngine.Object.Instantiate(plate, attachPoint, false);
            plateInstance.name = "WaiterCarryPlate";
            plateInstance.transform.localPosition = new Vector3(-0.09f, 1f, 0.4f);
            plateInstance.transform.localRotation = Quaternion.identity;
            plateInstance.transform.localScale = Vector3.one * carryPlateScale;

            var dishInstance = UnityEngine.Object.Instantiate(dishPrefab, plateInstance.transform, false);
            dishInstance.name = dishPrefab.name;
            dishInstance.transform.localPosition = Vector3.up * dishOnPlateYOffset;
            dishInstance.transform.localRotation = Quaternion.identity;
            dishInstance.transform.localScale = Vector3.one;
            return plateInstance;
        }

        /// <summary>从 SO Waiter03 在 waiterPoint 生成二楼小二表现。</summary>
        public static GameObject SpawnWaiter03AtPoint(Transform spawnPoint)
        {
            if (spawnPoint == null)
            {
                return null;
            }

            var prefab = ResolveWaiter03Prefab();
            if (prefab == null)
            {
                Debug.LogWarning("[TavernSecondFloorVip] 无法从 SO_Staff(id=5) 加载 Waiter03 预制体。");
                return null;
            }

            var instance = UnityEngine.Object.Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
            instance.name = SecondFloorWaiterVisualName;
            instance.SetActive(true);

            // 去掉会抢控制权的员工逻辑，仅保留表现 + 寻路。
            var waiterChars = instance.GetComponentsInChildren<WaiterCharacter>(true);
            for (var i = 0; i < waiterChars.Length; i++)
            {
                if (waiterChars[i] != null)
                {
                    waiterChars[i].enabled = false;
                    UnityEngine.Object.DestroyImmediate(waiterChars[i]);
                }
            }

            DisableAllNavMeshAgents(instance);
            return instance;
        }

        public static GameObject ResolveWaiter03Prefab()
        {
            var staff = SO_Staff.GetById(Waiter03StaffId);
            var level = staff?.GetLevelConfig(1) ?? staff?.GetLevelConfig(0);
            if (level?.staffPrefab != null)
            {
                return level.staffPrefab;
            }

            // 兜底：扫 Waiter 角色里 staffId=5
            var all = SO_Staff.GetAll();
            for (var index = 0; index < all.Count; index++)
            {
                var current = all[index];
                if (current == null || current.role != StaffRole.Waiter)
                {
                    continue;
                }

                if (current.staffId != Waiter03StaffId)
                {
                    continue;
                }

                var cfg = current.GetLevelConfig(1) ?? current.GetLevelConfig(0);
                if (cfg?.staffPrefab != null)
                {
                    return cfg.staffPrefab;
                }
            }

            return null;
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName))
            {
                return null;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var child = root.GetChild(index);
                if (child.name == childName)
                {
                    return child;
                }

                var nested = FindChildRecursive(child, childName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        public static void DisableAllNavMeshAgents(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var agents = root.GetComponentsInChildren<NavMeshAgent>(true);
            for (var index = 0; index < agents.Length; index++)
            {
                if (agents[index] != null)
                {
                    agents[index].enabled = false;
                }
            }
        }

        public static bool TryEnableAgentOnNavMesh(NavMeshAgent agent, Vector3 preferredPosition)
        {
            if (agent == null)
            {
                return false;
            }

            if (!agent.enabled)
            {
                agent.enabled = true;
            }

            if (NavMesh.SamplePosition(preferredPosition, out var hit, 2f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                return agent.isOnNavMesh;
            }

            return agent.isOnNavMesh;
        }

        private static void EnsureDishPrefabCache()
        {
            if (dishPrefabCache.Count > 0)
            {
                return;
            }

            for (var index = 0; index < DishPrefabPaths.Length; index++)
            {
                var loaded = GameplayResourceStore.LoadAsset<GameObject>(DishPrefabPaths[index]);
                if (loaded != null)
                {
                    dishPrefabCache.Add(loaded);
                }
            }
        }

        private static bool TryResolveVipSeatPose(
            out Vector3 seatPosition,
            out Quaternion seatRotation,
            out Transform chair)
        {
            seatPosition = Vector3.zero;
            seatRotation = Quaternion.identity;
            // 优先 Shop_Interior/贵客椅子，保证用场景里摆好的坐姿坐标。
            chair = ResolveVipChair();
            if (chair != null)
            {
                seatPosition = chair.position;
                seatRotation = chair.rotation;
                return true;
            }

            // 场景若无「贵客椅子」，优先用桌位 SeatSlot（比整桌中心更像座位）。
            var seatSlot = FindPreferredSeatSlot();
            if (seatSlot != null)
            {
                seatPosition = seatSlot.position;
                seatRotation = seatSlot.rotation;
                chair = CreateRuntimeVipChair(seatPosition, seatRotation, seatSlot.parent);
                return true;
            }

            var table = UnityEngine.Object.FindFirstObjectByType<TableArea>();
            if (table != null)
            {
                table.EnsureSeatSlotsCachedForRuntime();
                if (table.TryGetSeatPoseByIndex(0, out seatPosition, out var lookAt))
                {
                    var toward = lookAt - seatPosition;
                    toward.y = 0f;
                    seatRotation = toward.sqrMagnitude > 0.0001f
                        ? Quaternion.LookRotation(toward.normalized, Vector3.up)
                        : table.transform.rotation;
                    seatPosition.y = table.GetSeatedCustomerY();
                    chair = CreateRuntimeVipChair(seatPosition, seatRotation, table.transform);
                    return true;
                }

                seatPosition = table.transform.position;
                seatRotation = table.transform.rotation;
                chair = CreateRuntimeVipChair(seatPosition, seatRotation, table.transform);
                return true;
            }

            var shop = ResolveNamedTransform(ShopInteriorName);
            if (shop == null)
            {
                return false;
            }

            chair = CreateRuntimeVipChair(shop.position, shop.rotation, shop);
            seatPosition = chair.position;
            seatRotation = chair.rotation;
            return true;
        }

        /// <summary>
        /// 解析 Shop_Interior/贵客椅子；找不到再全场景按名查找。
        /// </summary>
        public static Transform ResolveVipChair()
        {
            var shop = ResolveNamedTransform(ShopInteriorName);
            if (shop != null)
            {
                var nested = FindChildRecursive(shop, VipChairObjectName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return ResolveNamedTransform(VipChairObjectName);
        }

        private static Transform FindPreferredSeatSlot()
        {
            var tables = UnityEngine.Object.FindObjectsByType<TableArea>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var i = 0; i < tables.Length; i++)
            {
                var table = tables[i];
                if (table == null)
                {
                    continue;
                }

                table.EnsureSeatSlotsCachedForRuntime();
                var slot = FindChildRecursive(table.transform, "SeatSlot")
                           ?? FindChildRecursive(table.transform, "SeatSlot_0")
                           ?? FindChildRecursive(table.transform, "SeatSlot_1");
                if (slot != null)
                {
                    return slot;
                }
            }

            return FindChildRecursive(ResolveNamedTransform(VipTableObjectName), "SeatSlot");
        }

        private static Transform CreateRuntimeVipChair(
            Vector3 worldPosition,
            Quaternion worldRotation,
            Transform parent = null)
        {
            var existing = ResolveNamedTransform(VipChairObjectName);
            if (existing != null)
            {
                existing.SetPositionAndRotation(worldPosition, worldRotation);
                return existing;
            }

            var go = new GameObject(VipChairObjectName);
            if (parent != null)
            {
                go.transform.SetParent(parent, true);
            }

            go.transform.SetPositionAndRotation(worldPosition, worldRotation);
            Debug.LogWarning(
                $"[TavernSecondFloorVip] 场景缺少「{VipChairObjectName}」，已运行时创建挂点。");
            return go.transform;
        }
    }

    /// <summary>
    /// 二楼贵客：从入口走到椅子落座；离店时可起身寻路到终点。
    /// </summary>
    public sealed class SecondFloorVipSeatDriver : MonoBehaviour
    {
        /// <summary>二楼贵客入座后锁定的世界坐标。</summary>
        public static readonly Vector3 SeatedWorldPosition = new(-2.8f, 0.26f, -6.4f);

        private Transform chair;
        private Vector3 seatPosition;
        private Quaternion seatRotation;
        private Animator animator;
        private NavMeshAgent agent;
        private int sittingStateHash;
        private bool hasSpeed;
        private bool hasIsSitting;
        private bool hasSitDown;
        private bool hasStandUp;
        private bool hasIsEating;
        private bool hasStartEat;
        private bool hasStopEat;
        private bool lockSitting;
        private bool isLeaving;
        private bool hasEnteredSeat;
        private bool isEating;
        /// <summary>坐下动画播放期间也钉死最终坐姿点，避免先在椅子坐标播动作再瞬移。</summary>
        private bool pinningSeatedPosition;

        public bool IsLeaving => isLeaving;
        public bool HasEnteredSeat => hasEnteredSeat;

        /// <summary>直接落座（兼容兜底）。</summary>
        public void Bind(Transform chairAnchor, Vector3 worldPosition, Quaternion worldRotation)
        {
            chair = chairAnchor;
            seatPosition = worldPosition;
            seatRotation = worldRotation;
            lockSitting = true;
            isLeaving = false;
            hasEnteredSeat = true;
            CacheAnimator();
            DisableRootMotion();
            ApplyTransform();
            ForceSitNow();
        }

        /// <summary>从入口点生成，尚未落座；由 <see cref="EnterAndSitRoutine"/> 完成入座。</summary>
        public void BindForEnter(
            Transform chairAnchor,
            Vector3 worldSeatPosition,
            Quaternion worldSeatRotation,
            Vector3 worldEnterPosition,
            Quaternion worldEnterRotation)
        {
            chair = chairAnchor;
            seatPosition = worldSeatPosition;
            seatRotation = worldSeatRotation;
            lockSitting = false;
            isLeaving = false;
            hasEnteredSeat = false;
            CacheAnimator();
            transform.SetPositionAndRotation(worldEnterPosition, worldEnterRotation);
            if (hasSpeed && animator != null)
            {
                animator.SetFloat("Speed", 0f);
            }

            if (hasIsSitting && animator != null)
            {
                animator.SetBool("IsSitting", false);
            }
        }

        public void ForceSitNow()
        {
            CacheAnimator();
            DisableRootMotion();
            ApplyTransform();
            if (animator == null)
            {
                return;
            }

            animator.enabled = true;
            animator.speed = 1f;
            if (hasSpeed)
            {
                animator.SetFloat("Speed", 0f);
            }

            if (hasStandUp)
            {
                animator.ResetTrigger("StandUp");
            }

            if (hasSitDown)
            {
                animator.ResetTrigger("SitDown");
            }

            if (hasIsSitting)
            {
                animator.SetBool("IsSitting", true);
            }

            // 贵客 Sitting 绑定非循环「坐」片段：切入末帧才是坐姿，从 0 会整段播坐下。
            if (sittingStateHash != 0 && animator.HasState(0, sittingStateHash))
            {
                animator.Play(sittingStateHash, 0, 1f);
            }
            else
            {
                animator.Play("Sitting", 0, 1f);
            }

            animator.Update(0f);
        }

        /// <summary>从当前位置寻路到贵客椅子并落座。</summary>
        public IEnumerator EnterAndSitRoutine()
        {
            if (hasEnteredSeat)
            {
                yield break;
            }

            CacheAnimator();
            EnsureAgent();
            var destination = seatPosition;
            if (chair != null)
            {
                destination = chair.position;
            }

            if (!TavernSecondFloorVipService.TryEnableAgentOnNavMesh(agent, transform.position))
            {
                yield return SitDownAtSeatRoutine(playSitDownTransition: false);
                yield break;
            }

            agent.isStopped = false;
            agent.SetDestination(destination);
            var timeout = 14f;
            while (timeout > 0f)
            {
                timeout -= Time.deltaTime;
                if (hasSpeed && animator != null && agent != null && agent.isOnNavMesh)
                {
                    animator.SetFloat("Speed", agent.velocity.magnitude);
                }

                if (!agent.pathPending
                    && agent.hasPath
                    && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
                {
                    break;
                }

                if (!agent.pathPending && !agent.hasPath)
                {
                    agent.SetDestination(destination);
                }

                yield return null;
            }

            yield return SitDownAtSeatRoutine(playSitDownTransition: true);
        }

        private IEnumerator SitDownAtSeatRoutine(bool playSitDownTransition)
        {
            const float sitAnimTimeoutSeconds = 2.6f;

            StopAgentCompletely();
            animator = null; // 强制重新抓 Animator，避免引用失效
            CacheAnimator();
            DisableRootMotion();
            if (animator != null)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = true;
                animator.speed = 1f;
                if (hasSpeed)
                {
                    animator.SetFloat("Speed", 0f);
                }
            }

            // 不绑椅子子节点；从坐下动画第一帧起就用最终坐姿坐标。
            if (transform.parent != null)
            {
                transform.SetParent(null, true);
            }

            pinningSeatedPosition = true;
            SnapToSeatedWorldPosition();
            yield return null;
            SnapToSeatedWorldPosition();

            // 贵客 Sitting = 非循环「坐」片段（约 2.27s）：必须从 0 播完才看得见坐下过程。
            if (playSitDownTransition && animator != null)
            {
                if (hasStandUp)
                {
                    animator.ResetTrigger("StandUp");
                }

                if (hasIsSitting)
                {
                    animator.SetBool("IsSitting", false);
                }

                // 先切到 Sitting(0)，再补 SitDown，确保一定进入坐下状态机。
                if (sittingStateHash != 0 && animator.HasState(0, sittingStateHash))
                {
                    animator.Play(sittingStateHash, 0, 0f);
                }
                else
                {
                    animator.CrossFadeInFixedTime("Sitting", 0.05f, 0, 0f);
                }

                if (hasSitDown)
                {
                    animator.ResetTrigger("SitDown");
                    animator.SetTrigger("SitDown");
                }

                animator.Update(0f);
                SnapToSeatedWorldPosition();

                var elapsed = 0f;
                while (elapsed < sitAnimTimeoutSeconds)
                {
                    elapsed += Time.deltaTime;
                    SnapToSeatedWorldPosition();
                    if (hasSpeed)
                    {
                        animator.SetFloat("Speed", 0f);
                    }

                    var state = animator.GetCurrentAnimatorStateInfo(0);
                    var inSitting = state.IsName("Sitting") || state.IsName("Base Layer.Sitting");
                    if (inSitting && !animator.IsInTransition(0) && state.normalizedTime >= 0.98f)
                    {
                        break;
                    }

                    yield return null;
                }
            }

            lockSitting = true;
            pinningSeatedPosition = false;
            hasEnteredSeat = true;
            ForceSitNow();
            yield return null;
            ForceSitNow();
        }

        private void StopAgentCompletely()
        {
            if (agent == null)
            {
                agent = GetComponent<NavMeshAgent>() ?? GetComponentInChildren<NavMeshAgent>(true);
            }

            if (agent == null)
            {
                return;
            }

            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }

            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.enabled = false;
        }

        /// <summary>
        /// 起身并走到终点，到达后回调。
        /// </summary>
        public IEnumerator LeaveToPointRoutine(Transform endPoint, Action onArrived)
        {
            if (isLeaving)
            {
                yield break;
            }

            isLeaving = true;
            lockSitting = false;
            CacheAnimator();
            StopEatingAnimation();
            EnsureAgent();
            if (agent != null)
            {
                agent.updatePosition = true;
                agent.updateRotation = true;
            }

            if (hasIsSitting)
            {
                animator?.SetBool("IsSitting", false);
            }

            if (hasStandUp)
            {
                animator?.SetTrigger("StandUp");
            }

            yield return new WaitForSeconds(0.25f);

            var destination = endPoint != null ? endPoint.position : transform.position + transform.forward * 2f;
            if (!TavernSecondFloorVipService.TryEnableAgentOnNavMesh(agent, transform.position))
            {
                transform.position = destination;
                onArrived?.Invoke();
                yield break;
            }

            agent.isStopped = false;
            agent.SetDestination(destination);
            var timeout = 12f;
            while (timeout > 0f)
            {
                timeout -= Time.deltaTime;
                if (hasSpeed && animator != null && agent != null && agent.isOnNavMesh)
                {
                    animator.SetFloat("Speed", agent.velocity.magnitude);
                }

                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.15f)
                {
                    break;
                }

                yield return null;
            }

            if (hasSpeed && animator != null)
            {
                animator.SetFloat("Speed", 0f);
            }

            onArrived?.Invoke();
        }

        private void EnsureAgent()
        {
            agent = GetComponent<NavMeshAgent>() ?? GetComponentInChildren<NavMeshAgent>(true);
            if (agent != null)
            {
                agent.updatePosition = true;
                agent.updateRotation = true;
                return;
            }

            agent = gameObject.AddComponent<NavMeshAgent>();
            agent.radius = 0.15f;
            agent.speed = 0.85f;
            agent.acceleration = 2f;
            agent.angularSpeed = 9000f;
            agent.height = 1f;
        }

        private void LateUpdate()
        {
            if (isLeaving)
            {
                return;
            }

            // 坐下过程中只钉位置，不 ForceSitNow，避免把坐下动画切到末帧。
            if (pinningSeatedPosition)
            {
                SnapToSeatedWorldPosition();
                return;
            }

            if (!lockSitting)
            {
                return;
            }

            ApplyTransform();
            if (animator == null)
            {
                return;
            }

            if (hasSpeed)
            {
                animator.SetFloat("Speed", 0f);
            }

            if (hasIsSitting)
            {
                animator.SetBool("IsSitting", true);
            }

            // 仅在离开 Sitting 时强制回末帧；用餐中保持 Eating，勿打断。
            if (isEating)
            {
                return;
            }

            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName("Sitting") && !state.IsName("Base Layer.Sitting"))
            {
                ForceSitNow();
            }
        }

        private void SnapToSeatedWorldPosition()
        {
            seatPosition = SeatedWorldPosition;
            transform.SetPositionAndRotation(seatPosition, seatRotation);
        }

        private void ApplyTransform()
        {
            // 入座后锁定世界坐标（不跟椅子旋转；位置固定为包厢坐姿点）。
            if (lockSitting || pinningSeatedPosition)
            {
                seatPosition = SeatedWorldPosition;
            }
            else if (chair != null)
            {
                seatPosition = chair.position;
            }

            transform.SetPositionAndRotation(seatPosition, seatRotation);
        }

        private void DisableRootMotion()
        {
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }
        }

        private void CacheAnimator()
        {
            if (animator != null)
            {
                return;
            }

            // 优先选带 SitDown/IsSitting 的 Animator（避免误绑到无控制器子节点）。
            var animators = GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                var candidate = animators[i];
                if (candidate == null || candidate.runtimeAnimatorController == null)
                {
                    continue;
                }

                foreach (var parameter in candidate.parameters)
                {
                    if (parameter.name is "SitDown" or "IsSitting" or "Sitting")
                    {
                        animator = candidate;
                        break;
                    }
                }

                if (animator != null)
                {
                    break;
                }
            }

            animator ??= GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                return;
            }

            sittingStateHash = Animator.StringToHash("Sitting");
            foreach (var parameter in animator.parameters)
            {
                if (parameter.name == "Speed" && parameter.type == AnimatorControllerParameterType.Float)
                {
                    hasSpeed = true;
                }
                else if (parameter.name == "IsSitting" && parameter.type == AnimatorControllerParameterType.Bool)
                {
                    hasIsSitting = true;
                }
                else if (parameter.name == "SitDown" && parameter.type == AnimatorControllerParameterType.Trigger)
                {
                    hasSitDown = true;
                }
                else if (parameter.name == "StandUp" && parameter.type == AnimatorControllerParameterType.Trigger)
                {
                    hasStandUp = true;
                }
                else if (parameter.name == "IsEating" && parameter.type == AnimatorControllerParameterType.Bool)
                {
                    hasIsEating = true;
                }
                else if (parameter.name == "StartEat" && parameter.type == AnimatorControllerParameterType.Trigger)
                {
                    hasStartEat = true;
                }
                else if (parameter.name == "StopEat" && parameter.type == AnimatorControllerParameterType.Trigger)
                {
                    hasStopEat = true;
                }
            }
        }

        /// <summary>
        /// 入座后播吃饭动画（StartEat / IsEating），用餐结束需 <see cref="StopEatingAnimation"/>。
        /// </summary>
        public void StartEatingAnimation()
        {
            CacheAnimator();
            isEating = true;
            if (animator == null)
            {
                return;
            }

            if (hasIsEating)
            {
                animator.SetBool("IsEating", true);
            }

            if (hasStartEat)
            {
                animator.ResetTrigger("StartEat");
                animator.SetTrigger("StartEat");
            }
        }

        /// <summary>
        /// 吃饭片段非循环时按间隔再点一次 StartEat。
        /// </summary>
        public void RetriggerEatingAnimation()
        {
            if (!isEating || animator == null || !hasStartEat)
            {
                return;
            }

            animator.ResetTrigger("StartEat");
            animator.SetTrigger("StartEat");
        }

        public void StopEatingAnimation()
        {
            isEating = false;
            if (animator == null)
            {
                return;
            }

            if (hasIsEating)
            {
                animator.SetBool("IsEating", false);
            }

            if (hasStopEat)
            {
                animator.ResetTrigger("StopEat");
                animator.SetTrigger("StopEat");
            }
        }
    }
}
