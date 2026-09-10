using System;
using UnityEngine;
using UnityEngine.UI;
using JN.Client.Manager;

namespace JN.Client.Scene
{
    /// <summary>
    /// 掌柜柜台随机收益气泡的点击领取组件。
    /// </summary>
    public class CounterRewardBubbleView : MonoBehaviour
    {
        private Action onClaim;
        private bool claimed;
        private Button button;

        /// <summary>
        /// 注入领取回调并确保 BubbleBG 可点击。
        /// </summary>
        public void Initialize(Action claimCallback)
        {
            onClaim = claimCallback;
            EnsureButton();
        }

        private void EnsureButton()
        {
            var bubbleBg = transform.Find("BubbleCanvas/BubbleBG");
            if (bubbleBg == null)
            {
                return;
            }

            button = bubbleBg.GetComponent<Button>();
            if (button == null)
            {
                button = bubbleBg.gameObject.AddComponent<Button>();
                var image = bubbleBg.GetComponent<Image>();
                if (image != null)
                {
                    button.targetGraphic = image;
                }
            }

            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
        }

        private void HandleClick()
        {
            if (claimed)
            {
                return;
            }

            GameAudioManager.PlayButtonClick();
            claimed = true;
            if (button != null)
            {
                button.interactable = false;
            }

            onClaim?.Invoke();
        }

        public Transform GetCoinFlySourceTransform()
        {
            var dishIcon = transform.Find("BubbleCanvas/BubbleBG/DishIcon");
            if (dishIcon != null)
            {
                return dishIcon;
            }

            var bubbleBg = transform.Find("BubbleCanvas/BubbleBG");
            return bubbleBg != null ? bubbleBg : transform;
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
            }
        }
    }
}
