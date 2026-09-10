using JN.Client;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.UI;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.Scene
{
    public partial class TavernSceneManager
    {
        private const string CounterRewardBubblePrefabPath = "Assets/Res/Resources/UI/Guides/CounterRewardBubble.prefab";
        private const string CounterRewardCoinIconPath = "Assets/Res/Resources/Textures/UI/Icons 1/coin.png";
        private const string ChenghaoIconPathFormat = "Assets/Res/Textures/UI/TavernStatusBarPanel/chenghao-{0}.png";
        private const float DefaultCounterRandomRewardInterval = 20f;
        private const int DefaultCounterRandomRewardCoinMin = 49;
        private const int DefaultCounterRandomRewardCoinMax = 301;
        private const float CounterRewardBubbleScaleMultiplier = 3f;

        [SerializeField] private Vector3 counterRewardBubbleLocalOffset = new(0f, 1.55f, 0f);

        private float counterRewardTimerRemaining = -1f;
        private GameObject counterRewardBubbleRoot;
        private int pendingCounterRewardAmount;

        private float GetCounterRandomRewardInterval()
        {
            return TbConfigRuntime.GetCounterRandomRewardInterval(DefaultCounterRandomRewardInterval);
        }

        private int RollCounterRandomRewardAmount()
        {
            TbConfigRuntime.GetCounterRandomRewardCoinRange(
                DefaultCounterRandomRewardCoinMin,
                DefaultCounterRandomRewardCoinMax,
                out var min,
                out var max);
            return Random.Range(min, max + 1);
        }

        private bool CanCounterRandomRewardRun()
        {
            if (DataManager.Instance == null
                || DataManager.Instance.TavernData == null
                || !DataManager.Instance.TavernData.isOpen
                || isClosingBusiness
                || softClosingStarted)
            {
                return false;
            }

            var guide = DataManager.Instance.GameplayGuideData;
            if (guide == null || !guide.purchasedCounter)
            {
                return false;
            }

            if (!StaffTechEffectMerger.IsCounterRandomRewardEnabled(DataManager.Instance?.ResearchedTechIds))
            {
                return false;
            }

            return TryGetCounterRewardBubbleAnchor(out _, out _);
        }

        public void RefreshCounterRandomReward()
        {
            StartCounterRandomRewardTimer();
        }

        private void StartCounterRandomRewardTimer()
        {
            if (!CanCounterRandomRewardRun())
            {
                ResetCounterRandomReward();
                return;
            }

            counterRewardTimerRemaining = GetCounterRandomRewardInterval();
        }

        private void ResetCounterRandomReward()
        {
            ClearCounterRewardBubble();
            counterRewardTimerRemaining = -1f;
            pendingCounterRewardAmount = 0;
        }

        private void TickCounterRandomReward(float deltaTime)
        {
            if (!CanCounterRandomRewardRun())
            {
                ResetCounterRandomReward();
                return;
            }

            if (counterRewardBubbleRoot != null)
            {
                return;
            }

            if (counterRewardTimerRemaining < 0f)
            {
                counterRewardTimerRemaining = GetCounterRandomRewardInterval();
            }

            counterRewardTimerRemaining = Mathf.Max(0f, counterRewardTimerRemaining - deltaTime);
            if (counterRewardTimerRemaining <= 0f)
            {
                TryShowCounterRewardBubble();
            }
        }

        private bool TryGetCounterRewardBubbleAnchor(out Transform anchor, out Vector3 localOffset)
        {
            anchor = null;
            localOffset = counterRewardBubbleLocalOffset;
            if (guideStaffVisuals.TryGetValue(GuideShopkeeperVisualKey, out var shopkeeper)
                && shopkeeper != null
                && shopkeeper.activeInHierarchy)
            {
                anchor = shopkeeper.transform;
                return true;
            }

            if (guideCounterObject != null && guideCounterObject.activeInHierarchy)
            {
                anchor = guideCounterObject.transform;
                return true;
            }

            return false;
        }

        private void TryShowCounterRewardBubble()
        {
            if (counterRewardBubbleRoot != null
                || !CanCounterRandomRewardRun()
                || !TryGetCounterRewardBubbleAnchor(out var anchor, out var localOffset))
            {
                return;
            }

            var bubblePrefab = GameplayResourceStore.LoadAsset<GameObject>(CounterRewardBubblePrefabPath);
            if (bubblePrefab == null)
            {
                counterRewardTimerRemaining = GetCounterRandomRewardInterval();
                return;
            }

            pendingCounterRewardAmount = RollCounterRandomRewardAmount();
            counterRewardBubbleRoot = Instantiate(bubblePrefab, anchor);
            if (counterRewardBubbleRoot == null)
            {
                pendingCounterRewardAmount = 0;
                counterRewardTimerRemaining = GetCounterRandomRewardInterval();
                return;
            }

            counterRewardBubbleRoot.name = "CounterRewardBubble";
            counterRewardBubbleRoot.transform.localPosition = localOffset;
            counterRewardBubbleRoot.transform.localRotation = Quaternion.identity;
            ApplyCounterRewardBubblePresentation(counterRewardBubbleRoot.transform);

            var billboard = counterRewardBubbleRoot.GetComponent<Billboard>();
            if (billboard != null)
            {
                billboard.SceneCamera = SceneCamera != null ? SceneCamera : Camera.main;
            }

            var coinIcon = GameplayResourceStore.LoadAsset<Sprite>(CounterRewardCoinIconPath);
            if (coinIcon == null)
            {
                var iconIndex = Random.Range(1, 3);
                coinIcon = GameplayResourceStore.LoadAsset<Sprite>(string.Format(ChenghaoIconPathFormat, iconIndex));
            }

            var dishIcon = counterRewardBubbleRoot.transform.Find("BubbleCanvas/BubbleBG/DishIcon")?.GetComponent<Image>();
            if (dishIcon != null)
            {
                dishIcon.sprite = coinIcon;
                dishIcon.preserveAspect = true;
                dishIcon.gameObject.SetActive(coinIcon != null);
            }

            var dishText = counterRewardBubbleRoot.transform.Find("BubbleCanvas/BubbleBG/DishText");
            if (dishText != null)
            {
                dishText.gameObject.SetActive(false);
            }

            var bubbleView = counterRewardBubbleRoot.AddComponent<CounterRewardBubbleView>();
            bubbleView.Initialize(OnCounterRewardBubbleClicked);
        }

        private void OnCounterRewardBubbleClicked()
        {
            if (pendingCounterRewardAmount <= 0 || counterRewardBubbleRoot == null)
            {
                ClearCounterRewardBubble();
                counterRewardTimerRemaining = GetCounterRandomRewardInterval();
                return;
            }

            var rewardAmount = pendingCounterRewardAmount;
            CoinDisplayRefreshCoordinator.DeferGoldRefreshUntilFlyComplete();
            GameAudioManager.PlayCheckoutCoins();
            TryPlayCounterRewardCoinFlyToTop(
                counterRewardBubbleRoot,
                CoinDisplayRefreshCoordinator.NotifyFlyComplete);
            DataManager.Instance?.GrantCounterRandomRewardIncome(rewardAmount);
            ClearCounterRewardBubble();
            counterRewardTimerRemaining = GetCounterRandomRewardInterval();
        }

        private void TryPlayCounterRewardCoinFlyToTop(GameObject bubbleRoot, System.Action onAllCoinsSettled = null)
        {
            if (bubbleRoot == null)
            {
                onAllCoinsSettled?.Invoke();
                return;
            }

            var coinTarget = GOReferenceManager.Instance != null ? GOReferenceManager.Instance.GetCoinTransform() : null;
            if (coinTarget == null)
            {
                onAllCoinsSettled?.Invoke();
                return;
            }

            var bubbleView = bubbleRoot.GetComponent<CounterRewardBubbleView>();
            var flySource = bubbleView != null
                ? bubbleView.GetCoinFlySourceTransform()
                : bubbleRoot.transform;
            if (flySource == null)
            {
                onAllCoinsSettled?.Invoke();
                return;
            }

            var camera = SceneCamera != null ? SceneCamera : Camera.main;
            if (camera == null)
            {
                GameUIEffects.PlayCoinsFly(flySource, coinTarget, onAllCoinsSettled);
                return;
            }

            var worldCenter = GameUIEffects.GetTransformWorldCenter(flySource);
            var screenPoint = camera.WorldToScreenPoint(worldCenter);
            if (screenPoint.z <= 0f)
            {
                GameUIEffects.PlayCoinsFly(flySource, coinTarget, onAllCoinsSettled);
                return;
            }

            GameUIEffects.PlayCoinsFlyFromScreen(screenPoint, coinTarget, onAllCoinsSettled);
        }

        private void ClearCounterRewardBubble()
        {
            if (counterRewardBubbleRoot == null)
            {
                return;
            }

            Destroy(counterRewardBubbleRoot);
            counterRewardBubbleRoot = null;
        }

        private static void ApplyCounterRewardBubblePresentation(Transform bubbleRoot)
        {
            if (bubbleRoot == null)
            {
                return;
            }

            bubbleRoot.localScale *= CounterRewardBubbleScaleMultiplier;

            var bubbleCanvasComponent = bubbleRoot.Find("BubbleCanvas")?.GetComponent<Canvas>();
            if (bubbleCanvasComponent != null)
            {
                bubbleCanvasComponent.sortingOrder = 20;
            }
        }
    }
}
