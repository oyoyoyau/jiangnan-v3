using System;
using System.Collections.Generic;
using cfg;
using DG.Tweening;
using JN.Client.Config;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.UI
{
    /// <summary>
    /// 对话面板数据。
    /// </summary>
    public class DialogPanelControllerData : UIPanelData
    {
        /// <summary>对话组 Id（对应 Dialog.dialogId）。</summary>
        public string DialogId;

        /// <summary>不走配表时的台词（优先于 DialogId）。</summary>
        public string[] ScriptedLines;

        /// <summary>脚本台词使用的立绘键，如 fushang。</summary>
        public string ScriptedHeadPic;

        /// <summary>打开时从下方飞入对话框。</summary>
        public bool FlyInFromBottom;

        /// <summary>全部台词播完并关闭后回调。</summary>
        public Action OnComplete;
    }

    /// <summary>
    /// 顺序对话面板：展示立绘与台词，点击 bg / mask 下一条，结束触发 OnComplete。
    /// </summary>
    public class DialogPanelController : OverlayPanelController<DialogPanelControllerData>
    {
        [SerializeField] private Image headPicImage;
        [SerializeField] private TextMeshProUGUI contentText;
        [SerializeField] private Button bgButton;
        [SerializeField] private Button maskButton;

        private readonly List<Dialog> lines = new();
        private readonly List<string> scriptedLines = new();
        private string scriptedHeadPic;
        private int lineIndex;
        private bool completeInvoked;
        private bool usingScriptedLines;
        private RectTransform rootRect;
        private Vector2 rootHomeAnchoredPosition;
        private bool hasRootHome;
        private Tween rootFlyTween;

        protected override void OnPanelInit()
        {
            EnsureNodes();
            BindDialogAdvanceButtons();
            CacheRootHome();
        }

        protected override void OnPanelOpen(DialogPanelControllerData data)
        {
            EnsureNodes();
            BindDialogAdvanceButtons();
            completeInvoked = false;
            lineIndex = 0;
            lines.Clear();
            scriptedLines.Clear();
            usingScriptedLines = false;
            scriptedHeadPic = data != null ? data.ScriptedHeadPic : null;

            if (data?.ScriptedLines != null && data.ScriptedLines.Length > 0)
            {
                usingScriptedLines = true;
                for (var i = 0; i < data.ScriptedLines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(data.ScriptedLines[i]))
                    {
                        scriptedLines.Add(data.ScriptedLines[i]);
                    }
                }
            }
            else
            {
                var dialogId = data != null ? data.DialogId : null;
                var loaded = DialogConfigUtility.GetLines(dialogId);
                if (loaded != null && loaded.Count > 0)
                {
                    lines.AddRange(loaded);
                }
            }

            if ((usingScriptedLines && scriptedLines.Count == 0) || (!usingScriptedLines && lines.Count == 0))
            {
                Debug.LogWarning($"[DialogPanel] 对话组为空或不存在：{data?.DialogId}");
                CloseSelf();
                return;
            }

            ShowCurrentLine();
            PlayFlyInIfNeeded(data);
        }

        protected override void OnPanelShow()
        {
            EnsureNodes();
        }

        protected override void OnPanelClose()
        {
            KillFlyIn();
            ResetRootHome();
            InvokeCompleteOnce();
            lines.Clear();
            scriptedLines.Clear();
            lineIndex = 0;
        }

        private void PlayFlyInIfNeeded(DialogPanelControllerData data)
        {
            CacheRootHome();
            if (rootRect == null)
            {
                return;
            }

            KillFlyIn();
            rootRect.anchoredPosition = rootHomeAnchoredPosition;
            if (data == null || !data.FlyInFromBottom)
            {
                return;
            }

            rootRect.anchoredPosition = rootHomeAnchoredPosition + new Vector2(0f, -460f);
            rootFlyTween = rootRect.DOAnchorPos(rootHomeAnchoredPosition, 0.55f)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true);
        }

        private void CacheRootHome()
        {
            rootRect ??= ResolveTransform("Root", "Root") as RectTransform;
            if (rootRect == null || hasRootHome)
            {
                return;
            }

            rootHomeAnchoredPosition = rootRect.anchoredPosition;
            hasRootHome = true;
        }

        private void ResetRootHome()
        {
            if (rootRect != null && hasRootHome)
            {
                rootRect.anchoredPosition = rootHomeAnchoredPosition;
            }
        }

        private void KillFlyIn()
        {
            rootFlyTween?.Kill();
            rootFlyTween = null;
        }

        private void OnDestroy()
        {
            KillFlyIn();
        }

        private void BindDialogAdvanceButtons()
        {
            BindButton(bgButton, OnClickBg);
            BindButton(maskButton, OnClickBg);
        }

        private void OnClickBg()
        {
            var count = usingScriptedLines ? scriptedLines.Count : lines.Count;
            if (count <= 0)
            {
                CloseSelf();
                return;
            }

            if (lineIndex >= count - 1)
            {
                CloseSelf();
                return;
            }

            lineIndex++;
            ShowCurrentLine();
        }

        private void ShowCurrentLine()
        {
            if (usingScriptedLines)
            {
                if (lineIndex < 0 || lineIndex >= scriptedLines.Count)
                {
                    return;
                }

                if (contentText != null)
                {
                    contentText.text = scriptedLines[lineIndex];
                    contentText.raycastTarget = false;
                }

                ApplyHeadPic(scriptedHeadPic);
                return;
            }

            if (lineIndex < 0 || lineIndex >= lines.Count)
            {
                return;
            }

            var line = lines[lineIndex];
            if (contentText != null)
            {
                contentText.text = line?.Content ?? string.Empty;
                contentText.raycastTarget = false;
            }

            ApplyHeadPic(line != null ? line.HeadPic : null);
        }

        private void ApplyHeadPic(string headPicKey)
        {
            if (headPicImage == null)
            {
                return;
            }

            var path = DialogConfigUtility.ResolveHeadPicPath(headPicKey);
            if (string.IsNullOrWhiteSpace(path))
            {
                headPicImage.enabled = false;
                return;
            }

            var sprite = GameplayResourceStore.LoadAsset<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning($"[DialogPanel] 立绘缺失：{path}");
                headPicImage.enabled = false;
                return;
            }

            headPicImage.sprite = sprite;
            headPicImage.preserveAspect = true;
            headPicImage.enabled = true;
            headPicImage.raycastTarget = false;
        }

        private void InvokeCompleteOnce()
        {
            if (completeInvoked)
            {
                return;
            }

            completeInvoked = true;
            var callback = Data?.OnComplete;
            if (Data != null)
            {
                Data.OnComplete = null;
            }

            callback?.Invoke();
        }

        private void EnsureNodes()
        {
            // Prefab：mask 在面板根；bg / txt / head 在 Root 下
            headPicImage ??= ResolveImage("Root/img_headPic", "img_headPic");
            contentText ??= ResolveComponent<TextMeshProUGUI>("Root/txt_content", "txt_content");
            bgButton ??= ResolveButton("Root/bg", "bg");
            maskButton ??= ResolveButton("mask", "mask");
        }
    }
}
