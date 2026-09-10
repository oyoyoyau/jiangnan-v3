using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.Model;
using JN.Client.UI;
using QFramework;
using System.Collections;
using UnityEngine;
using cfg;

namespace JN.Client.Scene
{
    public partial class TavernSceneManager
    {
        #region Guide Actions

        /// <summary>
        /// 处理购买柜台并播放搬运表现。
        /// </summary>
        private void HandleBuyCounter()
        {
            if (DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern)
            {
                HudOverlayService.ShowFloatingWarning("访客模式下不可新增设施");
                return;
            }

            // 已购买：静默忽略，不弹 tips。
            if (DataManager.Instance != null
                && DataManager.Instance.GameplayGuideData != null
                && DataManager.Instance.GameplayGuideData.purchasedCounter)
            {
                return;
            }

            var cost = GetGuideFacilityCostByKey("counter");
            if (DataManager.Instance.PlayerData.coinNum < cost)
            {
                HudOverlayService.ShowFloatingWarning($"铜钱不足，购买掌柜桌需要 {cost}");
                return;
            }

            guideCounterDeliveryPending = true;
            DataManager.Instance.MarkGuideBuildPlacementPending("counter", true);
            HudOverlayService.SetPendingPrestigeFlySource(
                guideCounterBuildBase != null ? guideCounterBuildBase.transform
                : guideCounterObject != null ? guideCounterObject.transform : null);
            if (!DataManager.Instance.TryPurchaseGuideCounter(out var message))
            {
                guideCounterDeliveryPending = false;
                DataManager.Instance.MarkGuideBuildPlacementPending("counter", false);
                HudOverlayService.SetPendingPrestigeFlySource(null);
                RefreshGuideWorldState();
                Signals.Get<GameplayGuideProgressSignal>().Dispatch();
                if (!string.IsNullOrWhiteSpace(message) && !message.EndsWith("已购买"))
                {
                    HudOverlayService.ShowFloatingWarning(message);
                }
                return;
            }

            GameAudioManager.PlayTableMove();

            if (!TryPlayGuideDeliveryEffect(
                    guideCounterObject != null ? guideCounterObject.transform : null,
                    guideCounterBuildBase != null ? guideCounterBuildBase.transform : null,
                    ResolveGuideCarrier(GuideCounterCarrierPrefabPath, "P_Equipment_CounterCarrier"),
                    () =>
                    {
                        guideCounterDeliveryPending = false;
                        DataManager.Instance.MarkGuideBuildPlacementPending("counter", false);
                        RefreshGuideWorldState();
                        Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
                    }))
            {
                guideCounterDeliveryPending = false;
                DataManager.Instance.MarkGuideBuildPlacementPending("counter", false);
            }

            RefreshGuideWorldState();
            Signals.Get<GameplayGuideProgressSignal>().Dispatch();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        /// <summary>
        /// 处理购买灶台并播放搬运表现。
        /// </summary>
        private void HandleBuyStove()
        {
            if (DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern)
            {
                HudOverlayService.ShowFloatingWarning("访客模式下不可新增设施");
                return;
            }

            // 已购买：静默忽略，不弹 tips。
            if (DataManager.Instance != null
                && DataManager.Instance.IsGuideKitchenItemPurchased("stove"))
            {
                return;
            }

            var cost = GetGuideFacilityCostByKey("stove");
            if (DataManager.Instance.PlayerData.coinNum < cost)
            {
                HudOverlayService.ShowFloatingWarning($"铜钱不足，购买灶台需要 {cost}");
                return;
            }

            guideStoveDeliveryPending = true;
            DataManager.Instance.MarkGuideBuildPlacementPending("stove", true);
            HudOverlayService.SetPendingPrestigeFlySource(
                guideStoveBuildBase != null ? guideStoveBuildBase.transform
                : guideStoveObject != null ? guideStoveObject.transform : null);
            if (!DataManager.Instance.TryPurchaseGuideStove(out var message))
            {
                guideStoveDeliveryPending = false;
                DataManager.Instance.MarkGuideBuildPlacementPending("stove", false);
                HudOverlayService.SetPendingPrestigeFlySource(null);
                RefreshGuideWorldState();
                Signals.Get<GameplayGuideProgressSignal>().Dispatch();
                if (!string.IsNullOrWhiteSpace(message) && !message.EndsWith("已购买"))
                {
                    HudOverlayService.ShowFloatingWarning(message);
                }
                return;
            }

            GameAudioManager.PlayTableMove();

            if (!TryPlayGuideDeliveryEffect(
                    guideStoveObject != null ? guideStoveObject.transform : null,
                    guideStoveBuildBase != null ? guideStoveBuildBase.transform : null,
                    ResolveGuideCarrier(GuideStoveCarrierPrefabPath, "P_Equipment_StoveCarrier"),
                    () =>
                    {
                        guideStoveDeliveryPending = false;
                        DataManager.Instance.MarkGuideBuildPlacementPending("stove", false);
                        RefreshGuideWorldState();
                        Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
                    }))
            {
                guideStoveDeliveryPending = false;
                DataManager.Instance.MarkGuideBuildPlacementPending("stove", false);
            }

            RefreshGuideWorldState();
            Signals.Get<GameplayGuideProgressSignal>().Dispatch();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        private void HandleBuyKitchenItem(string itemKey)
        {
            if (DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern)
            {
                HudOverlayService.ShowFloatingWarning("访客模式下不可新增设施");
                return;
            }

            // 已购买：静默忽略，不弹 tips。
            if (DataManager.Instance != null && DataManager.Instance.IsGuideKitchenItemPurchased(itemKey))
            {
                return;
            }

            var anchor = guideKitchenAnchors.Find(current => current != null && current.itemKey == itemKey);
            var itemDisplayName = anchor != null && !string.IsNullOrWhiteSpace(anchor.displayName)
                ? anchor.displayName
                : "厨房物件";
            var cost = GetGuideFacilityCostByKey(itemKey);
            if (DataManager.Instance.PlayerData.coinNum < cost)
            {
                HudOverlayService.ShowFloatingWarning($"铜钱不足，购买{itemDisplayName}需要{cost}");
                return;
            }

            // 轿子/楼梯：无搬运预制体，购买后直接建成不透明。
            var skipDelivery = anchor == null
                               || string.IsNullOrWhiteSpace(anchor.carrierPrefabPath)
                               || itemKey == "stairs"
                               || itemKey == "jiaozi";
            var waitForPlacement = !skipDelivery && anchor.sceneObject != null;
            if (waitForPlacement)
            {
                guidePendingKitchenItems.Add(itemKey);
                DataManager.Instance.MarkGuideBuildPlacementPending(itemKey, true);
            }

            HudOverlayService.SetPendingPrestigeFlySource(
                anchor != null
                    ? (anchor.buildBase != null ? anchor.buildBase.transform
                        : anchor.sceneObject != null ? anchor.sceneObject.transform : null)
                    : null);
            if (!DataManager.Instance.TryPurchaseGuideKitchenItem(itemKey, out var message))
            {
                if (waitForPlacement)
                {
                    guidePendingKitchenItems.Remove(itemKey);
                    DataManager.Instance.MarkGuideBuildPlacementPending(itemKey, false);
                }

                HudOverlayService.SetPendingPrestigeFlySource(null);

                RefreshGuideWorldState();
                Signals.Get<GameplayGuideProgressSignal>().Dispatch();
                if (!string.IsNullOrWhiteSpace(message) && !message.EndsWith("已购买"))
                {
                    HudOverlayService.ShowFloatingWarning(message);
                }
                return;
            }

            if (waitForPlacement)
            {
                GameAudioManager.PlayTableMove();
                if (!TryPlayGuideDeliveryEffect(
                        anchor.buildBase != null ? anchor.buildBase.transform : anchor.sceneObject.transform,
                        LoadGuideCarrierPrefab(anchor.carrierPrefabPath),
                        () =>
                        {
                            guidePendingKitchenItems.Remove(itemKey);
                            DataManager.Instance.MarkGuideBuildPlacementPending(itemKey, false);
                            RefreshGuideWorldState();
                            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
                        }))
                {
                    guidePendingKitchenItems.Remove(itemKey);
                    DataManager.Instance.MarkGuideBuildPlacementPending(itemKey, false);
                }
            }
            else if (anchor?.sceneObject != null)
            {
                // 无搬运：立刻对模型套建成不透明（轿子不改轿夫子节点）。
                var includeChildren = itemKey != "jiaozi";
                FacilityBuildVisualUtility.ApplyBuiltState(anchor.sceneObject, includeChildren);
            }

            // 楼梯建成 → 雅间；轿子若仍走购买入口则弹拉客（正常由 HireStaff_enter 结束后解锁）。
            if (itemKey == "stairs")
            {
                HudOverlayService.ShowNewFunctionUnlock(NewFunctionUnlockType.Yajian);
            }
            else if (itemKey == "jiaozi")
            {
                HudOverlayService.ShowNewFunctionUnlock(NewFunctionUnlockType.DrumUp);
            }

            RefreshGuideWorldState();
            Signals.Get<GameplayGuideProgressSignal>().Dispatch();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        /// <summary>
        /// 处理招聘掌柜。
        /// </summary>
        private void HandleHireShopkeeper()
        {
            if (HasGuideBuildPlacementPending())
            {
                return;
            }

            if (DataManager.Instance != null && !DataManager.Instance.CanHireMoreGuideShopkeeper())
            {
                return;
            }

            HudOverlayService.ShowStaffHireSelectPanel(StaffHireSelectRole.Shopkeeper, showRoleTabs: true);
        }

        /// <summary>
        /// 处理招聘厨师：打开读表三选一面板。
        /// </summary>
        private void HandleHireChef()
        {
            if (HasGuideBuildPlacementPending())
            {
                return;
            }

            HudOverlayService.ShowStaffHireSelectPanel(StaffHireSelectRole.Chef, showRoleTabs: true);
        }

        /// <summary>
        /// 处理招聘小二：打开读表三选一面板。
        /// </summary>
        private void HandleHireWaiter()
        {
            if (HasGuideBuildPlacementPending())
            {
                return;
            }

            HudOverlayService.ShowStaffHireSelectPanel(StaffHireSelectRole.Waiter, showRoleTabs: true);
        }

        /// <summary>
        /// 供引导 HUD 等外部入口复用：先弹确认 UI 再招聘。
        /// </summary>
        public void RequestGuideHireShopkeeper() => HandleHireShopkeeper();

        /// <summary>
        /// 供引导 HUD 等外部入口复用：打开厨师三选一面板。
        /// </summary>
        public void RequestGuideHireChef() => HandleHireChef();

        /// <summary>
        /// 供引导 HUD 等外部入口复用：打开小二三选一面板。
        /// </summary>
        public void RequestGuideHireWaiter() => HandleHireWaiter();

        /// <summary>
        /// 打开招聘人才确认界面。
        /// </summary>
        /// <param name="displayName">展示名称。</param>
        /// <param name="roleText">人员类型。</param>
        /// <param name="staffId">员工编号。</param>
        /// <param name="role">员工角色。</param>
        /// <param name="onConfirm">确认招聘回调。</param>
        private void OpenRecruitPanel(string displayName, string roleText, int staffId, StaffRole role, System.Action onConfirm)
        {
            var staff = DataManager.Instance.GetGuideStaffConfig(staffId, role);
            var cost = DataManager.Instance.GetGuideStaffHireCost(staffId, role);
            HudOverlayService.ShowRecruitPanel(staff != null ? staff.displayName : displayName, roleText, staff != null ? staff.icon : null, cost, onConfirm);
        }

        /// <summary>
        /// 确认招聘掌柜。
        /// </summary>
        private void ConfirmHireShopkeeper()
        {
            if (!DataManager.Instance.TryHireGuideShopkeeper(out var message))
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    HudOverlayService.ShowFloatingWarning(message);
                }

                return;
            }

            RefreshGuideWorldButtons(DataManager.Instance.GameplayGuideData);
            GameAudioManager.PlayRecruitShopkeeper();
            const int shopkeeperStaffId = 1;
            StartCoroutine(GuideStaffEnterRoutine(
                GuideShopkeeperVisualKey,
                GuideShopkeeperMarkerName,
                "WaiterF1",
                StaffRole.Waiter,
                shopkeeperStaffId));
            Signals.Get<GameplayGuideProgressSignal>().Dispatch();
        }

        /// <summary>
        /// 确认招聘厨师。
        /// </summary>
        private void ConfirmHireChef()
        {
            if (!DataManager.Instance.TryHireGuideChef(out var message))
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    HudOverlayService.ShowFloatingWarning(message);
                }

                return;
            }

            RefreshGuideWorldButtons(DataManager.Instance.GameplayGuideData);
            GameAudioManager.PlayRecruitChef();
            const int chefStaffId = 4;
            StartCoroutine(GuideStaffEnterRoutine(
                GuideChefVisualKey,
                GuideChefMarkerName,
                "Chef3",
                StaffRole.Chef,
                chefStaffId));
            Signals.Get<GameplayGuideProgressSignal>().Dispatch();
        }

        /// <summary>
        /// 确认招聘小二。
        /// </summary>
        private void ConfirmHireWaiter()
        {
            if (!DataManager.Instance.TryHireGuideWaiter(out var message))
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    HudOverlayService.ShowFloatingWarning(message);
                }

                return;
            }

            RefreshGuideWorldButtons(DataManager.Instance.GameplayGuideData);
            GameAudioManager.PlayRecruitWaiter();
            const int waiterStaffId = 5;
            StartCoroutine(GuideStaffEnterRoutine(
                GuideWaiterVisualKey,
                GuideWaiterMarkerName,
                "WaiterF1_1",
                StaffRole.Waiter,
                waiterStaffId));
            Signals.Get<GameplayGuideProgressSignal>().Dispatch();
        }

        /// <summary>
        /// 从底部员工按钮招聘厨师后，播放厨师从门口入场到站位的表现。
        /// </summary>
        public void PlayGuideChefEnterFromBottomRecruit(int hiredStaffId = 0)
        {
            RefreshGuideWorldButtons(DataManager.Instance.GameplayGuideData);
            const int defaultChefStaffId = 4;
            var staffId = hiredStaffId > 0
                ? hiredStaffId
                : ResolveLatestOwnedStaffId(StaffPosition.Chef, defaultChefStaffId);
            StartCoroutine(GuideStaffEnterRoutine(
                GuideChefVisualKey,
                GuideChefMarkerName,
                "Chef3",
                StaffRole.Chef,
                staffId,
                true));
        }

        /// <summary>
        /// 从底部员工按钮招聘小二后，播放小二从门口入场到站位的表现。
        /// </summary>
        public void PlayGuideWaiterEnterFromBottomRecruit(int hiredStaffId = 0)
        {
            RefreshGuideWorldButtons(DataManager.Instance.GameplayGuideData);
            const int defaultWaiterStaffId = 5;
            var staffId = hiredStaffId > 0
                ? hiredStaffId
                : ResolveLatestOwnedStaffId(StaffPosition.Waiter, defaultWaiterStaffId);
            StartCoroutine(GuideStaffEnterRoutine(
                GuideWaiterVisualKey,
                GuideWaiterMarkerName,
                "WaiterF1_1",
                StaffRole.Waiter,
                staffId,
                true));
        }

        /// <summary>
        /// 从通用招聘面板招聘掌柜后，播放掌柜从门口入场到站位的表现。
        /// </summary>
        public void PlayGuideShopkeeperEnterFromBottomRecruit(int hiredStaffId = 0)
        {
            RefreshGuideWorldButtons(DataManager.Instance.GameplayGuideData);
            const int defaultShopkeeperStaffId = 1;
            var staffId = hiredStaffId > 0
                ? hiredStaffId
                : ResolveLatestOwnedStaffId(StaffPosition.Shopkeeper, defaultShopkeeperStaffId);
            StartCoroutine(GuideStaffEnterRoutine(
                GuideShopkeeperVisualKey,
                GuideShopkeeperMarkerName,
                "WaiterF1",
                StaffRole.Waiter,
                staffId));
        }

        private static int ResolveLatestOwnedStaffId(StaffPosition position, int fallbackStaffId)
        {
            var ids = DataManager.Instance != null
                ? DataManager.Instance.GetOwnedStaffIdsByPosition(position, includeTemporary: position == StaffPosition.Waiter)
                : null;
            if (ids == null || ids.Count <= 0)
            {
                return fallbackStaffId;
            }

            return ids[ids.Count - 1];
        }

        /// <summary>
        /// 营业中招聘临时小二后，复用小二入场表现并接入当前服务调度。
        /// </summary>
        public void PlayTemporaryWaiterEnterFromRecruit()
        {
            PlayGuideWaiterEnterFromBottomRecruit();
        }

        /// <summary>
        /// 招聘完成后让人才从门口走到站位。
        /// </summary>
        /// <param name="visualKey">员工表现键。</param>
        /// <param name="markerName">目标站位名称。</param>
        /// <param name="legacyMarkerName">兼容旧站位名称。</param>
        /// <param name="role">员工角色。</param>
        /// <param name="preferredStaffId">员工配置编号。</param>
        /// <returns>协程迭代器。</returns>
        private IEnumerator GuideStaffEnterRoutine(string visualKey, string markerName, string legacyMarkerName, StaffRole role, int preferredStaffId, bool forceCreateNew = false)
        {
            var marker = FindSceneTransformByName(markerName) ?? FindSceneTransformByName(legacyMarkerName);
            var canUseFixedHome = visualKey == GuideChefVisualKey
                                  || visualKey == GuideShopkeeperVisualKey
                                  || visualKey == GuideWaiterVisualKey;
            if (marker == null && !canUseFixedHome)
            {
                yield break;
            }

            // 数据先变、信号先广播：调用本协程时 EnsureGuideStaffVisualCount 已经新建好了对应的员工表现。
            // 因此 forceCreateNew=true 不再额外 Instantiate，而是接管最新一位 visual 让它从门口走进来，
            // 否则会出现 N+1 个表现，多余的那一位会被下一次 RefreshGuideWorldState 销毁，
            // 表现上就是“最新招聘的那位”停在门口/锚点不动。
            GameObject visual;
            var existingVisuals = GetGuideStaffVisuals(visualKey);
            var hasGroupedVisuals = existingVisuals != null && existingVisuals.Length > 0;
            // 厨师/小二支持多人：无论入口来自顶部确认还是底部招募，都应优先拿“最后一个”
            // （即本次新增的 visual）做入场动画，避免错误地复用第一个员工导致站位/动画错乱。
            var preferLatestVisual = visualKey == GuideChefVisualKey || visualKey == GuideWaiterVisualKey;
            if ((forceCreateNew || preferLatestVisual) && hasGroupedVisuals)
            {
                visual = existingVisuals[existingVisuals.Length - 1];
            }
            else if (forceCreateNew)
            {
                visual = CreateAdditionalGuideStaffVisual(visualKey, role, preferredStaffId);
            }
            else
            {
                visual = GetOrCreateGuideStaffVisual(visualKey, role, preferredStaffId);
            }

            if (visual == null)
            {
                yield break;
            }

            BindGuideStaffId(visual, role, preferredStaffId);

            var visuals = GetGuideStaffVisuals(visualKey);
            var visualIndex = System.Array.IndexOf(visuals, visual);
            var safeIndex = Mathf.Max(0, visualIndex);
            var targetPosition = ResolveGuideStaffMarkerPosition(visualKey, marker, safeIndex);
            var targetRotation = marker != null ? marker.rotation : Quaternion.identity;
            var scaleSource = marker != null ? marker.lossyScale : Vector3.one;

            if (visualKey == GuideChefVisualKey)
            {
                if (TryResolveGuideChefHomePose(safeIndex, out targetPosition, out targetRotation, out scaleSource))
                {
                    // 已写入 target*
                }
                else if (marker != null)
                {
                    targetPosition = marker.position + new Vector3(GuideChefStackStepWorldX * safeIndex, 0f, 0f);
                    targetRotation = marker.rotation;
                    scaleSource = marker.lossyScale;
                }
            }
            else if (visualKey == GuideShopkeeperVisualKey)
            {
                targetRotation = ResolveGuideShopkeeperHomeRotation();
                scaleSource = ResolveGuideShopkeeperHomeScale();
            }
            else if (visualKey == GuideWaiterVisualKey)
            {
                if (!TryResolveGuideWaiterHomePose(safeIndex, out targetPosition, out targetRotation, out scaleSource))
                {
                    yield break;
                }
            }

            var start = customerEntryPoint != null
                ? customerEntryPoint.position
                : marker != null ? marker.position : targetPosition;
            var navStart = start;
            var navTarget = targetPosition;
            TryGetNavMeshPosition(start, out navStart);
            TryGetNavMeshPosition(targetPosition, out navTarget);

            // 先打上“正在入场”标记，再做位置/缩放重置，避免和同帧内的 RefreshGuideWorldState 争夺位置。
            staffVisualsBeingAnimated.Add(visual);
            visual.SetActive(false);
            StopWaiterHomeReturn(visual);
            visual.transform.position = navStart;
            visual.transform.rotation = targetRotation;
            visual.transform.localScale = ResolveGuideStaffVisualScale(visualKey, scaleSource);
            visual.SetActive(true);
            var animator = visual.GetComponentInChildren<Animator>(true);
            PrepareAnimatorForMovement(animator);
            SetAnimatorSpeed(animator, WalkAnimationSpeed);

            try
            {
                yield return MoveCharacterAlongNavMesh(visual.transform, navTarget, 1.15f, false);
            }
            finally
            {
                staffVisualsBeingAnimated.Remove(visual);
            }

            visual.transform.rotation = targetRotation;
            RefreshGuideWorldState();
        }

        #endregion
    }
}
