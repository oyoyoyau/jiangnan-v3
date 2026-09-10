using System.Collections.Generic;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.Model;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace JN.Client.Scene
{
    public partial class TavernSceneManager
    {
        #region Guide Lookup

        /// <summary>
        /// 查找新手引导购买后的真实目标物件。
        /// </summary>
        /// <param name="targetName">名称。</param>
        /// <returns>返回匹配到的对象引用。</returns>
        private GameObject FindGuideTargetObject(string targetName)
        {
            return FindSceneGameObjectByName(targetName, objectMovePoint);
        }

        /// <summary>
        /// 读取新手引导设施购买价格（优先 Facility.cost）。
        /// </summary>
        private static int GetGuideFacilityCostByKey(string guideKey)
        {
            var facility = FacilityConfigUtility.GetByGuideKey(guideKey);
            return FacilityConfigUtility.GetUnlockCost(facility, 0);
        }

        /// <summary>
        /// 读取新手引导设备的一阶购买价格（兼容旧 equipmentId 调用）。
        /// </summary>
        /// <param name="equipmentId">数据编号。</param>
        /// <returns>返回计算后的数值。</returns>
        private static int GetGuideEquipmentCost(int equipmentId)
        {
            var fromFacility = FacilityConfigUtility.GetUnlockCost(
                FacilityConfigUtility.GetByEquipmentId(equipmentId),
                0);
            if (fromFacility > 0)
            {
                return fromFacility;
            }

            var equipment = SO_Equipment.GetById(equipmentId);
            var levelConfig = equipment != null ? equipment.GetLevelConfig(1) : null;
            return levelConfig != null ? Mathf.Max(0, levelConfig.upgradeCost) : 0;
        }

        /// <summary>
        /// 读取新手引导员工的一阶招聘价格。
        /// </summary>
        /// <param name="role">参数值。</param>
        /// <param name="preferredStaffId">数据编号。</param>
        /// <returns>返回计算后的数值。</returns>
        private static int GetGuideStaffCost(StaffRole role, int preferredStaffId)
        {
            if (DataManager.Instance != null)
            {
                return DataManager.Instance.GetGuideStaffHireCost(preferredStaffId, role);
            }

            var soCost = 0;
            var allStaff = SO_Staff.GetAll();
            for (var index = 0; index < allStaff.Count; index++)
            {
                var staff = allStaff[index];
                if (staff == null || staff.role != role)
                {
                    continue;
                }

                if (!int.TryParse(staff.staffId, out var numericStaffId) || numericStaffId != preferredStaffId)
                {
                    continue;
                }

                var preferredLevel = staff.GetLevelConfig(1);
                if (preferredLevel != null)
                {
                    soCost = Mathf.Max(0, preferredLevel.hireUpgradeCost);
                }

                break;
            }

            return StaffConfigUtility.GetRecruitmentCost(preferredStaffId, soCost);
        }

        /// <summary>
        /// 在当前场景中按名称查找 节点。
        /// </summary>
        /// <param name="targetName">名称。</param>
        /// <returns>返回匹配到的对象引用。</returns>
        private static Transform FindSceneTransformByName(string targetName)
        {
            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var current in transforms)
            {
                if (current.name == targetName)
                {
                    return current;
                }
            }

            return null;
        }

        /// <summary>
        /// 在当前场景中按名称查找 游戏物件。
        /// </summary>
        /// <param name="targetName">名称。</param>
        /// <returns>返回匹配到的对象引用。</returns>
        private static GameObject FindSceneGameObjectByName(string targetName)
        {
            return FindSceneGameObjectByName(targetName, null);
        }

        /// <summary>
        /// 在当前场景中按名称查找 游戏物件。
        /// </summary>
        /// <param name="targetName">名称。</param>
        /// <param name="excludedRoot">参数值。</param>
        /// <returns>返回匹配到的对象引用。</returns>
        private static GameObject FindSceneGameObjectByName(string targetName, Transform excludedRoot)
        {
            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var current in transforms)
            {
                if (current.name == targetName && !IsChildOf(current, excludedRoot))
                {
                    return current.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// 优先在 物件s 根节点下查找引导场景物件。
        /// </summary>
        /// <param name="targetName">名称。</param>
        /// <returns>返回匹配到的对象引用。</returns>
        private GameObject FindGuideSceneObject(string targetName)
        {
            return FindChildGameObjectByName(sceneObjectsRoot, targetName)
                   ?? FindSceneGameObjectByName(targetName, objectMovePoint);
        }

        /// <summary>
        /// 把找到的场景物件加入引导物件列表。
        /// </summary>
        /// <param name="collection">参数值。</param>
        /// <param name="targetName">名称。</param>
        private void AddGuideSceneObject(List<GameObject> collection, string targetName)
        {
            if (collection == null || string.IsNullOrEmpty(targetName))
            {
                return;
            }

            var sceneObject = FindGuideSceneObject(targetName);
            if (sceneObject != null && !collection.Contains(sceneObject))
            {
                collection.Add(sceneObject);
            }
        }

        /// <summary>
        /// 注册厨房购买项对应的物件、底板和搬运 预制体。
        /// </summary>
        /// <param name="itemKey">语言表键值。</param>
        /// <param name="displayName">名称。</param>
        /// <param name="sceneObjectName">名称。</param>
        /// <param name="build基础Name">名称。</param>
        /// <param name="carrierPrefabPath">资源路径。</param>
        private void AddGuideKitchenAnchor(string itemKey, string displayName, string sceneObjectName, string buildBaseName, string carrierPrefabPath)
        {
            var sceneObject = FindGuideSceneObject(sceneObjectName);
            if (sceneObject != null && !guideStoveSceneObjects.Contains(sceneObject))
            {
                guideStoveSceneObjects.Add(sceneObject);
            }

            var buildBase = FindGuideSceneObject(buildBaseName) ?? FindGuideTargetObject(buildBaseName);
            if (sceneObject == null && buildBase == null)
            {
                return;
            }

            guideKitchenAnchors.Add(new GuidePurchaseAnchor
            {
                itemKey = itemKey,
                displayName = displayName,
                sceneObject = sceneObject,
                buildBase = buildBase,
                carrierPrefabPath = carrierPrefabPath
            });
        }

        /// <summary>
        /// 判断节点是否属于指定父节点层级。
        /// </summary>
        /// <param name="current">参数值。</param>
        /// <param name="root">参数值。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private static bool IsChildOf(Transform current, Transform root)
        {
            if (current == null || root == null)
            {
                return false;
            }

            var iterator = current;
            while (iterator != null)
            {
                if (iterator == root)
                {
                    return true;
                }

                iterator = iterator.parent;
            }

            return false;
        }

        /// <summary>
        /// 在指定父节点下按名称查找子物体。
        /// </summary>
        /// <param name="parent">参数值。</param>
        /// <param name="targetName">名称。</param>
        /// <returns>返回匹配到的对象引用。</returns>
        private static GameObject FindChildGameObjectByName(Transform parent, string targetName)
        {
            if (parent == null || string.IsNullOrEmpty(targetName))
            {
                return null;
            }

            var children = parent.GetComponentsInChildren<Transform>(true);
            foreach (var child in children)
            {
                if (child != null && child.name == targetName)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// 把世界坐标吸附到最近的 导航网格 点。
        /// </summary>
        /// <param name="sourcePosition">来源对象。</param>
        /// <param name="navMeshPosition">坐标。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private static bool TryGetNavMeshPosition(Vector3 sourcePosition, out Vector3 navMeshPosition)
        {
            if (NavMesh.SamplePosition(sourcePosition, out var hit, NavMeshSampleDistance, NavMesh.AllAreas))
            {
                navMeshPosition = hit.position;
                return true;
            }

            navMeshPosition = sourcePosition;
            return false;
        }

        /// <summary>
        /// 在编辑器下按路径加载桌面菜品 预制体。
        /// </summary>
        /// <param name="assetPath">资源路径。</param>
        /// <returns>返回匹配到的对象引用。</returns>
        private static GameObject LoadDishPrefab(string assetPath)
        {
            return GameplayResourceStore.LoadAsset<GameObject>(assetPath);
        }

        /// <summary>
        /// 从序列化引用里缓存顾客 预制体。
        /// </summary>
        private void CacheCustomerPrefabsFromReferences()
        {
            foreach (var prefab in customerPrefabAssets)
            {
                if (prefab != null)
                {
                    customerTemplates.Add(prefab);
                }
            }

            if (customerTemplates.Count == 0)
            {
                Debug.LogWarning("[TavernSceneManager] 未找到顾客模板，无法生成顾客。");
            }

            CacheVipCustomerPrefabsFromReferences();
        }

        /// <summary>
        /// 从序列化引用缓存贵客/稀客预制体（独立池，不并入普通顾客模板）。
        /// 贵客固定 CustomerM5；稀客固定 CustomerM6。
        /// </summary>
        private void CacheVipCustomerPrefabsFromReferences()
        {
            vipCustomerTemplates.Clear();
            rareCustomerTemplates.Clear();
            if (vipCustomerPrefabAssets == null)
            {
                return;
            }

            foreach (var prefab in vipCustomerPrefabAssets)
            {
                if (prefab == null)
                {
                    continue;
                }

                if (IsCustomerModelM5(prefab))
                {
                    vipCustomerTemplates.Add(prefab);
                }
                else if (IsCustomerModelM6(prefab))
                {
                    rareCustomerTemplates.Add(prefab);
                }
            }

            // 未按严格 token 匹配时，按名称含 M5/M6 回退。
            if (vipCustomerTemplates.Count == 0)
            {
                foreach (var prefab in vipCustomerPrefabAssets)
                {
                    if (prefab != null && prefab.name.IndexOf("M5", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        vipCustomerTemplates.Add(prefab);
                    }
                }
            }

            if (rareCustomerTemplates.Count == 0)
            {
                foreach (var prefab in vipCustomerPrefabAssets)
                {
                    if (prefab != null && prefab.name.IndexOf("M6", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        rareCustomerTemplates.Add(prefab);
                    }
                }
            }

            if (vipCustomerTemplates.Count == 0)
            {
                Debug.LogWarning("[TavernSceneManager] 贵客池未配置 CustomerM5，无法刷出贵客。请在 Inspector 的 Vip Customer Prefab Assets 中指定 P_Character_CustomerM5。");
            }
            else
            {
                TavernSecondFloorVipService.CacheVipPrefab(vipCustomerTemplates[0]);
            }

            if (rareCustomerTemplates.Count == 0)
            {
                Debug.LogWarning("[TavernSceneManager] 稀客池未配置 CustomerM6，无法刷出稀客。请在 Inspector 的 Vip Customer Prefab Assets 中指定 P_Character_CustomerM6。");
            }
        }

        #endregion
    }
}
