using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.UI
{
    /// <summary>
    /// 二楼贵客满意度：头顶一行 5 颗星（暂用程序生成贴图，之后可换成正式资源）。
    /// </summary>
    public class WorldFollowVipSatisfactionStarsView : MonoBehaviour
    {
        public const int StarCount = 5;
        private const float StarSize = 52f;
        private const float StarSpacing = 8f;

        private static Sprite litStarSprite;
        private static Sprite dimStarSprite;

        private Transform target;
        private Vector3 worldOffset;
        private RectTransform cachedRectTransform;
        private CanvasGroup cachedCanvasGroup;
        private readonly Image[] starImages = new Image[StarCount];
        private int litCount;
        private bool isVisible = true;

        public void BindTarget(Transform followTarget, Vector3 offset)
        {
            target = followTarget;
            worldOffset = offset;
        }

        public Transform FollowTarget => target;

        public void Initialize()
        {
            EnsureComponents();
            EnsureStarNodes();
            cachedCanvasGroup.blocksRaycasts = false;
            cachedCanvasGroup.interactable = false;
        }

        public Vector3 GetWorldAnchorPosition()
        {
            return target != null ? target.position + worldOffset : Vector3.zero;
        }

        public void SetAnchoredPosition(Vector2 position)
        {
            if (cachedRectTransform != null)
            {
                cachedRectTransform.anchoredPosition = position;
            }
        }

        public void SetVisible(bool visible)
        {
            if (isVisible == visible)
            {
                return;
            }

            isVisible = visible;
            if (cachedCanvasGroup == null)
            {
                return;
            }

            cachedCanvasGroup.alpha = visible ? 1f : 0f;
        }

        public void SetLitCount(int count)
        {
            litCount = Mathf.Clamp(count, 0, StarCount);
            EnsureStarNodes();
            for (var index = 0; index < StarCount; index++)
            {
                var image = starImages[index];
                if (image == null)
                {
                    continue;
                }

                var lit = index < litCount;
                image.sprite = lit ? GetLitSprite() : GetDimSprite();
                image.color = Color.white;
            }
        }

        private void EnsureComponents()
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

            var width = StarCount * StarSize + (StarCount - 1) * StarSpacing;
            cachedRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            cachedRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            cachedRectTransform.pivot = new Vector2(0.5f, 0.5f);
            cachedRectTransform.sizeDelta = new Vector2(width, StarSize);
            cachedRectTransform.localScale = Vector3.one;
            cachedRectTransform.localRotation = Quaternion.identity;
        }

        private void EnsureStarNodes()
        {
            EnsureComponents();
            var totalWidth = StarCount * StarSize + (StarCount - 1) * StarSpacing;
            var startX = -totalWidth * 0.5f + StarSize * 0.5f;
            for (var index = 0; index < StarCount; index++)
            {
                if (starImages[index] != null)
                {
                    continue;
                }

                var go = new GameObject($"star_{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(transform, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(StarSize, StarSize);
                rect.anchoredPosition = new Vector2(startX + index * (StarSize + StarSpacing), 0f);
                var image = go.GetComponent<Image>();
                image.raycastTarget = false;
                image.preserveAspect = true;
                starImages[index] = image;
            }
        }

        private static Sprite GetLitSprite()
        {
            return litStarSprite ??= CreateStarSprite(new Color(1f, 0.84f, 0.12f, 1f));
        }

        private static Sprite GetDimSprite()
        {
            return dimStarSprite ??= CreateStarSprite(new Color(0.62f, 0.62f, 0.66f, 1f));
        }

        private static Sprite CreateStarSprite(Color fill)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var cx = (size - 1) * 0.5f;
            var cy = (size - 1) * 0.5f;
            var outer = size * 0.46f;
            var inner = outer * 0.40f;
            var pixels = new Color[size * size];
            var outlineColor = new Color(0.12f, 0.10f, 0.08f, 1f);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var radius = StarRadius(dx, dy);
                    var edge = SampleStarEdge(dx, dy, outer, inner);
                    Color pixel;
                    if (radius <= edge)
                    {
                        pixel = fill;
                    }
                    else if (radius <= edge + 2.4f)
                    {
                        pixel = outlineColor;
                    }
                    else
                    {
                        pixel = Color.clear;
                    }

                    pixels[y * size + x] = pixel;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static float StarRadius(float dx, float dy)
        {
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static float SampleStarEdge(float dx, float dy, float outer, float inner)
        {
            var angle = Mathf.Atan2(dx, dy);
            if (angle < 0f)
            {
                angle += Mathf.PI * 2f;
            }

            const float slice = Mathf.PI * 2f / 5f;
            var local = (angle + slice * 0.5f) % slice;
            var t = local / slice;
            return t < 0.5f
                ? Mathf.Lerp(outer, inner, t * 2f)
                : Mathf.Lerp(inner, outer, (t - 0.5f) * 2f);
        }
    }
}
