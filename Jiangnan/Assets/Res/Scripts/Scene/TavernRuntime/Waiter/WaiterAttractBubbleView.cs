using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.Scene
{
    /// <summary>
    /// 小二拉客头顶气泡：显示本会话已拉客人数。
    /// </summary>
    public class WaiterAttractBubbleView : MonoBehaviour
    {
        private Text legacyCountText;
        private TMP_Text tmpCountText;
        private Image dishIcon;

        public void SetCustomerCount(int attractedCustomerCount)
        {
            EnsureReferences();
            var label = $"拉客人数：{Mathf.Max(0, attractedCustomerCount)}";
            if (tmpCountText != null)
            {
                tmpCountText.gameObject.SetActive(true);
                tmpCountText.text = label;
            }

            if (legacyCountText != null)
            {
                legacyCountText.gameObject.SetActive(true);
                legacyCountText.text = label;
            }

            gameObject.SetActive(true);
        }

        public void SetIcon(Sprite icon)
        {
            EnsureReferences();
            if (dishIcon == null)
            {
                return;
            }

            dishIcon.gameObject.SetActive(icon != null);
            dishIcon.enabled = icon != null;
            if (icon != null)
            {
                dishIcon.sprite = icon;
            }
        }

        private void EnsureReferences()
        {
            if (tmpCountText == null)
            {
                tmpCountText = transform.Find("BubbleCanvas/BubbleBG/DishText")?.GetComponent<TMP_Text>()
                               ?? GetComponentInChildren<TMP_Text>(true);
            }

            if (legacyCountText == null)
            {
                legacyCountText = transform.Find("BubbleCanvas/BubbleBG/DishText")?.GetComponent<Text>()
                                  ?? GetComponentInChildren<Text>(true);
            }

            if (dishIcon == null)
            {
                dishIcon = transform.Find("BubbleCanvas/BubbleBG/DishIcon")?.GetComponent<Image>();
            }
        }
    }
}
