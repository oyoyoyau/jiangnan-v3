using TMPro;
using UnityEngine;

namespace JN.Client.Scene
{
    /// <summary>
    /// 负责地块购买价格的场景内显示，不走屏幕层 UI。
    /// </summary>
    public class LandPriceWorld : MonoBehaviour
    {
        private const string CoinSpritePath = "Assets/Res/Resources/Textures/UI/Icons 1/coin.png";
        private const string FontAssetPath = "Assets/Res/Fonts/AlibabaPuHuiTi SDF.asset";

        private static Sprite s_CoinSprite;
        private static TMP_FontAsset s_FontAsset;

        [SerializeField] private Vector3 worldOffset = new(0f, 2.1f, 0f);
        [SerializeField] private Vector3 worldScale = new(0.9f, 0.9f, 0.9f);
        [SerializeField] private Vector2 backgroundSize = new(2.8f, 0.9f);
        [SerializeField] private Vector2 coinLocalPosition = new(-0.75f, 0f);
        [SerializeField] private Vector2 coinLocalScale = new(0.48f, 0.48f);
        [SerializeField] private Vector2 textLocalPosition = new(0.2f, 0f);
        [SerializeField] private float textFontSize = 4.2f;
        [SerializeField] private SpriteRenderer backgroundRenderer;
        [SerializeField] private SpriteRenderer coinRenderer;
        [SerializeField] private TextMeshPro priceText;

        private Tile m_Tile;
        private Billboard m_Billboard;

        /// <summary>
        /// 当前对象是否已具备 prefab 里应有的关键节点绑定。
        /// </summary>
        public bool HasConfiguredBindings => backgroundRenderer != null && coinRenderer != null && priceText != null;

        /// <summary>
        /// 绑定所属地块并初始化可视节点。
        /// </summary>
        public void Bind(Tile tile)
        {
            m_Tile = tile;
            name = "LandPrice_World";
            EnsureVisuals();
            RefreshCamera();
            ApplyLayout();
        }

        /// <summary>
        /// 刷新显示内容与显隐。
        /// </summary>
        public void Refresh(bool visible, int price)
        {
            EnsureVisuals();
            RefreshCamera();
            ApplyLayout();
            gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            if (priceText != null)
            {
                priceText.text = price.ToString();
            }
        }

        private void OnEnable()
        {
            RefreshCamera();
            ApplyLayout();
        }

        /// <summary>
        /// 创建场景内价格节点。
        /// </summary>
        private void EnsureVisuals()
        {
            m_Billboard ??= gameObject.GetComponent<Billboard>() ?? gameObject.AddComponent<Billboard>();

            backgroundRenderer ??= FindRenderer("Background");
            if (backgroundRenderer == null)
            {
                var background = EnsureChild("Background");
                if (!background.gameObject.TryGetComponent(out backgroundRenderer))
                {
                    backgroundRenderer = background.gameObject.AddComponent<SpriteRenderer>();
                }
            }
            backgroundRenderer.sprite = CreateBackgroundSprite();
            backgroundRenderer.color = new Color(0.12f, 0.08f, 0.04f, 0.88f);
            backgroundRenderer.sortingOrder = 120;

            coinRenderer ??= FindRenderer("Coin");
            if (coinRenderer == null)
            {
                var coin = EnsureChild("Coin");
                if (!coin.gameObject.TryGetComponent(out coinRenderer))
                {
                    coinRenderer = coin.gameObject.AddComponent<SpriteRenderer>();
                }
            }
            coinRenderer.sprite = LoadCoinSprite();
            coinRenderer.sortingOrder = 121;

            priceText ??= FindText("PriceText");
            if (priceText == null)
            {
                var priceTextTransform = EnsureChild("PriceText");
                if (!priceTextTransform.gameObject.TryGetComponent(out priceText))
                {
                    priceText = priceTextTransform.gameObject.AddComponent<TextMeshPro>();
                }
            }
            priceText.font = LoadFontAsset();
            priceText.alignment = TextAlignmentOptions.MidlineLeft;
            priceText.fontSize = textFontSize;
            priceText.color = new Color32(255, 244, 204, 255);
            priceText.sortingOrder = 122;
            priceText.raycastTarget = false;
        }

        /// <summary>
        /// 根据配置更新世界内标牌的布局。
        /// </summary>
        private void ApplyLayout()
        {
            if (m_Tile != null)
            {
                transform.SetParent(m_Tile.transform, false);
            }

            transform.localPosition = worldOffset;
            transform.localScale = worldScale;
            transform.localRotation = Quaternion.identity;

            if (backgroundRenderer != null)
            {
                backgroundRenderer.transform.localPosition = Vector3.zero;
                backgroundRenderer.transform.localRotation = Quaternion.identity;
                backgroundRenderer.transform.localScale = new Vector3(backgroundSize.x, backgroundSize.y, 1f);
            }

            if (coinRenderer != null)
            {
                coinRenderer.transform.localPosition = new Vector3(coinLocalPosition.x, coinLocalPosition.y, -0.01f);
                coinRenderer.transform.localRotation = Quaternion.identity;
                coinRenderer.transform.localScale = new Vector3(coinLocalScale.x, coinLocalScale.y, 1f);
            }

            if (priceText != null)
            {
                priceText.transform.localPosition = new Vector3(textLocalPosition.x, textLocalPosition.y, -0.02f);
                priceText.transform.localRotation = Quaternion.identity;
                priceText.transform.localScale = Vector3.one * 0.1f;
                priceText.rectTransform.sizeDelta = new Vector2(10f, 3f);
                priceText.fontSize = textFontSize;
            }
        }

        /// <summary>
        /// 绑定场景相机朝向。
        /// </summary>
        private void RefreshCamera()
        {
            if (m_Billboard == null)
            {
                return;
            }

            m_Billboard.SceneCamera = TileManager.Instance != null
                ? TileManager.Instance.GetSceneCamera()
                : Camera.main;
        }

        private Transform EnsureChild(string childName)
        {
            var child = transform.Find(childName);
            if (child != null)
            {
                return child;
            }

            var childObject = new GameObject(childName);
            childObject.transform.SetParent(transform, false);
            return childObject.transform;
        }

        private SpriteRenderer FindRenderer(string childName)
        {
            var child = transform.Find(childName);
            return child != null ? child.GetComponent<SpriteRenderer>() : null;
        }

        private TextMeshPro FindText(string childName)
        {
            var child = transform.Find(childName);
            return child != null ? child.GetComponent<TextMeshPro>() : null;
        }

        private static Sprite LoadCoinSprite()
        {
            if (s_CoinSprite == null)
            {
                s_CoinSprite = GameplayResourceStore.LoadAsset<Sprite>(CoinSpritePath);
            }

            return s_CoinSprite;
        }

        private static TMP_FontAsset LoadFontAsset()
        {
            if (s_FontAsset == null)
            {
                s_FontAsset = GameplayResourceStore.LoadAsset<TMP_FontAsset>(FontAssetPath);
            }

            return s_FontAsset;
        }

        private static Sprite CreateBackgroundSprite()
        {
            var texture = Texture2D.whiteTexture;
            var rect = new Rect(0f, 0f, texture.width, texture.height);
            return Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
