using System;
using QFramework;
using UnityEngine;

namespace AkiFramework.UI
{
    /// <summary>
    /// 基于 Resources 的 UIKit 适配层（为兼容历史命名沿用 AddressablesUIKit 类名）。
    /// 负责把面板加载逻辑接入 QFramework UIKit。
    /// </summary>
    public static class AddressablesUIKit
    {
        private static bool initialized;

        /// <summary>
        /// 初始化 UIKit 的 Addressables 面板加载配置。
        /// </summary>
        public static void Initialize(AddressablesUIKitConfig config = null)
        {
            if (initialized)
            {
                return;
            }

            config ??= AddressablesUIKitConfig.Default;
            UIKit.Config.PanelLoaderPool = new AddressablesPanelLoaderPool(config.AddressResolver);
            UIKit.Root.SetResolution(config.ReferenceWidth, config.ReferenceHeight, config.MatchWidthOrHeight);
            if (config.UseScreenSpaceOverlay)
            {
                UIKit.Root.ScreenSpaceOverlayRenderMode();
            }

            initialized = true;
        }

        /// <summary>
        /// 面板加载器对象池，为 UIKit 按需创建 Resources 面板加载器。
        /// </summary>
        private sealed class AddressablesPanelLoaderPool : AbstractPanelLoaderPool
        {
            private readonly Func<PanelSearchKeys, string> addressResolver;

            public AddressablesPanelLoaderPool(Func<PanelSearchKeys, string> addressResolver)
            {
                this.addressResolver = addressResolver;
            }

            protected override IPanelLoader CreatePanelLoader()
            {
                return new AddressablesPanelLoader(addressResolver);
            }
        }

        /// <summary>
        /// 单个面板的 Resources 加载器。
        /// 负责同步/异步加载和释放对应面板 prefab。
        /// </summary>
        private sealed class AddressablesPanelLoader : IPanelLoader
        {
            private readonly Func<PanelSearchKeys, string> addressResolver;
            private GameObject loadedPrefab;

            public AddressablesPanelLoader(Func<PanelSearchKeys, string> addressResolver)
            {
                this.addressResolver = addressResolver;
            }

            public GameObject LoadPanelPrefab(PanelSearchKeys panelSearchKeys)
            {
                var key = ResolveAddress(panelSearchKeys);
                loadedPrefab = LoadPanelByKey(key);
                return loadedPrefab;
            }

            public void LoadPanelPrefabAsync(PanelSearchKeys panelSearchKeys, Action<GameObject> onPanelPrefabLoad)
            {
                var key = ResolveAddress(panelSearchKeys);
                loadedPrefab = LoadPanelByKey(key);
                onPanelPrefabLoad?.Invoke(loadedPrefab);
            }

            public void Unload()
            {
                loadedPrefab = null;
            }

            private static string ResolveAddressDefault(PanelSearchKeys panelSearchKeys)
            {
                // 默认优先使用显式 GameObjName，其次再退回到面板类型名。
                return !string.IsNullOrWhiteSpace(panelSearchKeys.GameObjName)
                    ? panelSearchKeys.GameObjName
                    : panelSearchKeys.PanelType.Name;
            }

            private string ResolveAddress(PanelSearchKeys panelSearchKeys)
            {
                return addressResolver?.Invoke(panelSearchKeys) ?? ResolveAddressDefault(panelSearchKeys);
            }

            private static GameObject LoadPanelByKey(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return null;
                }

                // 1) 直接把 key 当作完整工程路径
                var byFullPath = GameplayResourceStore.LoadAsset<GameObject>(key);
                if (byFullPath != null)
                {
                    return byFullPath;
                }

                // 2) 按 UI 目录约定拼接路径（Panel/Window/Scene）
                var candidates = new[]
                {
                    $"Assets/Res/Prefabs/UI/Panel/{key}.prefab",
                    $"Assets/Res/Prefabs/UI/Window/{key}.prefab",
                    $"Assets/Res/Prefabs/UI/Scene/{key}.prefab",
                    $"Assets/Res/Resources/UI/Panel/{key}.prefab",
                    $"Assets/Res/Resources/UI/Window/{key}.prefab",
                    $"Assets/Res/Resources/UI/Scene/{key}.prefab",
                };

                for (var i = 0; i < candidates.Length; i++)
                {
                    var prefab = GameplayResourceStore.LoadAsset<GameObject>(candidates[i]);
                    if (prefab != null)
                    {
                        return prefab;
                    }
                }

                // 3) 再尝试把 key 当 Resources 相对路径（由调用方保证格式）
                return Resources.Load<GameObject>(key);
            }
        }
    }

    /// <summary>
    /// Addressables UIKit 初始化配置。
    /// </summary>
    public sealed class AddressablesUIKitConfig
    {
        public int ReferenceWidth = 1080;
        public int ReferenceHeight = 1920;
        public float MatchWidthOrHeight = 0.5f;
        public bool UseScreenSpaceOverlay = true;
        public Func<PanelSearchKeys, string> AddressResolver;

        public static AddressablesUIKitConfig Default => new AddressablesUIKitConfig
        {
            AddressResolver = panelSearchKeys => !string.IsNullOrWhiteSpace(panelSearchKeys.GameObjName)
                ? panelSearchKeys.GameObjName
                : panelSearchKeys.PanelType.Name
        };
    }
}
