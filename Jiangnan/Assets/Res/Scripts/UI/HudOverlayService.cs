using System;
using System.Collections;
using cfg;
using JN.Client;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.Scene;
using QFramework;
using UnityEngine;
using UnityEngine.Video;

namespace JN.Client.UI
{
    /// <summary>
    /// HUD 浮层统一入口。
    /// 固定弹层走 UIKit，世界跟随类浮层走统一运行时 HUD 容器。
    /// </summary>
    public static class HudOverlayService
    {
        /// <summary>
        /// 显示通用信息面板。
        /// </summary>
        public static void ShowInfoPanel(string title, string content)
        {
            OpenOrReplace<RuntimeInfoPanelController>(new RuntimeInfoPanelControllerData
            {
                Title = title,
                Content = content,
                OnConfirm = null
            });
        }

        /// <summary>
        /// 显示通用二次确认弹窗；确认后执行 onConfirm。
        /// </summary>
        public static void ShowConfirmBox(string title, string content, Action onConfirm)
        {
            OpenOrReplace<RuntimeInfoPanelController>(new RuntimeInfoPanelControllerData
            {
                Title = title,
                Content = content,
                OnConfirm = onConfirm
            });
        }

        /// <summary>
        /// 显示员工天赋描述弹窗（StaffTalent.desc）。
        /// </summary>
        public static void ShowStaffTalentDescPanel(Staff staff)
        {
            if (!StaffTalentConfigUtility.TryGetStaffTalentDescPopup(staff, out var title, out var content))
            {
                return;
            }

            ShowInfoPanel(title, content);
        }

        /// <summary>
        /// 显示顺序对话面板；播完关闭后执行 onComplete。
        /// </summary>
        /// <param name="dialogId">Dialog 表 dialogId，如 welcome_fushang。</param>
        /// <param name="onComplete">结束回调（空对话也会触发，避免卡流程）。</param>
        public static void ShowDialog(string dialogId, Action onComplete = null)
        {
            // 升级酒楼 / 外出揽客引导会盖住招聘面板，先关掉以免挡点击。
            if (dialogId == DialogConfigUtility.DialogIdUpdate
                || dialogId == DialogConfigUtility.DialogIdHireStaffEnter)
            {
                UIKit.ClosePanel<StaffHireSelectPanelController>();
            }

            OpenOrReplace<DialogPanelController>(new DialogPanelControllerData
            {
                DialogId = dialogId,
                OnComplete = onComplete
            });
        }

        /// <summary>
        /// 自家进店后延迟弹出引导对话（等 HUD 就绪）。
        /// </summary>
        public static IEnumerator DeferredTryShowGuideDialogsAfterEnterOwnTavern()
        {
            yield return null;
            yield return null;
            TryShowGuideFirstEnterDialog(TryShowGuideTaskDialogs);
        }

        /// <summary>
        /// 新建后首次进店：先播 enterTavern 视频，播完再出 first_enter（仅一次）。
        /// </summary>
        public static void TryShowGuideFirstEnterDialog(Action onComplete = null)
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null || dataManager.IsVisitingOtherTavern)
            {
                onComplete?.Invoke();
                return;
            }

            var guide = dataManager.GameplayGuideData;
            if (guide == null)
            {
                onComplete?.Invoke();
                return;
            }

            // 已开业过则跳过并记为已播，避免旧档反复进店弹。
            if (guide.dialogFirstEnterShown || dataManager.GetBusinessOpenCount() > 0)
            {
                if (!guide.dialogFirstEnterShown)
                {
                    guide.dialogFirstEnterShown = true;
                    dataManager.SaveGame();
                }

                onComplete?.Invoke();
                return;
            }

            guide.dialogFirstEnterShown = true;
            dataManager.SaveGame();

            var clip = GameplayResourceStore.LoadAsset<VideoClip>(FirstEnterTavernVideoPath);
            if (clip == null)
            {
                Debug.LogWarning($"[HudOverlay] 缺少进店视频：{FirstEnterTavernVideoPath}，直接播 first_enter。");
                ShowDialog(DialogConfigUtility.DialogIdFirstEnter, onComplete);
                return;
            }

            VideoWindowController.Show(
                clip,
                () => ShowDialog(DialogConfigUtility.DialogIdFirstEnter, onComplete),
                pauseOnLastFrame: false);
        }

        private const string FirstEnterTavernVideoPath = "Assets/Res/Resources/Videos/enterTavern.mp4";

        /// <summary>
        /// 当前主线任务变化时播对应对话（各一次；有对话面板打开时跳过）。
        /// employ=招募伙计，update=升级酒楼，opening=新店开张，HireStaff_enter=外出揽客。
        /// </summary>
        public static void TryShowGuideTaskDialogs()
        {
            if (UIKit.GetPanel<DialogPanelController>() != null)
            {
                return;
            }

            var dataManager = DataManager.Instance;
            if (dataManager == null || dataManager.IsVisitingOtherTavern)
            {
                return;
            }

            var guide = dataManager.GameplayGuideData;
            if (guide == null)
            {
                return;
            }

            var currentTask = dataManager.GetCurrentAchievementTask();
            if (!guide.dialogEmployShown
                && currentTask != null
                && currentTask.AchievementType == AchievementType.EmployFellow)
            {
                guide.dialogEmployShown = true;
                dataManager.SaveGame();
                ShowDialog(DialogConfigUtility.DialogIdEmploy, TryShowGuideTaskDialogs);
                return;
            }

            if (!guide.dialogUpdateShown
                && currentTask != null
                && currentTask.Id == AchievementConfigUtility.MainlineUpgradeTavernId)
            {
                guide.dialogUpdateShown = true;
                dataManager.SaveGame();
                ShowDialog(DialogConfigUtility.DialogIdUpdate, TryShowGuideTaskDialogs);
                return;
            }

            if (!guide.dialogOpeningShown
                && currentTask != null
                && currentTask.Id == AchievementConfigUtility.MainlineOpenNewShopId)
            {
                guide.dialogOpeningShown = true;
                dataManager.SaveGame();
                ShowDialog(DialogConfigUtility.DialogIdOpening, TryShowGuideTaskDialogs);
                return;
            }

            if (!guide.dialogHireStaffEnterShown
                && currentTask != null
                && currentTask.AchievementType == AchievementType.Solicit)
            {
                // 先记已播，避免重复弹；轿子模型等对话结束后再自动解锁。
                guide.dialogHireStaffEnterShown = true;
                dataManager.SaveGame();
                ShowDialog(DialogConfigUtility.DialogIdHireStaffEnter, () =>
                {
                    dataManager.TryGrantJiaoziUnlockedByProgress();
                    TavernSceneManager.ApplyHireStaffEnterCameraX();
                    ShowNewFunctionUnlock(NewFunctionUnlockType.DrumUp);
                });
            }
        }

        /// <summary>
        /// 首次上二楼后延迟弹出 UnlockMenu 对话（等 HUD 就绪）。
        /// </summary>
        public static IEnumerator DeferredTryShowUnlockMenuDialog()
        {
            yield return null;
            yield return null;
            TryShowUnlockMenuDialog();
        }

        /// <summary>
        /// 首次上二楼：播 UnlockMenu，结束后解锁底部菜单按钮。
        /// </summary>
        public static void TryShowUnlockMenuDialog(Action onComplete = null)
        {
            if (UIKit.GetPanel<DialogPanelController>() != null)
            {
                onComplete?.Invoke();
                return;
            }

            var dataManager = DataManager.Instance;
            if (dataManager == null || dataManager.IsVisitingOtherTavern)
            {
                onComplete?.Invoke();
                return;
            }

            var guide = dataManager.GameplayGuideData;
            if (guide == null || guide.dialogUnlockMenuShown || dataManager.IsTavernMenuEntryUnlocked())
            {
                onComplete?.Invoke();
                return;
            }

            ShowDialog(DialogConfigUtility.DialogIdUnlockMenu, () =>
            {
                dataManager.UnlockTavernMenuEntry();
                // 二楼首次对话结束：弹出菜单新功能拍脸。
                ShowNewFunctionUnlock(NewFunctionUnlockType.Menu);
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// 显示新功能开启拍脸弹窗（1.5 秒自动关闭）。
        /// </summary>
        public static void ShowNewFunctionUnlock(NewFunctionUnlockType functionType, Action onClosed = null)
        {
            OpenOrReplace<NewFunctionUnlockPanelController>(
                new NewFunctionUnlockPanelControllerData
                {
                    FunctionType = functionType,
                    OnClosed = onClosed
                },
                "NewFunctionUnlockPanel");
        }

        /// <summary>
        /// 显示短时警告提示。
        /// </summary>
        public static void ShowFloatingWarning(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            OpenOrReplace<FloatingWarningPanelController>(new FloatingWarningPanelControllerData
            {
                Content = content
            });
        }

        /// <summary>
        /// 菜单切换成功后的短时提示（翻牌关闭弹窗后弹出，3 秒后消失）。
        /// </summary>
        public static void ShowSwitchMenuTips(bool vipMenu)
        {
            OpenOrReplace<SwitchMenuTipsPanelController>(new SwitchMenuTipsPanelControllerData
            {
                VipMenu = vipMenu
            });
        }

        /// <summary>
        /// 高峰期到来提示（升星触发高峰时弹出）。
        /// </summary>
        public static void ShowPeakTimeWarning(string content = null)
        {
            OpenOrReplace<PeakTimeWarningPanelController>(new PeakTimeWarningPanelControllerData
            {
                Content = string.IsNullOrWhiteSpace(content) ? "限时客流+200%" : content
            });
        }

        /// <summary>
        /// 升星触发的高峰提示：等恭喜升级弹窗关闭后再弹出（开业高峰仍立刻 ShowPeakTimeWarning）。
        /// </summary>
        public static void RequestPeakTimeWarningAfterUpgradePopClosed()
        {
            pendingPeakTimeWarningAfterUpgradePop = true;
        }

        private static bool pendingPeakTimeWarningAfterUpgradePop;

        private static void FlushPendingPeakTimeWarningAfterUpgradePop()
        {
            if (!pendingPeakTimeWarningAfterUpgradePop)
            {
                return;
            }

            pendingPeakTimeWarningAfterUpgradePop = false;
            Scene.TavernSceneManager.Instance?.RefreshGuideWorldState();
            ShowPeakTimeWarning();
        }

        /// <summary>
        /// 打开员工信息面板。
        /// </summary>
        public static void ShowStaffInfoPanel(int focusStaffId = 0)
        {
            OpenOrReplace<StaffInfoPanelController>(new StaffInfoPanelControllerData
            {
                FocusStaffId = focusStaffId
            });
        }

        /// <summary>
        /// 打开员工招聘面板（小二/厨师固定槽，掌柜随机一人）。
        /// </summary>
        public static void ShowStaffHireSelectPanel(
            StaffHireSelectRole defaultRole = StaffHireSelectRole.Waiter,
            bool showRoleTabs = false)
        {
            OpenOrReplace<StaffHireSelectPanelController>(new StaffHireSelectPanelControllerData
            {
                DefaultRole = defaultRole,
                ShowRoleTabs = showRoleTabs
            });
        }

        /// <summary>
        /// 打开完整招聘界面（厨师/小二页签可切换）。
        /// </summary>
        public static void ShowStaffRecruitPanel()
        {
            var dataManager = DataManager.Instance;
            var defaultRole = dataManager != null
                ? dataManager.ResolveStaffHireSelectDefaultRole()
                : StaffHireSelectRole.Waiter;
            ShowStaffHireSelectPanel(defaultRole, showRoleTabs: true);
        }

        /// <summary>
        /// 打开贵客猜菜三选一面板；关闭后可选回调（用于继续点单流程）。
        /// </summary>
        public static void ShowVipGuestDishGuessPanel(int tableId = 0, bool forceRegenerate = false, Action onClosed = null)
        {
            OpenOrReplace<VipGuestDishGuessPanelController>(new VipGuestDishGuessPanelControllerData
            {
                TableId = tableId,
                ForceRegenerate = forceRegenerate,
                OnClosed = onClosed
            });
        }

        /// <summary>
        /// 打开酒店科技树面板。
        /// </summary>
        public static void ShowTavernTechTreePanel()
        {
            ShowTavernTechTreePanel(0);
        }

        /// <summary>
        /// 打开酒店科技树面板，可选预选科技节点。
        /// </summary>
        public static void ShowTavernTechTreePanel(int initialSelectedTechId)
        {
            OpenOrReplace<TavernTechTreePanelController>(new TavernTechTreePanelControllerData
            {
                InitialSelectedTechId = initialSelectedTechId
            });
        }

        /// <summary>
        /// 打开成就图鉴面板。
        /// </summary>
        public static void ShowAchievementCatalogPanel()
        {
            OpenOrReplace<AchievementCatalogPanelController>(new AchievementCatalogPanelControllerData());
        }

        /// <summary>
        /// 打开招募列表面板。
        /// </summary>
        public static void ShowRecruitListPanel(RecruitPanelRole defaultRole)
        {
            // 新流程：三选一招聘
            ShowStaffHireSelectPanel(defaultRole == RecruitPanelRole.Chef
                ? StaffHireSelectRole.Chef
                : StaffHireSelectRole.Waiter);
        }

        /// <summary>
        /// 显示功能解锁提示。
        /// </summary>
        public static void ShowNewFeatureOpenToast()
        {
            OpenOrReplace<NewFeatureOpenToastPanelController>(new NewFeatureOpenToastPanelControllerData());
        }

        /// <summary>
        /// 显示通用成功/解锁弹窗（SuccessPanel）。
        /// </summary>
        public static void ShowSuccessPanel(SuccessPanelControllerData data)
        {
            if (data == null)
            {
                return;
            }

            OpenOrReplace<SuccessPanelController>(data);
        }

        /// <summary>
        /// 显示成就获得横幅。
        /// </summary>
        public static void ShowGetAchievementPanel(GetAchievementPanelControllerData data)
        {
            if (data == null)
            {
                return;
            }

            GameplayResourceStore.InvalidateCachedAsset(
                "Assets/Res/Resources/UI/Panel/GetAchievementPanelController.prefab");

            UIKit.ClosePanel<GetAchievementPanelController>();
            var panel = UIKit.OpenPanel<GetAchievementPanelController>(
                JiangNanUIPanelLayerConfig.Resolve<GetAchievementPanelController>(UILevel.PopUI),
                data,
                prefabName: "GetAchievementPanelController");
            if (panel == null)
            {
                Debug.LogError("[HudOverlay] OpenPanel failed: GetAchievementPanelController");
                ShowAchievementCompletedFallback(data);
                return;
            }

            JiangNanUIPanelLayerConfig.Apply(panel);
        }

        private static void ShowAchievementCompletedFallback(GetAchievementPanelControllerData data)
        {
            var achievement = AchievementConfigUtility.Get(data?.AchievementId ?? 0);
            var callback = data?.OnClosed;
            ShowSuccessPanel(new SuccessPanelControllerData
            {
                Headline = "成就达成",
                Message = achievement?.Desc ?? achievement?.Name ?? string.Empty,
                ButtonText = "知道了",
                OnClosed = callback
            });
        }

        /// <summary>
        /// 显示二级桌位解锁提示。
        /// </summary>
        public static void ShowNewFeatureOpenTableLv2Panel(System.Action onComplete = null)
        {
            OpenOrReplace<NewFeatureOpenTableLv2PanelController>(new NewFeatureOpenTableLv2PanelControllerData
            {
                OnComplete = onComplete
            });
        }

        /// <summary>
        /// 显示桌位升级确认面板。
        /// </summary>
        public static void ShowTableUpgradePanel(Scene.TableArea table, System.Action onConfirm)
        {
            OpenOrReplace<TableUpgradePanelController>(new TableUpgradePanelControllerData
            {
                Table = table,
                OnConfirm = onConfirm
            });
        }

        /// <summary>
        /// 显示酒楼声望升级弹窗。
        /// </summary>
        public static void ShowUpgradeTavernPanel()
        {
            if (DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern)
            {
                return;
            }

            OpenOrReplace<UpgradeTavernPanelController>(new UpgradeTavernPanelControllerData());
        }

        /// <summary>
        /// 显示菜单切换弹窗（二星及以上自家酒楼）。
        /// </summary>
        public static void ShowMenuSwitchPanel()
        {
            if (DataManager.Instance == null || !DataManager.Instance.ShouldShowTavernMenuEntry())
            {
                return;
            }

            OpenOrReplace<MenuSwitchPanelController>(new MenuSwitchPanelControllerData(), "MenuSwitchPanel");
        }

        /// <summary>
        /// 显示酒楼恭喜升级弹窗（升级成功后）。
        /// </summary>
        /// <param name="tavernLevel">升级后的星级。</param>
        public static void ShowUpgradeTavernPopPanel(int tavernLevel)
        {
            OpenOrReplace<UpgradeTavernPopPanelController>(
                new UpgradeTavernPopPanelControllerData
                {
                    TavernLevel = Mathf.Max(1, tavernLevel),
                    OnClosed = FlushPendingPeakTimeWarningAfterUpgradePop
                },
                "UpgradeTavernPopPanel");
        }

        /// <summary>
        /// 升星成功后：先播 levelUpgradeLv{新星级} 视频（期间暂停营业时间/耐心/进度条），
        /// 播完再恢复并弹出恭喜升级窗，随后走原有高峰提示等流程。
        /// </summary>
        public static void PlayLevelUpgradeCinematicThenCongrats(int newTavernLevel)
        {
            newTavernLevel = Mathf.Max(1, newTavernLevel);
            BeginGameplayPauseForUpgradeCinematic();

            var clip = LoadLevelUpgradeVideoClip(newTavernLevel);
            if (clip == null)
            {
                Debug.LogWarning(
                    $"[HudOverlay] 缺少升星视频：{FormatLevelUpgradeVideoPath(newTavernLevel)}，直接弹恭喜窗。");
                EndUpgradeCinematicAndShowCongrats(newTavernLevel);
                return;
            }

            VideoWindowController.Show(
                clip,
                () => EndUpgradeCinematicAndShowCongrats(newTavernLevel),
                pauseOnLastFrame: false);
        }

        private static string FormatLevelUpgradeVideoPath(int newTavernLevel)
        {
            return $"Assets/Res/Resources/Videos/levelUpgradeLv{newTavernLevel}.mp4";
        }

        private static UnityEngine.Video.VideoClip LoadLevelUpgradeVideoClip(int newTavernLevel)
        {
            return GameplayResourceStore.LoadAsset<UnityEngine.Video.VideoClip>(
                FormatLevelUpgradeVideoPath(newTavernLevel));
        }

        private static int upgradeCinematicPauseDepth;
        private static float timeScaleBeforeUpgradeCinematic = 1f;

        /// <summary>
        /// 升星过场：timeScale=0，营业计时 / WaitForSeconds 进度 / 耐心 Time.time 一并冻结；VideoPlayer 仍走实时时钟。
        /// </summary>
        private static void BeginGameplayPauseForUpgradeCinematic()
        {
            if (upgradeCinematicPauseDepth++ > 0)
            {
                return;
            }

            timeScaleBeforeUpgradeCinematic = Time.timeScale;
            if (timeScaleBeforeUpgradeCinematic <= 0.0001f)
            {
                timeScaleBeforeUpgradeCinematic = 1f;
            }

            Time.timeScale = 0f;
        }

        private static void EndGameplayPauseForUpgradeCinematic()
        {
            if (upgradeCinematicPauseDepth <= 0)
            {
                return;
            }

            upgradeCinematicPauseDepth--;
            if (upgradeCinematicPauseDepth > 0)
            {
                return;
            }

            Time.timeScale = timeScaleBeforeUpgradeCinematic > 0.0001f
                ? timeScaleBeforeUpgradeCinematic
                : 1f;
        }

        private static void EndUpgradeCinematicAndShowCongrats(int newTavernLevel)
        {
            EndGameplayPauseForUpgradeCinematic();
            Scene.TavernSceneManager.Instance?.RefreshGuideWorldState();
            ShowUpgradeTavernPopPanel(newTavernLevel);
        }

        private static Transform pendingPrestigeFlySource;

        /// <summary>
        /// 设置下一次设施声望飞行的起点（购买前调用，Grant 时消费）。
        /// </summary>
        public static void SetPendingPrestigeFlySource(Transform source)
        {
            pendingPrestigeFlySource = source;
        }

        /// <summary>
        /// 原飞声望动画已停用；声望增加改由 tips「声望+XXX」提示。
        /// </summary>
        public static void PlayFacilityPrestigeFly(int prestigeAmount)
        {
            pendingPrestigeFlySource = null;
        }

        /// <summary>
        /// 显示单个员工招募确认面板。
        /// </summary>
        public static void ShowRecruitPanel(string displayName, string roleText, Sprite portrait, int cost, System.Action onConfirm)
        {
            OpenOrReplace<RecruitConfirmPanelController>(new RecruitConfirmPanelControllerData
            {
                DisplayName = displayName,
                RoleText = roleText,
                Portrait = portrait,
                Cost = cost,
                OnConfirm = onConfirm
            });
        }

        /// <summary>
        /// 显示厨师头顶的定时进度条。
        /// </summary>
        public static void ShowChefCookProgress(Transform target, float duration, Vector3 worldOffset)
        {
            EnsureWorldRuntimeHudPanel()?.ShowTimedProgress(target, duration, worldOffset, null, "ChefCookProgress");
        }

        /// <summary>
        /// 显示前台掌柜头顶的点单进度条（外部进度驱动，满进度由调用方回收）。
        /// </summary>
        public static GameObject ShowShopkeeperOrderProgress(
            Transform target,
            System.Func<float> progressProvider,
            Vector3 worldOffset,
            Sprite icon = null)
        {
            return EnsureWorldRuntimeHudPanel()?.ShowPersistentDynamicProgress(
                target,
                icon,
                progressProvider,
                worldOffset,
                "ShopkeeperOrderProgress");
        }

        /// <summary>
        /// 显示小二头顶的定时进度条。
        /// </summary>
        public static void ShowWaiterTaskProgress(Transform target, float duration, Vector3 worldOffset, Sprite icon = null)
        {
            EnsureWorldRuntimeHudPanel()?.ShowTimedProgress(target, duration, worldOffset, icon, "WaiterTaskProgress");
        }

        /// <summary>
        /// 显示可点击的小二头顶定时进度条。
        /// </summary>
        public static GameObject ShowClickableWaiterTaskProgress(Transform target, float duration, Vector3 worldOffset, Sprite icon, System.Action onClick)
        {
            return EnsureWorldRuntimeHudPanel()?.ShowClickableTimedProgress(target, duration, worldOffset, icon, onClick, "WaiterClickableTaskProgress");
        }

        /// <summary>
        /// 显示由外部进度驱动的小二进度条。
        /// </summary>
        public static GameObject ShowWaiterOrderCookProgress(Transform target, Sprite icon, System.Func<float> progressProvider, Vector3 worldOffset)
        {
            return EnsureWorldRuntimeHudPanel()?.ShowDynamicProgress(target, icon, progressProvider, worldOffset, "WaiterOrderCookProgress");
        }

        /// <summary>
        /// 显示可点击且由外部进度驱动的小二进度条。
        /// </summary>
        public static GameObject ShowClickableWaiterOrderCookProgress(Transform target, Sprite icon, System.Func<float> progressProvider, Vector3 worldOffset, System.Action onClick)
        {
            return EnsureWorldRuntimeHudPanel()?.ShowClickableDynamicProgress(target, icon, progressProvider, worldOffset, onClick, "WaiterClickableOrderCookProgress");
        }

        /// <summary>
        /// 释放世界跟随 HUD 条目。
        /// </summary>
        public static void ReleaseWorldHudItem(GameObject root)
        {
            EnsureWorldRuntimeHudPanel()?.ReleaseItem(root);
        }

        /// <summary>
        /// 显示楼梯上楼按钮（跟随世界挂点）。
        /// </summary>
        public static GameObject ShowUpStairButton(Transform target, Vector3 worldOffset, Action onClick)
        {
            return EnsureWorldRuntimeHudPanel()?.ShowUpStairButton(target, worldOffset, onClick);
        }

        /// <summary>
        /// 显示场景拉客按钮（跟随「轿子建造」挂点）。
        /// </summary>
        public static GameObject ShowMyDrumUpButton(Transform target, Vector3 worldOffset, Action onClick)
        {
            return EnsureWorldRuntimeHudPanel()?.ShowMyDrumUpButton(target, worldOffset, onClick);
        }

        /// <summary>销毁场景拉客按钮。</summary>
        public static void ClearMyDrumUpButton()
        {
            EnsureWorldRuntimeHudPanel()?.ClearMyDrumUpButton();
        }

        /// <summary>
        /// 自家酒楼拉客入口点击（场景 MyDrumUpBtn / 原底栏 btn_DrumUp 共用）。
        /// </summary>
        public static void HandleOwnTavernDrumUpClick()
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null || dataManager.IsVisitingOtherTavern)
            {
                return;
            }

            if (dataManager.GetTavernLevel() < 2)
            {
                ShowFloatingWarning("二星酒楼解锁");
                return;
            }

            if (!dataManager.IsJiaoziUnlocked())
            {
                ShowFloatingWarning("轿子未解锁");
                return;
            }

            if (!dataManager.IsPullCustomerCooldownReady())
            {
                ShowFloatingWarning("冷却中");
                return;
            }

            // 与城镇按钮一致：回城镇再去拜访他人酒楼拉客。
            var host = GameManager.Instance;
            if (host != null)
            {
                host.StartCoroutine(SceneFlowCoordinator.EnterTown());
            }
        }

        /// <summary>
        /// 显示贵客头顶大堂/包厢气泡。
        /// </summary>
        /// <param name="privateRoomLocked">二楼未开放：包厢置灰，点击仅提示。</param>
        public static GameObject ShowVipGuestAction(
            Transform target,
            Vector3 worldOffset,
            bool usePrivateRoom,
            Action onClick,
            bool privateRoomLocked = false)
        {
            return EnsureWorldRuntimeHudPanel()?.ShowVipGuestAction(
                target,
                worldOffset,
                usePrivateRoom,
                onClick,
                privateRoomLocked);
        }

        /// <summary>
        /// 顾客反馈文字气泡（跟随模型，低于耐心条）。durationSeconds &lt; 0 常驻到模型消失。
        /// </summary>
        public static GameObject ShowCustomerReviewTip(
            Transform target,
            string content,
            float durationSeconds = -1f,
            Vector3? worldOffset = null)
        {
            var offset = worldOffset ?? new Vector3(0f, TavernReviewTipsView.DefaultHeadOffsetY, 0f);
            return EnsureWorldRuntimeHudPanel()?.ShowCustomerReviewTip(target, content, offset, durationSeconds);
        }

        /// <summary>等待超时走客反馈。</summary>
        public static void ShowWaitTimeoutReviewTip(Transform target)
        {
            ShowCustomerReviewTip(target, "没人招呼我", durationSeconds: -1f);
        }

        /// <summary>拜访拉客反馈（约 3 秒）。</summary>
        public static void ShowPulledAwayReviewTip(Transform target)
        {
            ShowCustomerReviewTip(target, "走，去看看", durationSeconds: 3f);
        }

        /// <summary>贵客且二楼有空位时的反馈（常驻到模型消失）。</summary>
        public static void ShowVipLobbyNoisyReviewTip(Transform target)
        {
            ShowCustomerReviewTip(target, "大堂太吵了", durationSeconds: -1f);
        }

        /// <summary>关闭指定目标上的顾客反馈气泡。</summary>
        public static void ReleaseCustomerReviewTip(Transform target)
        {
            EnsureWorldRuntimeHudPanel()?.ReleaseCustomerReviewTip(target);
        }

        /// <summary>桌位「客人被拉走」提示。</summary>
        public static TavernDrumUpTipsView ShowTablePulledTip(
            Transform tableTarget,
            int tableId,
            int headIconId,
            string pullerName,
            Vector3? worldOffset = null,
            System.Action onClick = null,
            string displayCaption = null,
            bool useSelfHeadIcon = false,
            bool clickEnabled = true)
        {
            var offset = worldOffset ?? new Vector3(0f, TavernWorldRuntimeHudLayout.TablePulledTipHeightOffset, 0f);
            return EnsureWorldRuntimeHudPanel()?.ShowTablePulledTip(
                tableTarget,
                tableId,
                offset,
                headIconId,
                pullerName,
                onClick,
                displayCaption,
                useSelfHeadIcon,
                clickEnabled);
        }

        /// <summary>关闭指定桌位的被拉客提示。</summary>
        public static void ReleaseTablePulledTip(int tableId)
        {
            EnsureWorldRuntimeHudPanel()?.ReleaseTablePulledTip(tableId);
        }

        /// <summary>指定桌是否正在显示被拉客提示。</summary>
        public static bool HasTablePulledTip(int tableId)
        {
            return EnsureWorldRuntimeHudPanel()?.HasTablePulledTip(tableId) == true;
        }

        /// <summary>
        /// 销毁全部上楼按钮（进二楼或离开一楼时调用）。
        /// </summary>
        public static void ClearUpStairButtons()
        {
            EnsureWorldRuntimeHudPanel()?.ClearUpStairButtons();
        }

        /// <summary>
        /// 显示可点击的小二状态图标。
        /// </summary>
        public static GameObject ShowWaiterStateIcon(Transform target, Sprite icon, System.Action onClick, Vector3 worldOffset)
        {
            return EnsureWorldRuntimeHudPanel()?.ShowStateIcon(target, icon, onClick, worldOffset, "WaiterTaskProgress");
        }

        /// <summary>
        /// 显示出餐台上的上菜按钮（NewOrderBtn）。
        /// </summary>
        public static GameObject ShowFoodTableServeBubble(Transform target, Sprite icon, System.Action onClick, Vector3 worldOffset)
        {
            return EnsureWorldRuntimeHudPanel()?.ShowFoodTableServeBubble(target, icon, onClick, worldOffset);
        }

        /// <summary>
        /// 获取或创建 waiter 复用的头顶 HUD 条目。
        /// </summary>
        public static TavernWorldRuntimeHudItemView GetOrCreateWaiterTaskItem(GameObject existingRoot)
        {
            return EnsureWorldRuntimeHudPanel()?.GetOrCreateItemView(existingRoot, "WaiterTaskProgress");
        }

        /// <summary>
        /// 注册桌位头顶动作 HUD，并交由统一世界 HUD 容器管理。
        /// </summary>
        public static Scene.TableAreaUI RegisterTableActionHud(Scene.TableArea table)
        {
            return EnsureWorldRuntimeHudPanel()?.EnsureTableActionHud(table);
        }

        /// <summary>
        /// 注册引导建造点头顶动作 HUD，并交由统一世界 HUD 容器管理。
        /// </summary>
        public static Scene.TableAreaUI RegisterPurchaseActionHud(string purchaseKey, Transform target, System.Action onPurchase)
        {
            return EnsureWorldRuntimeHudPanel()?.EnsurePurchaseActionHud(purchaseKey, target, onPurchase);
        }

        /// <summary>
        /// 注册墙体扩建头顶 HUD（group_expand）。
        /// </summary>
        public static Scene.TableAreaUI RegisterInteriorWallExpandHud(Transform target, int cost, System.Action onExpand)
        {
            return EnsureWorldRuntimeHudPanel()?.EnsureInteriorWallExpandHud(target, cost, onExpand);
        }

        /// <summary>
        /// 注册招聘地块头顶动作 HUD，并交由统一世界 HUD 容器管理。
        /// </summary>
        public static Scene.EmployAreaUI RegisterEmployActionHud(
            string employKey,
            Transform target,
            cfg.StaffPosition position,
            int cost,
            System.Action onEmploy)
        {
            return EnsureWorldRuntimeHudPanel()?.EnsureEmployActionHud(employKey, target, position, cost, onEmploy);
        }

        /// <summary>
        /// 兼容旧接口：无职位信息时仅绑定回调。
        /// </summary>
        public static Scene.EmployAreaUI RegisterEmployActionHud(string employKey, Transform target, System.Action onEmploy)
        {
            return EnsureWorldRuntimeHudPanel()?.EnsureEmployActionHud(employKey, target, onEmploy);
        }

        /// <summary>
        /// 释放桌位头顶动作 HUD。
        /// </summary>
        public static void UnregisterTableActionHud(Scene.TableArea table)
        {
            EnsureWorldRuntimeHudPanel()?.ReleaseTableActionHud(table);
        }

        /// <summary>
        /// 释放引导建造点头顶动作 HUD。
        /// </summary>
        public static void UnregisterPurchaseActionHud(string purchaseKey)
        {
            EnsureWorldRuntimeHudPanel()?.ReleasePurchaseActionHud(purchaseKey);
        }

        /// <summary>
        /// 释放墙体扩建头顶 HUD。
        /// </summary>
        public static void UnregisterInteriorWallExpandHud()
        {
            EnsureWorldRuntimeHudPanel()?.ReleaseInteriorWallExpandHud();
        }

        /// <summary>
        /// 释放招聘地块头顶动作 HUD。
        /// </summary>
        public static void UnregisterEmployActionHud(string employKey)
        {
            EnsureWorldRuntimeHudPanel()?.ReleaseEmployActionHud(employKey);
        }

        /// <summary>
        /// 关闭旧实例并重新打开面板，确保同类浮层只保留一个。
        /// </summary>
        private static void OpenOrReplace<TPanel>(UIPanelData data) where TPanel : UIPanel
        {
            OpenOrReplace<TPanel>(data, null);
        }

        private static void OpenOrReplace<TPanel>(UIPanelData data, string prefabPath) where TPanel : UIPanel
        {
            UIKit.ClosePanel<TPanel>();
            var panel = string.IsNullOrWhiteSpace(prefabPath)
                ? UIKit.OpenPanel<TPanel>(JiangNanUIPanelLayerConfig.Resolve<TPanel>(UILevel.PopUI), data)
                : UIKit.OpenPanel<TPanel>(
                    JiangNanUIPanelLayerConfig.Resolve<TPanel>(UILevel.PopUI),
                    data,
                    prefabName: prefabPath);
            if (panel == null)
            {
                Debug.LogError($"[HudOverlay] OpenPanel failed: {typeof(TPanel).Name}");
            }
            else
            {
                JiangNanUIPanelLayerConfig.Apply(panel);
            }
        }

        /// <summary>
        /// 确保世界跟随 HUD 容器存在。
        /// </summary>
        internal static TavernWorldRuntimeHudPanelController EnsureWorldRuntimeHudPanelForWaitHud()
        {
            return EnsureWorldRuntimeHudPanel();
        }

        /// <summary>
        /// 确保世界跟随 HUD 容器存在。
        /// </summary>
        private static TavernWorldRuntimeHudPanelController EnsureWorldRuntimeHudPanel()
        {
            var panel = UIKit.GetPanel<TavernWorldRuntimeHudPanelController>();
            if (panel != null)
            {
                // 不要反复置顶：否则会盖住顶栏员工/科技入口点击
                JiangNanUIPanelLayerConfig.Apply(panel, bringToFront: false);
                return panel;
            }

            return UIKit.OpenPanel<TavernWorldRuntimeHudPanelController>(
                JiangNanUIPanelLayerConfig.Resolve<TavernWorldRuntimeHudPanelController>(),
                new TavernWorldRuntimeHudPanelControllerData());
        }
    }
}
