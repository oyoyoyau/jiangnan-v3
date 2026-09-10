using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace JN.Client.UI
{
    public class TownIntroVideoPlayer : MonoBehaviour
    {
        private const string OverlayRootName = "TownIntroVideoOverlay";
        private const int OverlaySortingOrder = 1000001;

        public static TownIntroVideoPlayer ActiveInstance { get; private set; }

        [SerializeField] private VideoClip introClip;
        [SerializeField] private RectTransform overlayHost;
        [SerializeField] private Color backgroundColor = Color.black;

        private RectTransform m_OverlayRoot;
        private RawImage m_VideoImage;
        private Image m_BackgroundImage;
        private VideoPlayer m_VideoPlayer;
        private AudioSource m_AudioSource;
        private RenderTexture m_RenderTexture;
        private Action m_OnPlaybackFinished;
        private VideoClip m_RuntimeClip;
        private bool m_PauseOnLastFrame = true;

        public bool HasPlayableClip => ActiveClip != null;

        private VideoClip ActiveClip => m_RuntimeClip != null ? m_RuntimeClip : introClip;

        /// <summary>
        /// 设置本次要播放的视频资源。
        /// </summary>
        public void SetClip(VideoClip clip)
        {
            // 外部传入的视频只覆盖本次播放，避免污染 prefab 上的默认视频。
            m_RuntimeClip = clip;
        }

        /// <summary>
        /// 播放完停在最后一帧，适合开场贷款视频。
        /// </summary>
        public void PlayAndPauseOnLastFrame(Action onPausedAtLastFrame)
        {
            Play(onPausedAtLastFrame, true);
        }

        /// <summary>
        /// 播放完立即回调，适合建造后无缝切场景。
        /// </summary>
        public void PlayToEnd(Action onFinished)
        {
            Play(onFinished, false);
        }

        /// <summary>
        /// 按结束策略启动底层 VideoPlayer。
        /// </summary>
        private void Play(Action onFinished, bool pauseOnLastFrame)
        {
            var clip = ActiveClip;
            if (clip == null)
            {
                onFinished?.Invoke();
                return;
            }

            ActiveInstance = this;
            m_OnPlaybackFinished = onFinished;
            m_PauseOnLastFrame = pauseOnLastFrame;
            EnsureOverlay();
            EnsureVideoPlayer();
            EnsureRenderTexture(clip);

            m_OverlayRoot.gameObject.SetActive(true);
            m_OverlayRoot.SetAsLastSibling();
            m_BackgroundImage.color = backgroundColor;
            m_VideoImage.texture = m_RenderTexture;

            m_VideoPlayer.Stop();
            m_VideoPlayer.clip = clip;
            m_VideoPlayer.targetTexture = m_RenderTexture;
            m_VideoPlayer.isLooping = false;
            m_VideoPlayer.waitForFirstFrame = true;
            m_VideoPlayer.skipOnDrop = false;
            m_VideoPlayer.prepareCompleted -= HandlePrepareCompleted;
            m_VideoPlayer.loopPointReached -= HandleLoopPointReached;
            m_VideoPlayer.prepareCompleted += HandlePrepareCompleted;
            m_VideoPlayer.loopPointReached += HandleLoopPointReached;
            m_VideoPlayer.Prepare();
        }

        /// <summary>
        /// 停止播放并隐藏视频遮罩。
        /// </summary>
        public void HideIntro()
        {
            if (m_VideoPlayer != null)
            {
                m_VideoPlayer.prepareCompleted -= HandlePrepareCompleted;
                m_VideoPlayer.loopPointReached -= HandleLoopPointReached;
                m_VideoPlayer.Stop();
                m_VideoPlayer.targetTexture = null;
            }

            if (m_AudioSource != null)
            {
                m_AudioSource.Stop();
            }

            if (m_VideoImage != null)
            {
                m_VideoImage.texture = null;
            }

            if (m_OverlayRoot != null)
            {
                m_OverlayRoot.gameObject.SetActive(false);
            }

            m_OnPlaybackFinished = null;
            m_RuntimeClip = null;

            if (ActiveInstance == this)
            {
                ActiveInstance = null;
            }
        }

        /// <summary>
        /// 隐藏当前激活的视频遮罩。
        /// </summary>
        public static void HideActiveIntro()
        {
            ActiveInstance?.HideIntro();
        }

        /// <summary>
        /// 视频准备完成后开始播放。
        /// </summary>
        private void HandlePrepareCompleted(VideoPlayer source)
        {
            source.Play();
        }

        /// <summary>
        /// 视频到达结尾时按策略回调。
        /// </summary>
        private void HandleLoopPointReached(VideoPlayer source)
        {
            if (!m_PauseOnLastFrame)
            {
                var callback = m_OnPlaybackFinished;
                m_OnPlaybackFinished = null;
                callback?.Invoke();
                return;
            }

            StartCoroutine(PauseOnLastFrameAfterPlayback());
        }

        private IEnumerator PauseOnLastFrameAfterPlayback()
        {
            yield return null;

            if (m_VideoPlayer == null)
            {
                yield break;
            }

            if (m_VideoPlayer.frameCount > 0)
            {
                m_VideoPlayer.frame = (long)m_VideoPlayer.frameCount - 1;
            }

            m_VideoPlayer.Pause();

            var callback = m_OnPlaybackFinished;
            m_OnPlaybackFinished = null;
            callback?.Invoke();
        }

        private void EnsureOverlay()
        {
            if (m_OverlayRoot != null && m_VideoImage != null && m_BackgroundImage != null)
            {
                return;
            }

            var host = ResolveOverlayHost();
            var overlayRoot = host != null ? host.Find(OverlayRootName) : null;

            if (overlayRoot == null)
            {
                var overlayObject = new GameObject(OverlayRootName, typeof(RectTransform));
                overlayRoot = overlayObject.transform;
                overlayRoot.SetParent(host != null ? host : transform, false);
            }

            var overlayRect = (RectTransform)overlayRoot;
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.SetAsLastSibling();

            m_OverlayRoot = overlayRect;
            m_OverlayRoot.gameObject.SetActive(false);
            ConfigureOverlayCanvas(m_OverlayRoot);

            m_BackgroundImage = EnsureImageChild(overlayRoot, "Background");
            m_BackgroundImage.color = backgroundColor;
            m_BackgroundImage.raycastTarget = true;

            m_VideoImage = EnsureRawImageChild(overlayRoot, "Video");
            m_VideoImage.color = Color.white;
            m_VideoImage.raycastTarget = false;
        }

        private void ConfigureOverlayCanvas(RectTransform overlayRect)
        {
            if (overlayRect == null)
            {
                return;
            }

            var overlayCanvas = overlayRect.GetComponent<Canvas>();
            if (overlayCanvas == null)
            {
                overlayCanvas = overlayRect.gameObject.AddComponent<Canvas>();
            }

            var parentCanvas = overlayRect.GetComponentInParent<Canvas>();
            overlayCanvas.overrideSorting = true;
            overlayCanvas.sortingOrder = OverlaySortingOrder;
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            if (parentCanvas != null)
            {
                overlayCanvas.sortingLayerID = parentCanvas.sortingLayerID;
                overlayCanvas.worldCamera = null;
                overlayCanvas.planeDistance = 100f;
            }

            if (overlayRect.GetComponent<GraphicRaycaster>() == null)
            {
                overlayRect.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        private void EnsureVideoPlayer()
        {
            m_VideoPlayer ??= gameObject.GetComponent<VideoPlayer>();
            if (m_VideoPlayer == null)
            {
                m_VideoPlayer = gameObject.AddComponent<VideoPlayer>();
            }

            m_AudioSource ??= gameObject.GetComponent<AudioSource>();
            if (m_AudioSource == null)
            {
                m_AudioSource = gameObject.AddComponent<AudioSource>();
            }

            m_VideoPlayer.playOnAwake = false;
            m_VideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            m_VideoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            m_VideoPlayer.EnableAudioTrack(0, true);
            m_VideoPlayer.SetTargetAudioSource(0, m_AudioSource);
        }

        private void EnsureRenderTexture(VideoClip clip)
        {
            var width = clip != null && clip.width > 0 ? (int)clip.width : 1920;
            var height = clip != null && clip.height > 0 ? (int)clip.height : 1080;

            if (m_RenderTexture != null && m_RenderTexture.width == width && m_RenderTexture.height == height)
            {
                return;
            }

            if (m_RenderTexture != null)
            {
                m_RenderTexture.Release();
                Destroy(m_RenderTexture);
            }

            m_RenderTexture = new RenderTexture(width, height, 0)
            {
                name = "TownIntroVideoRenderTexture"
            };
            m_RenderTexture.Create();
        }

        private RectTransform ResolveOverlayHost()
        {
            if (overlayHost != null)
            {
                return overlayHost;
            }

            overlayHost = transform as RectTransform;
            if (overlayHost != null)
            {
                return overlayHost;
            }

            overlayHost = GetComponentInParent<RectTransform>();
            return overlayHost;
        }

        private static Image EnsureImageChild(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child == null)
            {
                var childObject = new GameObject(childName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                child = childObject.transform;
                child.SetParent(parent, false);
            }

            var rect = (RectTransform)child;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return child.GetComponent<Image>();
        }

        private static RawImage EnsureRawImageChild(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child == null)
            {
                var childObject = new GameObject(childName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                child = childObject.transform;
                child.SetParent(parent, false);
            }

            var rect = (RectTransform)child;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return child.GetComponent<RawImage>();
        }

        private void OnDestroy()
        {
            if (m_RenderTexture != null)
            {
                m_RenderTexture.Release();
                Destroy(m_RenderTexture);
                m_RenderTexture = null;
            }

            if (ActiveInstance == this)
            {
                ActiveInstance = null;
            }
        }
    }
}
