using System;
using System.Collections.Generic;
using JN.Client.Manager;

namespace JN.Client.UI
{
    /// <summary>
    /// 串行展示通用成功弹窗，避免科技/成就同时触发时互相覆盖。
    /// </summary>
    public static class GameplaySuccessToastService
    {
        private static readonly Queue<SuccessPanelControllerData> Pending = new();
        private static readonly Queue<GetAchievementPanelControllerData> AchievementPending = new();
        private static readonly HashSet<string> ActiveSignatures = new();
        private static readonly HashSet<int> ActiveAchievementIds = new();
        private static bool showing;
        private static bool achievementShowing;
        private static string showingSignature;

        public static void Enqueue(string headline, string message, string buttonText = "知道了", Action onClosed = null)
        {
            if (string.IsNullOrWhiteSpace(headline) && string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var signature = BuildSignature(headline, message);
            if (!ActiveSignatures.Add(signature))
            {
                return;
            }

            Pending.Enqueue(new SuccessPanelControllerData
            {
                Headline = headline,
                Message = message,
                ButtonText = buttonText,
                ExtraOnClosed = onClosed
            });
            TryShowNext();
        }

        public static void EnqueueTechUnlocked(string techName)
        {
            var name = string.IsNullOrWhiteSpace(techName) ? "生财策" : techName;
            Enqueue("生财策解锁", name);
        }

        public static void EnqueueAchievementCompleted(int achievementId)
        {
            // 酒馆内成就达成提示暂时关闭。
            return;
        }

        public static void EnqueueAchievementEntryUnlocked(Action onClosed = null)
        {
            // 成就图鉴解锁提示暂时关闭。
            onClosed?.Invoke();
        }

        public static void EnqueueTechEntryUnlocked(Action onClosed = null)
        {
            Enqueue("新功能开启", "生财策已解锁", "知道了", onClosed);
        }

        private static void TryShowNext()
        {
            if (showing || Pending.Count == 0)
            {
                return;
            }

            showing = true;
            var data = Pending.Dequeue();
            showingSignature = BuildSignature(data.Headline, data.Message);
            GameAudioManager.PlayFeatureUnlock();
            var extraOnClosed = data.ExtraOnClosed;
            data.ExtraOnClosed = null;
            data.OnClosed = () =>
            {
                extraOnClosed?.Invoke();
                ReleaseSignature(showingSignature);
                showingSignature = null;
                showing = false;
                TryShowNext();
            };
            HudOverlayService.ShowSuccessPanel(data);
        }

        private static void TryShowNextAchievement()
        {
            if (achievementShowing || AchievementPending.Count == 0)
            {
                return;
            }

            achievementShowing = true;
            var data = AchievementPending.Dequeue();
            var achievementId = data.AchievementId;
            GameAudioManager.PlayFeatureUnlock();
            data.OnClosed = () =>
            {
                ActiveAchievementIds.Remove(achievementId);
                achievementShowing = false;
                TryShowNextAchievement();
            };
            HudOverlayService.ShowGetAchievementPanel(data);
        }

        private static void ReleaseSignature(string signature)
        {
            if (string.IsNullOrEmpty(signature))
            {
                return;
            }

            ActiveSignatures.Remove(signature);
        }

        private static string BuildSignature(string headline, string message)
        {
            return $"{headline}\u001f{message}";
        }
    }
}
