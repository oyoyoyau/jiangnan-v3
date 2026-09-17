using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JN.Client.UI
{
    /// <summary>
    /// 二楼贵客上可乐限时：世界跟随秒表，指针扫过，下方显示剩余秒数。
    /// </summary>
    public class WorldFollowVipColaDeadlineTimerView : MonoBehaviour
    {
        private const float FaceSize = 78f;
        private const float NumberFontSize = 34f;
        private static readonly Color FaceRimColor = new(0.42f, 0.24f, 0.12f, 1f);
        private static readonly Color FaceFillColor = new(0.96f, 0.90f, 0.76f, 1f);
        private static readonly Color TickColor = new(0.28f, 0.16f, 0.08f, 1f);
        private static readonly Color PieColor = new(0.92f, 0.22f, 0.18f, 0.82f);
        private static readonly Color HandColor = new(0.78f, 0.08f, 0.08f, 1f);
        private static readonly Color NumberSafeColor = new(0.28f, 0.16f, 0.08f, 1f);
        private static readonly Color NumberUrgentColor = new(0.86f, 0.10f, 0.10f, 1f);

        private static Sprite faceSprite;
        private static Sprite pieSprite;
        private static Sprite handSprite;
        private static Sprite capSprite;

        private Transform target;
        private Vector3 worldOffset;
        private Vector2 screenOffset;
        private RectTransform cachedRectTransform;
        private CanvasGroup cachedCanvasGroup;
        private RectTransform handRect;
        private Image pieImage;
        private TMP_Text numberText;
        private float totalSeconds = 10f;
        private bool isVisible = true;

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
            EnsureComponents();
            EnsureVisuals();
            cachedCanvasGroup.blocksRaycasts = false;
            cachedCanvasGroup.interactable = false;
        }

        public void SetRemaining(float remainingSeconds)
        {
            SetDeadline(totalSeconds, remainingSeconds);
        }

        public void SetDeadline(float durationSeconds, float remainingSeconds)
        {
            totalSeconds = Mathf.Max(0.01f, durationSeconds);
            var remaining = Mathf.Clamp(remainingSeconds, 0f, totalSeconds);
            var ratio = remaining / totalSeconds;
            if (pieImage != null)
            {
                pieImage.fillAmount = ratio;
            }

            if (handRect != null)
            {
                handRect.localEulerAngles = new Vector3(0f, 0f, (1f - ratio) * -360f);
            }

            if (numberText != null)
            {
                var display = Mathf.CeilToInt(remaining);
                if (remaining <= 0f)
                {
                    display = 0;
                }

                numberText.text = display.ToString();
                numberText.color = remaining <= 3f ? NumberUrgentColor : NumberSafeColor;
            }
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

            cachedRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            cachedRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            cachedRectTransform.pivot = new Vector2(0.5f, 0.5f);
            cachedRectTransform.sizeDelta = new Vector2(FaceSize, FaceSize + 44f);
            cachedRectTransform.localScale = Vector3.one;
            cachedRectTransform.localRotation = Quaternion.identity;
        }

        private void EnsureVisuals()
        {
            EnsureComponents();
            if (handRect != null)
            {
                return;
            }

            CreateImage("face", GetFaceSprite(), new Vector2(0f, 16f), new Vector2(FaceSize, FaceSize), Color.white);
            pieImage = CreateImage("pie", GetPieSprite(), new Vector2(0f, 16f), new Vector2(FaceSize * 0.72f, FaceSize * 0.72f), PieColor);
            pieImage.type = Image.Type.Filled;
            pieImage.fillMethod = Image.FillMethod.Radial360;
            pieImage.fillOrigin = (int)Image.Origin360.Top;
            pieImage.fillClockwise = false;
            pieImage.fillAmount = 1f;

            var handImage = CreateImage("hand", GetHandSprite(), new Vector2(0f, 16f), new Vector2(10f, FaceSize * 0.42f), HandColor);
            handRect = handImage.rectTransform;
            handRect.pivot = new Vector2(0.5f, 0.12f);
            handRect.anchoredPosition = new Vector2(0f, 16f);

            CreateImage("cap", GetCapSprite(), new Vector2(0f, 16f), new Vector2(14f, 14f), FaceRimColor);

            var numberObject = new GameObject("seconds", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            numberObject.transform.SetParent(transform, false);
            var numberRect = numberObject.GetComponent<RectTransform>();
            numberRect.anchorMin = new Vector2(0.5f, 0.5f);
            numberRect.anchorMax = new Vector2(0.5f, 0.5f);
            numberRect.pivot = new Vector2(0.5f, 0.5f);
            numberRect.sizeDelta = new Vector2(80f, 40f);
            numberRect.anchoredPosition = new Vector2(0f, -46f);
            numberText = numberObject.GetComponent<TextMeshProUGUI>();
            numberText.fontSize = NumberFontSize;
            numberText.alignment = TextAlignmentOptions.Center;
            numberText.fontStyle = FontStyles.Bold;
            numberText.color = NumberSafeColor;
            numberText.raycastTarget = false;
            numberText.enableWordWrapping = false;
            if (TMP_Settings.defaultFontAsset != null)
            {
                numberText.font = TMP_Settings.defaultFontAsset;
            }

            numberText.text = "10";
        }

        private Image CreateImage(string nodeName, Sprite sprite, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var go = new GameObject(nodeName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        private static Sprite GetFaceSprite()
        {
            return faceSprite ??= CreateClockFaceSprite();
        }

        private static Sprite GetPieSprite()
        {
            return pieSprite ??= CreateCircleSprite(Color.white, 0.92f);
        }

        private static Sprite GetHandSprite()
        {
            return handSprite ??= CreateHandSprite();
        }

        private static Sprite GetCapSprite()
        {
            return capSprite ??= CreateCircleSprite(Color.white, 1f);
        }

        private static Sprite CreateClockFaceSprite()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var cx = (size - 1) * 0.5f;
            var cy = (size - 1) * 0.5f;
            var outer = size * 0.48f;
            var inner = size * 0.40f;
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var radius = Mathf.Sqrt(dx * dx + dy * dy);
                    Color pixel;
                    if (radius > outer + 1.2f)
                    {
                        pixel = Color.clear;
                    }
                    else if (radius >= inner)
                    {
                        pixel = FaceRimColor;
                    }
                    else
                    {
                        pixel = FaceFillColor;
                        var angle = Mathf.Atan2(dx, dy);
                        if (angle < 0f)
                        {
                            angle += Mathf.PI * 2f;
                        }

                        var tick = Mathf.RoundToInt(angle / (Mathf.PI * 2f / 12f));
                        var tickAngle = tick * (Mathf.PI * 2f / 12f);
                        var delta = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, tickAngle * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                        if (radius > inner * 0.72f && delta < 0.045f)
                        {
                            pixel = TickColor;
                        }
                    }

                    pixels[y * size + x] = pixel;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateCircleSprite(Color fill, float radiusRatio)
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
            var radius = size * 0.5f * radiusRatio;
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    pixels[y * size + x] = dx * dx + dy * dy <= radius * radius ? fill : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateHandSprite()
        {
            const int width = 16;
            const int height = 48;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color[width * height];
            var cx = (width - 1) * 0.5f;
            for (var y = 0; y < height; y++)
            {
                var t = y / (height - 1f);
                var half = Mathf.Lerp(1.2f, 5.2f, t);
                for (var x = 0; x < width; x++)
                {
                    pixels[y * width + x] = Mathf.Abs(x - cx) <= half ? Color.white : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.12f), 100f);
        }
    }
}
