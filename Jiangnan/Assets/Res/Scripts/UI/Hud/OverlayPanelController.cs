using System;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using JN.Client.Manager;

namespace JN.Client.UI
{
    /// <summary>
    /// 为各类覆盖型 HUD 面板提供通用节点解析和按钮绑定能力。
    /// </summary>
    public abstract class OverlayPanelController<TData> : HudPanelController<TData> where TData : UIPanelData, new()
    {
        /// <summary>
        /// 按路径或兜底名称解析指定组件。
        /// </summary>
        protected T ResolveComponent<T>(string path, string fallbackName = null) where T : Component
        {
            var target = ResolveTransform(path, fallbackName);
            return target != null ? target.GetComponent<T>() ?? target.GetComponentInChildren<T>(true) : null;
        }

        /// <summary>
        /// 快速解析文本组件。
        /// </summary>
        protected TMP_Text ResolveText(string path, string fallbackName = null)
        {
            return ResolveComponent<TMP_Text>(path, fallbackName);
        }

        /// <summary>
        /// 快速解析按钮组件。
        /// </summary>
        protected Button ResolveButton(string path, string fallbackName = null)
        {
            return ResolveComponent<Button>(path, fallbackName);
        }

        /// <summary>
        /// 快速解析图片组件。
        /// </summary>
        protected Image ResolveImage(string path, string fallbackName = null)
        {
            return ResolveComponent<Image>(path, fallbackName);
        }

        /// <summary>
        /// 按路径或名称解析目标节点。
        /// </summary>
        protected GameObject ResolveNode(string path, string fallbackName = null)
        {
            return ResolveTransform(path, fallbackName)?.gameObject;
        }

        /// <summary>
        /// 优先按完整路径，失败后按名称回退解析节点。
        /// </summary>
        protected Transform ResolveTransform(string path, string fallbackName = null)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                var byPath = transform.Find(path);
                if (byPath != null)
                {
                    return byPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(fallbackName))
            {
                return HudBindingUtility.FindChildRecursive(transform, fallbackName);
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                var lastSlash = path.LastIndexOf('/');
                var defaultName = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
                return HudBindingUtility.FindChildRecursive(transform, defaultName);
            }

            return null;
        }

        /// <summary>
        /// 统一重绑按钮回调。
        /// </summary>
        protected void BindButton(Button button, Action callback)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            if (callback != null)
            {
                button.onClick.AddListener(() =>
                {
                    GameAudioManager.PlayButtonClick();
                    callback();
                });
            }
        }

        /// <summary>
        /// 将文本设为可点击（用于天赋名等轻量交互）。
        /// </summary>
        protected void BindTextButton(TMP_Text text, Action callback)
        {
            if (text == null)
            {
                return;
            }

            text.raycastTarget = true;

            var legacyButton = text.GetComponent<Button>();
            if (legacyButton != null)
            {
                legacyButton.onClick.RemoveAllListeners();
                Destroy(legacyButton);
            }

            var trigger = text.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = text.gameObject.AddComponent<EventTrigger>();
            }

            trigger.triggers ??= new System.Collections.Generic.List<EventTrigger.Entry>();
            trigger.triggers.RemoveAll(entry => entry.eventID == EventTriggerType.PointerClick);

            if (callback == null)
            {
                return;
            }

            var clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            clickEntry.callback.AddListener(_ =>
            {
                GameAudioManager.PlayButtonClick();
                callback();
            });
            trigger.triggers.Add(clickEntry);
        }

        /// <summary>
        /// 切换指定节点显隐。
        /// </summary>
        protected void SetNodeVisible(string path, bool visible, string fallbackName = null)
        {
            var node = ResolveNode(path, fallbackName);
            if (node != null)
            {
                node.SetActive(visible);
            }
        }

        /// <summary>
        /// 设置指定文本节点内容。
        /// </summary>
        protected void SetText(string path, string content, string fallbackName = null)
        {
            var text = ResolveText(path, fallbackName);
            if (text != null)
            {
                text.text = content;
            }
        }
    }
}
