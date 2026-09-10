using System.Collections.Generic;
using JN.Client.Model;
using UnityEngine;
using UnityEngine.AI;

namespace JN.Client.Scene
{
    public partial class TavernSceneManager
    {
        /// <summary>额外厨师沿世界 X 与首名错开的间距。</summary>
        private const float GuideChefStackStepWorldX = -0.6f;

        /// <summary>首名厨师世界站位（x/z）；y 优先取场景标记高度。</summary>
        private static readonly Vector3 GuideChefHomeBasePosition = new(-2.55f, 0f, -2.9f);

        /// <summary>厨师站位朝向（Y=180）。</summary>
        private static readonly Quaternion GuideChefHomeRotation = Quaternion.Euler(0f, 180f, 0f);

        /// <summary>前台（掌柜）实际站位（世界坐标）。</summary>
        private static readonly Vector3 GuideShopkeeperHomePosition = new(-0.06f, 0f, -5.5f);

        /// <summary>小二雇佣/站位场景挂点名前缀（小二雇佣1~4，与 WJ 场景 Objects 一致）。</summary>
        private const string WaiterEmployMarkerNamePrefix = "小二雇佣";

        /// <summary>厨师雇佣场景挂点名前缀（厨师雇佣1~3）。</summary>
        private const string ChefEmployMarkerNamePrefix = "厨师雇佣";

        /// <summary>厨师场景站位标记（缩放仍以此为准；位置/朝向改用固定配置）。</summary>
        private static Transform FindGuideChefHomeMarker()
        {
            return FindSceneTransformByName(GuideChefMarkerName)
                   ?? FindSceneTransformByName("P_Character_Chef03_Chef");
        }

        /// <summary>按序号取厨师雇用挂点（1-based 节点名）。</summary>
        private static Transform FindChefEmployMarker(int index)
        {
            return FindSceneTransformByName($"{ChefEmployMarkerNamePrefix}{Mathf.Max(0, index) + 1}");
        }

        /// <summary>
        /// 厨师站位：首名 (-2.55, y, -2.9) 朝向 Y180；第 2/3 名 X 依次 -0.6。
        /// </summary>
        private static bool TryResolveGuideChefHomePose(
            int index,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            var safeIndex = Mathf.Max(0, index);
            var marker = FindGuideChefHomeMarker();
            var y = marker != null ? marker.position.y : GuideChefHomeBasePosition.y;
            scale = marker != null ? marker.lossyScale : Vector3.one;
            position = new Vector3(
                GuideChefHomeBasePosition.x + GuideChefStackStepWorldX * safeIndex,
                y,
                GuideChefHomeBasePosition.z);
            rotation = GuideChefHomeRotation;
            return true;
        }

        /// <summary>兼容旧调用：仅取厨师世界坐标。</summary>
        private static Vector3 ResolveGuideChefHomePosition(int index)
        {
            return TryResolveGuideChefHomePose(index, out var position, out _, out _)
                ? position
                : GuideChefHomeBasePosition + new Vector3(GuideChefStackStepWorldX * Mathf.Max(0, index), 0f, 0f);
        }

        /// <summary>兼容旧调用：厨师朝向固定 Y180。</summary>
        private static Quaternion ResolveGuideChefHomeRotation()
        {
            return GuideChefHomeRotation;
        }

        /// <summary>厨师缩放取场景标记，避免 Instantiates 用 1 把人缩放错。</summary>
        private static Vector3 ResolveGuideChefHomeScale()
        {
            return TryResolveGuideChefHomePose(0, out _, out _, out var scale)
                ? scale
                : Vector3.one;
        }

        /// <summary>前台掌柜固定站位。</summary>
        private static Vector3 ResolveGuideShopkeeperHomePosition()
        {
            return GuideShopkeeperHomePosition;
        }

        /// <summary>前台掌柜默认朝向（Y=-90）。</summary>
        private static Quaternion ResolveGuideShopkeeperHomeRotation()
        {
            return Quaternion.Euler(0f, -90f, 0f);
        }

        /// <summary>
        /// 前台模型缩放取原站位标记，避免固定站位后误用 Vector3.one 把人放大。
        /// </summary>
        private static Vector3 ResolveGuideShopkeeperHomeScale()
        {
            var scaleMarker = FindSceneTransformByName(GuideShopkeeperMarkerName)
                              ?? FindSceneTransformByName("WaiterF1");
            return scaleMarker != null ? scaleMarker.lossyScale : Vector3.one;
        }

        /// <summary>按序号取小二雇佣挂点（1-based 节点名）。</summary>
        private static Transform FindWaiterEmployMarker(int index)
        {
            return FindSceneTransformByName($"{WaiterEmployMarkerNamePrefix}{Mathf.Max(0, index) + 1}");
        }

        /// <summary>
        /// 小二模型缩放仍取原站位标记（与雇佣前一致），不跟雇佣挂点 scale，避免挂点放大把人撑大。
        /// </summary>
        private static Vector3 ResolveGuideWaiterHomeScale()
        {
            var scaleMarker = FindSceneTransformByName(GuideWaiterMarkerName)
                              ?? FindSceneTransformByName("WaiterF1_1");
            return scaleMarker != null ? scaleMarker.lossyScale : Vector3.one;
        }

        /// <summary>
        /// 小二站位：位置跟雇佣挂点「小二雇佣N」；待机朝向固定 Y=180（面朝客人）；缩放另取原站位。
        /// </summary>
        private static bool TryResolveGuideWaiterHomePose(
            int index,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            var marker = FindWaiterEmployMarker(index);
            if (marker == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                scale = Vector3.one;
                return false;
            }

            position = marker.position;
            rotation = Quaternion.Euler(0f, 180f, 0f);
            scale = ResolveGuideWaiterHomeScale();
            return true;
        }

        #region Guide Staff And Follow

        /// <summary>
        /// 获取指定员工类型当前在场景中的所有引导表现。
        /// </summary>
        /// <param name="visualKey">员工表现键。</param>
        /// <returns>去除空引用后的员工列表。</returns>
        private List<GameObject> GetGuideStaffVisualGroup(string visualKey)
        {
            if (!guideStaffVisualGroups.TryGetValue(visualKey, out var group) || group == null)
            {
                group = new System.Collections.Generic.List<GameObject>();
                guideStaffVisualGroups[visualKey] = group;
            }

            group.RemoveAll(current => current == null);
            return group;
        }

        private void RegisterGuideStaffVisualInGroup(string visualKey, GameObject visual)
        {
            if (string.IsNullOrEmpty(visualKey) || visual == null)
            {
                return;
            }

            var group = GetGuideStaffVisualGroup(visualKey);
            if (!group.Contains(visual))
            {
                group.Add(visual);
            }
        }

        /// <summary>
        /// 获取指定员工类型当前在场景中的所有有效表现对象。
        /// </summary>
        /// <param name="visualKey">员工表现键。</param>
        /// <returns>员工表现列表。</returns>
        private GameObject[] GetGuideStaffVisuals(string visualKey)
        {
            return GetGuideStaffVisualGroup(visualKey).ToArray();
        }

        /// <summary>
        /// 追加创建一个新的员工引导表现，并记录到分组里。
        /// </summary>
        /// <param name="visualKey">员工表现键。</param>
        /// <param name="role">员工角色。</param>
        /// <param name="preferredStaffId">优先员工编号。</param>
        /// <returns>创建后的员工对象。</returns>
        private GameObject CreateAdditionalGuideStaffVisual(string visualKey, StaffRole role, int preferredStaffId)
        {
            var staffPrefab = ResolveGuideStaffPrefab(visualKey, role);
            if (staffPrefab == null)
            {
                return null;
            }

            var visual = Instantiate(staffPrefab);
            EnsureGuideStaffCharacterComponent(visual, role, preferredStaffId);
            var group = GetGuideStaffVisualGroup(visualKey);
            var suffix = group.Count;
            visual.name = suffix <= 0 ? $"{visualKey}_GuideVisual" : $"{visualKey}_GuideVisual_{suffix + 1}";
            foreach (var agent in visual.GetComponentsInChildren<NavMeshAgent>(true))
            {
                agent.enabled = false;
            }

            group.Add(visual);
            if (!guideStaffVisuals.ContainsKey(visualKey) || guideStaffVisuals[visualKey] == null)
            {
                guideStaffVisuals[visualKey] = visual;
            }

            return visual;
        }

        /// <summary>
        /// 销毁指定员工类型的全部引导表现。
        /// </summary>
        /// <param name="visualKey">员工表现键。</param>
        private void DestroyGuideStaffVisuals(string visualKey)
        {
            var group = GetGuideStaffVisualGroup(visualKey);
            for (var index = 0; index < group.Count; index++)
            {
                if (group[index] != null)
                {
                    // 销毁前先把入场动画占位移除，避免后续 HashSet 持有已销毁的引用。
                    staffVisualsBeingAnimated.Remove(group[index]);
                    Destroy(group[index]);
                }
            }

            group.Clear();
            guideStaffVisuals.Remove(visualKey);
        }

        /// <summary>
        /// 根据当前招聘员工列表同步同类员工表现数量，并按 staffId 一一绑定。
        /// </summary>
        private void EnsureGuideStaffVisualCount(string visualKey, StaffRole role, IReadOnlyList<int> staffIds)
        {
            var targetCount = staffIds != null ? staffIds.Count : 0;
            var group = GetGuideStaffVisualGroup(visualKey);
            while (group.Count < targetCount)
            {
                var staffId = staffIds[group.Count];
                if (CreateAdditionalGuideStaffVisual(visualKey, role, staffId) == null)
                {
                    break;
                }
            }

            while (group.Count > targetCount)
            {
                var lastIndex = group.Count - 1;
                var visual = group[lastIndex];
                group.RemoveAt(lastIndex);
                if (visual != null)
                {
                    staffVisualsBeingAnimated.Remove(visual);
                    Destroy(visual);
                }
            }

            for (var index = 0; index < group.Count; index++)
            {
                BindGuideStaffId(group[index], role, staffIds[index]);
            }

            guideStaffVisuals[visualKey] = group.Count > 0 ? group[0] : null;
        }

        /// <summary>
        /// 兼容旧调用：同一 preferredStaffId 重复绑定 targetCount 次。
        /// </summary>
        private void EnsureGuideStaffVisualCount(string visualKey, StaffRole role, int targetCount, int preferredStaffId)
        {
            var ids = new List<int>(Mathf.Max(0, targetCount));
            for (var index = 0; index < targetCount; index++)
            {
                ids.Add(preferredStaffId);
            }

            EnsureGuideStaffVisualCount(visualKey, role, ids);
        }

        private const float GuideStaffStackStepX = -0.5f;
        private const float GuideStaffStackStepZ = -1f;
        private const int GuideStaffStackGridColumns = 3;

        /// <summary>
        /// 计算多人员工在同一工作点附近的散开偏移，避免人物完全重叠。
        /// 小二/掌柜等：index 0 在锚点，其余按 x 优先（步长 -0.5）、再 z（步长 -1）排成方阵网格。
        /// </summary>
        /// <param name="visualKey">员工表现键。</param>
        /// <param name="index">序号。</param>
        /// <returns>对应序号的本地偏移。</returns>
        private static Vector3 GetGuideStaffStackOffset(string visualKey, int index)
        {
            if (index <= 0)
            {
                return Vector3.zero;
            }

            if (visualKey == GuideChefVisualKey)
            {
                // 厨师站位由 ResolveGuideChefHomePosition 固定，不使用网格偏移。
                return Vector3.zero;
            }

            return GetGuideStaffGridStackOffset(index);
        }

        /// <summary>
        /// 按 x 优先、再 z 扩展的方阵网格偏移（index 0 由调用方处理为锚点中心）。
        /// </summary>
        private static Vector3 GetGuideStaffGridStackOffset(int index)
        {
            var slot = index - 1;
            var col = slot % GuideStaffStackGridColumns;
            var row = slot / GuideStaffStackGridColumns;
            return new Vector3(GuideStaffStackStepX * (col + 1), 0f, GuideStaffStackStepZ * row);
        }

        /// <summary>
        /// 刷新新手引导阶段的员工站位表现。
        /// </summary>
        /// <param name="visualKey">用于区分掌柜、小二和厨师的表现键。</param>
        /// <param name="role">参数值。</param>
        /// <param name="should显示">参数值。</param>
        /// <param name="anchorObject">参数值。</param>
        /// <param name="localOffset">参数值。</param>
        /// <param name="preferredStaffId">数据编号。</param>
        /// <param name="extraYawDegrees">在锚点朝向基础上额外旋转的角度。</param>
        private void RefreshGuideStaffVisual(string visualKey, StaffRole role, bool shouldShow, GameObject anchorObject, Vector3 localOffset, int preferredStaffId, float extraYawDegrees = 0f)
        {
            if (!shouldShow || anchorObject == null || !anchorObject.activeInHierarchy)
            {
                DestroyGuideStaffVisuals(visualKey);
                return;
            }

            if (HasVisibleRuntimeStaffNearAnchor(GetStaffNameKeyword(visualKey, role), anchorObject.transform))
            {
                DestroyGuideStaffVisuals(visualKey);
                return;
            }

            if (guideStaffVisuals.TryGetValue(visualKey, out var existingVisual) && existingVisual != null)
            {
                BindGuideStaffId(existingVisual, role, preferredStaffId);
                // 入场动画进行中保留当前位置，等动画播完再交回常规位置同步逻辑。
                if (staffVisualsBeingAnimated.Contains(existingVisual))
                {
                    return;
                }
                UpdateGuideStaffTransform(existingVisual.transform, anchorObject.transform, localOffset, extraYawDegrees);
                return;
            }

            var staffPrefab = ResolveGuideStaffPrefab(visualKey, role);
            if (staffPrefab == null)
            {
                return;
            }

            var visual = Instantiate(staffPrefab);
            EnsureGuideStaffCharacterComponent(visual, role, preferredStaffId);
            visual.name = $"{visualKey}_GuideVisual";
            foreach (var agent in visual.GetComponentsInChildren<NavMeshAgent>(true))
            {
                agent.enabled = false;
            }

            UpdateGuideStaffTransform(visual.transform, anchorObject.transform, localOffset, extraYawDegrees);
            guideStaffVisuals[visualKey] = visual;
            RegisterGuideStaffVisualInGroup(visualKey, visual);
        }

        /// <summary>
        /// 营业中已在跑任务/打盹/拉客的小二（及忙碌厨师）保留世界坐标，
        /// 避免结账加声望等触发 RefreshGuideWorldState 时被瞬移回默认站位。
        /// </summary>
        private bool ShouldPreserveGuideStaffRuntimeWorldPose(string visualKey, GameObject visual)
        {
            if (visual == null)
            {
                return false;
            }

            if (visualKey == GuideWaiterVisualKey)
            {
                if (!IsBusinessActive && !postCloseCleanupActive)
                {
                    return false;
                }

                return busyWaiters.Contains(visual)
                       || waiterTaskRoutines.ContainsKey(visual)
                       || IsWaiterNapping(visual)
                       || attractingWaiters.Contains(visual)
                       || IsWaiterInAttractFlow(visual)
                       || waitersSuppressHomeReturn.Contains(visual)
                       || (waiterContexts.TryGetValue(visual, out var context)
                           && context != null
                           && context.CurrentStateKey != WaiterStateKeys.Idle
                           && context.CurrentStateKey != WaiterStateKeys.ReturningHome);
            }

            if (visualKey == GuideChefVisualKey)
            {
                if (!IsBusinessActive && !postCloseCleanupActive)
                {
                    return false;
                }

                return nappingChefs.Contains(visual)
                       || chefWakeRoutines.ContainsKey(visual)
                       || (chefRuntimeContexts.TryGetValue(visual, out var chefContext)
                           && chefContext != null
                           && !string.IsNullOrEmpty(chefContext.CurrentStateKey)
                           && chefContext.CurrentStateKey != ChefStateKeys.Idle
                           && chefContext.CurrentStateKey != ChefStateKeys.ReturningHome);
            }

            return false;
        }

        /// <summary>
        /// 使用场景中的预摆放节点作为员工生成标记位。
        /// </summary>
        /// <param name="visualKey">用于区分掌柜、小二和厨师的表现键。</param>
        /// <param name="role">员工角色。</param>
        /// <param name="shouldShow">是否需要显示员工。</param>
        /// <param name="markerName">场景中用于对齐位置和朝向的节点名。</param>
        /// <param name="legacyMarkerName">旧场景节点名，用于兼容尚未改名的场景。</param>
        /// <param name="fallbackAnchor">找不到标记位时使用的锚点。</param>
        /// <param name="fallbackOffset">找不到标记位时使用的本地偏移。</param>
        /// <param name="preferredStaffId">员工配置编号。</param>
        /// <param name="fallbackYawDegrees">找不到标记位时额外旋转的角度。</param>
        private void RefreshGuideStaffVisualAtSceneMarker(
            string visualKey,
            StaffRole role,
            bool shouldShow,
            string markerName,
            string legacyMarkerName,
            GameObject fallbackAnchor,
            Vector3 fallbackOffset,
            int preferredStaffId,
            float fallbackYawDegrees = 0f)
        {
            if (!shouldShow)
            {
                DestroyGuideStaffVisuals(visualKey);
                return;
            }

            var marker = FindSceneTransformByName(markerName) ?? FindSceneTransformByName(legacyMarkerName);
            if (marker == null)
            {
                if (visualKey == GuideShopkeeperVisualKey)
                {
                    var shopkeeperVisual = GetOrCreateGuideStaffVisual(visualKey, role, preferredStaffId);
                    if (shopkeeperVisual == null || staffVisualsBeingAnimated.Contains(shopkeeperVisual))
                    {
                        return;
                    }

                    shopkeeperVisual.transform.position = ResolveGuideShopkeeperHomePosition();
                    shopkeeperVisual.transform.rotation = ResolveGuideShopkeeperHomeRotation();
                    shopkeeperVisual.transform.localScale = ResolveGuideStaffVisualScale(
                        visualKey,
                        ResolveGuideShopkeeperHomeScale());
                    return;
                }

                if (visualKey == GuideWaiterVisualKey
                    && TryResolveGuideWaiterHomePose(0, out var nullMarkerWaiterHome, out var nullMarkerWaiterRot, out var nullMarkerWaiterScale))
                {
                    var waiterVisual = GetOrCreateGuideStaffVisual(visualKey, role, preferredStaffId);
                    if (waiterVisual == null || staffVisualsBeingAnimated.Contains(waiterVisual))
                    {
                        return;
                    }

                    if (ShouldPreserveGuideStaffRuntimeWorldPose(visualKey, waiterVisual))
                    {
                        return;
                    }

                    waiterVisual.transform.SetPositionAndRotation(nullMarkerWaiterHome, nullMarkerWaiterRot);
                    waiterVisual.transform.localScale = ResolveGuideStaffVisualScale(visualKey, nullMarkerWaiterScale);
                    return;
                }

                if (visualKey == GuideChefVisualKey)
                {
                    var chefVisual = GetOrCreateGuideStaffVisual(visualKey, role, preferredStaffId);
                    if (chefVisual == null || staffVisualsBeingAnimated.Contains(chefVisual))
                    {
                        return;
                    }

                    if (ShouldPreserveGuideStaffRuntimeWorldPose(visualKey, chefVisual))
                    {
                        return;
                    }

                    if (TryResolveGuideChefHomePose(0, out var chefHome, out var chefRot, out var chefScale))
                    {
                        chefVisual.transform.SetPositionAndRotation(chefHome, chefRot);
                        chefVisual.transform.localScale = ResolveGuideStaffVisualScale(visualKey, chefScale);
                    }

                    return;
                }

                RefreshGuideStaffVisual(visualKey, role, true, fallbackAnchor, fallbackOffset, preferredStaffId, fallbackYawDegrees);
                return;
            }

            var visual = GetOrCreateGuideStaffVisual(visualKey, role, preferredStaffId);
            if (visual == null)
            {
                return;
            }

            // 入场动画进行中不要瞬移到锚点，否则人会先闪到目的地再被走过来。
            if (staffVisualsBeingAnimated.Contains(visual))
            {
                return;
            }

            // 营业中小二/厨师已在干活：引导刷新只保证对象存在，不改世界坐标。
            if (ShouldPreserveGuideStaffRuntimeWorldPose(visualKey, visual))
            {
                return;
            }

            if (visualKey == GuideChefVisualKey)
            {
                if (TryResolveGuideChefHomePose(0, out var chefHome, out var chefRot, out var chefScale))
                {
                    visual.transform.SetPositionAndRotation(chefHome, chefRot);
                    visual.transform.localScale = ResolveGuideStaffVisualScale(visualKey, chefScale);
                }

                return;
            }

            if (visualKey == GuideShopkeeperVisualKey)
            {
                visual.transform.position = ResolveGuideShopkeeperHomePosition();
                visual.transform.rotation = ResolveGuideShopkeeperHomeRotation();
                visual.transform.localScale = ResolveGuideStaffVisualScale(
                    visualKey,
                    ResolveGuideShopkeeperHomeScale());
                return;
            }

            if (visualKey == GuideWaiterVisualKey
                && TryResolveGuideWaiterHomePose(0, out var waiterHome, out var waiterRot, out var waiterScale))
            {
                visual.transform.SetPositionAndRotation(waiterHome, waiterRot);
                visual.transform.localScale = ResolveGuideStaffVisualScale(visualKey, waiterScale);
                return;
            }

            visual.transform.position = marker.position;
            visual.transform.rotation = marker.rotation;
            visual.transform.localScale = ResolveGuideStaffVisualScale(visualKey, marker.lossyScale);
        }

        /// <summary>
        /// 根据标记点和序号计算员工目标点。
        /// 厨师对齐场景标记；掌柜用固定世界坐标；小二与雇佣挂点「小二雇佣N」一致。
        /// </summary>
        /// <param name="visualKey">员工表现键。</param>
        /// <param name="marker">标记点。</param>
        /// <param name="index">员工序号（从 0 开始）。</param>
        /// <returns>世界坐标目标点。</returns>
        private static Vector3 ResolveGuideStaffMarkerPosition(string visualKey, Transform marker, int index)
        {
            if (visualKey == GuideChefVisualKey)
            {
                return ResolveGuideChefHomePosition(index);
            }

            if (visualKey == GuideShopkeeperVisualKey)
            {
                return ResolveGuideShopkeeperHomePosition();
            }

            if (visualKey == GuideWaiterVisualKey
                && TryResolveGuideWaiterHomePose(index, out var waiterHome, out _, out _))
            {
                return waiterHome;
            }

            if (marker == null)
            {
                return Vector3.zero;
            }

            if (index <= 0)
            {
                return marker.position;
            }

            var stackOffset = GetGuideStaffStackOffset(visualKey, index);
            return marker.position + marker.right * stackOffset.x + marker.up * stackOffset.y + marker.forward * stackOffset.z;
        }

        /// <summary>
        /// 获取或创建指定员工的引导表现。
        /// </summary>
        /// <param name="visualKey">用于区分员工表现的键。</param>
        /// <param name="role">员工角色。</param>
        /// <param name="preferredStaffId">员工配置编号。</param>
        /// <returns>创建成功时返回员工表现对象。</returns>
        private GameObject GetOrCreateGuideStaffVisual(string visualKey, StaffRole role, int preferredStaffId)
        {
            if (guideStaffVisuals.TryGetValue(visualKey, out var existingVisual) && existingVisual != null)
            {
                BindGuideStaffId(existingVisual, role, preferredStaffId);
                RegisterGuideStaffVisualInGroup(visualKey, existingVisual);
                return existingVisual;
            }

            var staffPrefab = ResolveGuideStaffPrefab(visualKey, role);
            if (staffPrefab == null)
            {
                return null;
            }

            var visual = Instantiate(staffPrefab);
            EnsureGuideStaffCharacterComponent(visual, role, preferredStaffId);
            visual.name = $"{visualKey}_GuideVisual";
            foreach (var agent in visual.GetComponentsInChildren<NavMeshAgent>(true))
            {
                agent.enabled = false;
            }

            guideStaffVisuals[visualKey] = visual;
            RegisterGuideStaffVisualInGroup(visualKey, visual);
            return visual;
        }

        /// <summary>
        /// 销毁指定角色的引导员工表现。
        /// </summary>
        /// <param name="visualKey">用于区分员工表现的键。</param>
        private void DestroyGuideStaffVisual(string visualKey)
        {
            DestroyGuideStaffVisuals(visualKey);
        }

        /// <summary>
        /// 根据锚点和偏移同步员工表现位置。
        /// </summary>
        /// <param name="visual">参数值。</param>
        /// <param name="anchor">参数值。</param>
        /// <param name="localOffset">参数值。</param>
        /// <param name="extraYawDegrees">在锚点朝向基础上额外旋转的角度。</param>
        private static void UpdateGuideStaffTransform(Transform visual, Transform anchor, Vector3 localOffset, float extraYawDegrees)
        {
            if (visual == null || anchor == null)
            {
                return;
            }

            var worldOffset = anchor.right * localOffset.x + anchor.up * localOffset.y + anchor.forward * localOffset.z;
            visual.position = anchor.position + worldOffset;
            visual.rotation = Quaternion.LookRotation(-anchor.forward, Vector3.up) * Quaternion.Euler(0f, extraYawDegrees, 0f);
        }

        /// <summary>
        /// 根据员工类型修正模型缩放，小二略微缩小以贴合当前场景比例。
        /// </summary>
        /// <param name="visualKey">用于区分员工表现的键。</param>
        /// <param name="sourceScale">场景标记位或锚点提供的原始缩放。</param>
        /// <returns>应用角色修正后的缩放。</returns>
        private static Vector3 ResolveGuideStaffVisualScale(string visualKey, Vector3 sourceScale)
        {
            return visualKey == GuideWaiterVisualKey ? sourceScale * WaiterVisualScaleMultiplier : sourceScale;
        }

        /// <summary>
        /// 判断锚点附近是否已经有真实员工。
        /// </summary>
        /// <param name="matchKeyword">需要匹配的员工根节点关键字。</param>
        /// <param name="anchor">参数值。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private static bool HasVisibleRuntimeStaffNearAnchor(string matchKeyword, Transform anchor)
        {
            if (anchor == null || string.IsNullOrEmpty(matchKeyword))
            {
                return false;
            }

            var scene = anchor.gameObject.scene;
            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || renderer.gameObject.scene != scene || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var targetTransform = renderer.transform.root != null ? renderer.transform.root : renderer.transform;
                var targetName = targetTransform.name;
                if (targetName.Contains("GuideVisual") || !targetName.Contains(matchKeyword))
                {
                    continue;
                }

                if (Vector3.Distance(targetTransform.position, anchor.position) > 2.2f)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// 根据引导员工类型获取场景里用于去重的名称关键字。
        /// </summary>
        /// <param name="visualKey">员工表现键。</param>
        /// <param name="role">员工角色。</param>
        /// <returns>可用于匹配场景模型的名称关键字。</returns>
        private static string GetStaffNameKeyword(string visualKey, StaffRole role)
        {
            return role == StaffRole.Chef ? "Chef" : visualKey;
        }

        /// <summary>
        /// 按职位固定复用预制体，不跟招聘表 Visual / 本次 hiredStaffId 混用。
        /// 掌柜 → WaiterF01（SO id=1），厨师 → Chef03（SO id=4），小二 → Waiter03（SO id=5）。
        /// 预制体在 Assets/Res/Prefabs，需经 SO_Staff 引用加载，不能走 Resources.Load。
        /// </summary>
        private static GameObject ResolveGuideStaffPrefab(string visualKey, StaffRole role)
        {
            var fixedStaffId = visualKey == GuideShopkeeperVisualKey
                ? 1
                : visualKey == GuideChefVisualKey || role == StaffRole.Chef
                    ? 4
                    : 5;
            var lookupRole = visualKey == GuideChefVisualKey || role == StaffRole.Chef
                ? StaffRole.Chef
                : StaffRole.Waiter;

            var prefab = ResolveSoStaffPrefab(lookupRole, fixedStaffId);
            if (prefab != null)
            {
                return prefab;
            }

            Debug.LogWarning(
                $"[TavernSceneManager] Missing fixed staff prefab visualKey={visualKey} staffId={fixedStaffId}");
            return null;
        }

        private static GameObject ResolveSoStaffPrefab(StaffRole role, int preferredStaffId)
        {
            var allStaff = SO_Staff.GetAll();
            SO_Staff fallback = null;
            for (var index = 0; index < allStaff.Count; index++)
            {
                var staff = allStaff[index];
                if (staff == null || staff.role != role)
                {
                    continue;
                }

                fallback ??= staff;
                if (!int.TryParse(staff.staffId, out var numericStaffId) || numericStaffId != preferredStaffId)
                {
                    continue;
                }

                var preferredLevel = staff.GetLevelConfig(1);
                if (preferredLevel?.staffPrefab != null)
                {
                    return preferredLevel.staffPrefab;
                }
            }

            var fallbackLevel = fallback?.GetLevelConfig(1);
            return fallbackLevel?.staffPrefab;
        }

        /// <summary>
        /// 初始化员工角色组件并绑定 StaffId。
        /// </summary>
        private void EnsureGuideStaffCharacterComponent(GameObject visual, StaffRole role, int staffId = 0)
        {
            if (visual == null)
            {
                return;
            }

            ClearStaffHeadOrderBubbleNodes(visual);
            StaffSceneClickUtility.EnsureClickCollider(visual);

            switch (role)
            {
                case StaffRole.Waiter:
                {
                    var waiter = visual.GetComponent<WaiterCharacter>();
                    if (waiter != null)
                    {
                        waiter.InitializeWaiter(this, this);
                        if (staffId > 0)
                        {
                            waiter.BindStaffId(staffId);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Waiter prefab missing {nameof(WaiterCharacter)}: {visual.name}");
                    }

                    EnsureWaiterAnimationReceiver(visual);
                    break;
                }
                case StaffRole.Chef:
                {
                    var chef = visual.GetComponent<ChefCharacter>();
                    if (chef != null)
                    {
                        chef.InitializeChef(this, this);
                        if (staffId > 0)
                        {
                            chef.BindStaffId(staffId);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Chef prefab missing {nameof(ChefCharacter)}: {visual.name}");
                    }

                    break;
                }
            }
        }

        private static void BindGuideStaffId(GameObject visual, StaffRole role, int staffId)
        {
            if (visual == null || staffId <= 0)
            {
                return;
            }

            if (role == StaffRole.Chef)
            {
                visual.GetComponent<ChefCharacter>()?.BindStaffId(staffId);
                return;
            }

            visual.GetComponent<WaiterCharacter>()?.BindStaffId(staffId);
        }

        /// <summary>
        /// 在帧末同步跟随 界面 和场景表现位置。
        /// </summary>
        private void LateUpdate()
        {
            CleanupGuideStaffVisuals();

            if (SceneCamera == null)
            {
                return;
            }

            foreach (var button in guideWorldButtons)
            {
                if (button == null || button.rectTransform == null || button.target == null || !button.rectTransform.gameObject.activeSelf)
                {
                    continue;
                }

                UpdateScreenSpaceElement(button.rectTransform, button.target.position + button.worldOffset);
            }

            foreach (var label in guideWorldLabels)
            {
                if (label == null || label.rectTransform == null || label.target == null || !label.rectTransform.gameObject.activeSelf)
                {
                    continue;
                }

                UpdateScreenSpaceElement(label.rectTransform, label.target.position + label.worldOffset);
            }
        }

        /// <summary>
        /// 把世界坐标投影到屏幕空间 界面。
        /// </summary>
        /// <param name="rectTransform">参数值。</param>
        /// <param name="worldPosition">坐标。</param>
        private void UpdateScreenSpaceElement(RectTransform rectTransform, Vector3 worldPosition)
        {
            if (rectTransform == null || SceneCamera == null)
            {
                return;
            }

            var screenPosition = SceneCamera.WorldToScreenPoint(worldPosition);
            var isVisible = screenPosition.z > 0f;
            if (rectTransform.gameObject.activeSelf != isVisible)
            {
                rectTransform.gameObject.SetActive(isVisible);
            }

            if (!isVisible)
            {
                return;
            }

            rectTransform.position = screenPosition;
            rectTransform.rotation = Quaternion.identity;
            rectTransform.localScale = ResolveScreenElementScale(rectTransform.transform);
        }

        /// <summary>
        /// 根据当前跟随界面类型返回应该使用的屏幕缩放。
        /// </summary>
        /// <param name="elementTransform">界面节点。</param>
        /// <returns>最终缩放。</returns>
        private Vector3 ResolveScreenElementScale(Transform elementTransform)
        {
            if (elementTransform == null)
            {
                return Vector3.one;
            }

            for (var index = 0; index < guideWorldLabels.Count; index++)
            {
                var label = guideWorldLabels[index];
                if (label?.rectTransform == elementTransform)
                {
                    return label.scale;
                }
            }

            for (var index = 0; index < guideWorldButtons.Count; index++)
            {
                var button = guideWorldButtons[index];
                if (button?.rectTransform == elementTransform)
                {
                    return button.scale;
                }
            }

            return Vector3.one;
        }

        /// <summary>
        /// 清理真实员工出现后残留的引导员工表现。
        /// </summary>
        private void CleanupGuideStaffVisuals()
        {
            if (guideCounterObject != null && HasVisibleRuntimeStaffNearAnchor(GuideShopkeeperVisualKey, guideCounterObject.transform))
            {
                DestroyGuideStaffVisuals(GuideShopkeeperVisualKey);
                DestroyOrphanGuideVisual($"{GuideShopkeeperVisualKey}_GuideVisual");
            }

            if (guideStoveObject != null && HasVisibleRuntimeStaffNearAnchor("Chef", guideStoveObject.transform))
            {
                DestroyGuideStaffVisuals(GuideChefVisualKey);
                DestroyOrphanGuideVisual($"{GuideChefVisualKey}_GuideVisual");
            }

            if (customerEntryPoint != null && HasVisibleRuntimeStaffNearAnchor(GuideWaiterVisualKey, customerEntryPoint))
            {
                DestroyGuideStaffVisuals(GuideWaiterVisualKey);
                DestroyOrphanGuideVisual($"{GuideWaiterVisualKey}_GuideVisual");
            }
        }

        /// <summary>
        /// 隐藏场景里预摆放的员工模型，避免招聘前就出现人物。
        /// </summary>
        private static void HidePreRecruitSceneStaffModels()
        {
            SetPreplacedStaffModelVisible(GuideChefMarkerName, false);
            SetPreplacedStaffModelVisible(GuideShopkeeperMarkerName, false);
            SetPreplacedStaffModelVisible(GuideWaiterMarkerName, false);
            SetPreplacedStaffModelVisible("Chef3", false);
            SetPreplacedStaffModelVisible("WaiterF1", false);
            SetPreplacedStaffModelVisible("WaiterF1_1", false);
        }

        /// <summary>
        /// 按根节点名称设置预摆放员工的显隐。
        /// </summary>
        /// <param name="objectName">场景里预摆放员工的节点名。</param>
        /// <param name="visible">是否显示。</param>
        private static void SetPreplacedStaffModelVisible(string objectName, bool visible)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return;
            }

            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var current in transforms)
            {
                if (current == null || current.name != objectName)
                {
                    continue;
                }

                current.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 销毁未被字典追踪的孤立引导表现。
        /// </summary>
        /// <param name="objectName">名称。</param>
        private static void DestroyOrphanGuideVisual(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return;
            }

            var orphan = GameObject.Find(objectName);
            if (orphan == null)
            {
                return;
            }

            Destroy(orphan);
        }

        #endregion
    }
}
