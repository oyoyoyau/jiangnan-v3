using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.UI
{
    /// <summary>
    /// 拜访抢贵客：头顶说服进度条，每次点击往上填一截。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class WorldFollowVipPersuadeBarView : MonoBehaviour
    {
        private const float BarWidth = 240f;
        private const float BarHeight = 36f;
        private static readonly Color TrackColor = new(0.28f, 0.16f, 0.08f, 0.92f);
        private static readonly Color FillColor = new(0.98f, 0.72f, 0.18f, 1f);
        private static readonly Color RimColor = new(0.55f, 0.36f, 0.16f, 1f);

        private static Sprite barSprite;

        private Transform target;
        private Vector3 worldOffset;
        private Vector2 screenOffset;
        private RectTransform cachedRectTransform;
        private CanvasGroup cachedCanvasGroup;
        private Image fillImage;
        private Tween fillTween;
        private bool isVisible = true;
        private float displayedFill;

        public Transform FollowTarget => target;

        public void BindTarget(Transform followTarget, Vector3 offset)
        {
            target = followTarget;
            worldOffset = offset;
        }

        public void SetScreenOffset(Vector2 offset)
        {
            screenOffset = offset;
        }

        public void Initialize()
        {
            EnsureVisuals();
            if (cachedCanvasGroup != null)
            {
                cachedCanvasGroup.blocksRaycasts = false;
                cachedCanvasGroup.interactable = false;
            }

            SetFill(0f, animate: false);
        }

        public void SetFill(float normalized, bool animate)
        {
            EnsureVisuals();
            var targetFill = Mathf.Clamp01(normalized);
            fillTween?.Kill();
            if (!animate || fillImage == null)
            {
                displayedFill = targetFill;
                if (fillImage != null)
                {
                    fillImage.fillAmount = targetFill;
                }

                return;
            }

            fillTween = fillImage.DOFillAmount(targetFill, 0.28f)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .OnComplete(() => displayedFill = targetFill);
        }

        public Vector3 GetWorldAnchorPosition()
        {
            return target != null ? target.position + worldOffset : Vector3.zero;
        }

        public void SetAnchoredPosition(Vector2 position)
        {
            if (cachedRectTransform != null)
            {
                cachedRectTransform.anchoredPosition = position + screenOffset;
            }
        }

        public void SetVisible(bool visible)
        {
            if (isVisible == visible)
            {
                return;
            }

            isVisible = visible;
            if (cachedCanvasGroup != null)
            {
                cachedCanvasGroup.alpha = visible ? 1f : 0f;
            }
        }

        private void OnDestroy()
        {
            fillTween?.Kill();
            fillTween = null;
        }

        private void EnsureVisuals()
        {
            cachedRectTransform ??= transform as RectTransform;
            if (cachedRectTransform == null)
            {
                cachedRectTransform = gameObject.AddComponent<RectTransform>();
            }

            cachedCanvasGroup ??= GetComponent<CanvasGroup>();
            if (cachedCanvasGroup == null)
            {
                cachedCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            cachedRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            cachedRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            cachedRectTransform.pivot = new Vector2(0.5f, 0.5f);
            cachedRectTransform.sizeDelta = new Vector2(BarWidth + 8f, BarHeight + 8f);
            cachedRectTransform.localScale = Vector3.one;
            cachedRectTransform.localRotation = Quaternion.identity;

            if (fillImage != null)
            {
                return;
            }

            var sprite = GetBarSprite();
            CreateImage("rim", sprite, cachedRectTransform.sizeDelta, RimColor, Image.Type.Sliced);
            CreateImage("track", sprite, new Vector2(BarWidth, BarHeight), TrackColor, Image.Type.Sliced);
            fillImage = CreateImage("fill", sprite, new Vector2(BarWidth, BarHeight), FillColor, Image.Type.Filled);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 0f;
        }

        private Image CreateImage(string nodeName, Sprite sprite, Vector2 size, Color color, Image.Type imageType)
        {
            var go = new GameObject(nodeName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = imageType;
            image.raycastTarget = false;
            image.preserveAspect = false;
            return image;
        }

        private static Sprite GetBarSprite()
        {
            if (barSprite != null)
            {
                return barSprite;
            }

            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = Mathf.Min(x, size - 1 - x);
                    var dy = Mathf.Min(y, size - 1 - y);
                    var dist = Mathf.Min(dx, dy);
                    pixels[y * size + x] = dist >= 1f ? Color.white : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            barSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(8f, 8f, 8f, 8f));
            return barSprite;
        }
    }
}
