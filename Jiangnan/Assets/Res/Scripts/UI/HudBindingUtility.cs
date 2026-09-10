using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.UI
{
    /// <summary>
    /// 提供 HUD 拆分后常用的节点查找辅助方法。
    /// </summary>
    internal static class HudBindingUtility
    {
        /// <summary>
        /// 递归查找指定名称的子节点。
        /// </summary>
        public static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null)
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

                var found = FindChildRecursive(child, childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// 解析指定节点下的文本组件，兼容直接和递归查找。
        /// </summary>
        public static TMP_Text ResolveChildText(Transform root, string nodeName)
        {
            var node = root != null ? root.Find(nodeName) ?? FindChildRecursive(root, nodeName) : null;
            return node != null ? node.GetComponent<TMP_Text>() ?? node.GetComponentInChildren<TMP_Text>(true) : null;
        }

        /// <summary>
        /// 解析指定节点下的图片组件，兼容直接和递归查找。
        /// </summary>
        public static Image ResolveChildImage(Transform root, string nodeName)
        {
            var node = root != null ? root.Find(nodeName) ?? FindChildRecursive(root, nodeName) : null;
            return node != null ? node.GetComponent<Image>() ?? node.GetComponentInChildren<Image>(true) : null;
        }
    }
}
