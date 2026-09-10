using System.Collections.Generic;
using JN.Client;
using QFramework;
using UnityEngine;

namespace JN.Client.UI
{
    /// <summary>
    /// Town 世界建筑入口 HUD 面板数据。
    /// </summary>
    public class BuildingItemSceneControllerData : UIPanelData
    {
        public JN.Client.Scene.TileManager TileManager;
    }

    /// <summary>
    /// Town 世界锚点 HUD 容器，负责地块入口 UI 的创建、跟随与统一显隐。
    /// </summary>
    /// <summary>
    /// Town 世界锚点 HUD 容器。
    /// 负责地块入口 UI 的创建、跟随和统一显隐。
    /// </summary>
    public class BuildingItemSceneController : WorldAnchorHudPanelController<BuildingItemSceneControllerData, JN.Client.Scene.BuildingItemUI>
    {
        private const string BuildingItemPrefabPath = "Assets/Res/Resources/UI/Item/BuildingItem.prefab";

        private readonly Dictionary<int, JN.Client.Scene.BuildingItemUI> itemViews = new();
        private JN.Client.Scene.TileManager tileManager;

        /// <summary>
        /// 打开时绑定 TileManager 并应用显隐状态。
        /// </summary>
        protected override void OnPanelOpen(BuildingItemSceneControllerData data)
        {
            Signals.Get<AchievementProgressSignal>().RemoveListener(RefreshAllTiles);
            Signals.Get<AchievementProgressSignal>().AddListener(RefreshAllTiles);
            BindTileManager(data.TileManager);
            ApplyExternalVisibilityState();
        }

        /// <summary>
        /// 关闭时清空条目和场景引用。
        /// </summary>
        protected override void OnPanelClose()
        {
            Signals.Get<AchievementProgressSignal>().RemoveListener(RefreshAllTiles);
            ClearItems();
            tileManager = null;
            SceneCamera = null;
        }

        /// <summary>
        /// 每帧刷新所有建筑入口的跟随位置。
        /// </summary>
        private void LateUpdate()
        {
            if (!IsItemsVisible || tileManager == null || SceneCamera == null)
            {
                return;
            }

            foreach (var item in itemViews.Values)
            {
                if (item == null)
                {
                    continue;
                }

                RefreshItemPosition(item);
            }

            RefreshBuildingItemDepthSort();
        }

        /// <summary>
        /// 按与相机距离排序：越靠近相机的建筑 HUD（称号/头像）层级越高，避免被远处建筑遮挡。
        /// </summary>
        private void RefreshBuildingItemDepthSort()
        {
            EnsureContentRoot();
            if (ContentRoot == null || SceneCamera == null || itemViews.Count <= 1)
            {
                return;
            }

            var cameraPosition = SceneCamera.transform.position;
            var sortedItems = new List<JN.Client.Scene.BuildingItemUI>(itemViews.Count);
            foreach (var item in itemViews.Values)
            {
                if (item != null)
                {
                    sortedItems.Add(item);
                }
            }

            sortedItems.Sort((left, right) =>
            {
                var leftDepth = Vector3.Distance(cameraPosition, left.GetWorldSortPosition());
                var rightDepth = Vector3.Distance(cameraPosition, right.GetWorldSortPosition());
                return rightDepth.CompareTo(leftDepth);
            });

            for (var index = 0; index < sortedItems.Count; index++)
            {
                sortedItems[index].transform.SetSiblingIndex(index);
            }
        }

        /// <summary>
        /// 绑定新的 TileManager，并重建全部入口条目。
        /// </summary>
        public void BindTileManager(JN.Client.Scene.TileManager newTileManager)
        {
            if (newTileManager == null)
            {
                return;
            }

            tileManager = newTileManager;
            SceneCamera = newTileManager.GetSceneCamera();
            RebuildItems();
        }

        /// <summary>
        /// 刷新单个地块入口。
        /// </summary>
        public void RefreshTile(int tileId)
        {
            if (tileManager == null || !tileManager.AllTiles.TryGetValue(tileId, out var tile))
            {
                return;
            }

            if (!itemViews.TryGetValue(tileId, out var item) || item == null)
            {
                item = CreateItem(tile);
                if (item == null)
                {
                    return;
                }
            }

            item.SetData(tile.buildingInfo);
            RefreshItemPosition(item);
        }

        /// <summary>
        /// 刷新全部地块入口。
        /// </summary>
        public void RefreshAllTiles()
        {
            if (tileManager == null)
            {
                return;
            }

            foreach (var tileId in tileManager.AllTiles.Keys)
            {
                RefreshTile(tileId);
            }
        }

        /// <summary>
        /// 设置所有地块入口显隐。
        /// </summary>
        public void SetSceneItemsVisible(bool isVisible)
        {
            SetSceneItemsVisibleInternal(isVisible);
            if (isVisible)
            {
                RefreshAllTiles();
            }
        }

        /// <summary>
        /// 根据贷款演出状态切换地块入口显隐。
        /// </summary>
        protected override void ApplyExternalVisibilityState()
        {
            SetSceneItemsVisible(!TownStatusBarPanelController.IsOpeningLoanPresentationActive);
        }

        /// <summary>
        /// 重新创建全部地块入口。
        /// </summary>
        private void RebuildItems()
        {
            ClearItems();
            if (tileManager == null)
            {
                return;
            }

            foreach (var tile in tileManager.AllTiles.Values)
            {
                var item = CreateItem(tile);
                if (item == null)
                {
                    continue;
                }

                item.SetData(tile.buildingInfo);
                RefreshItemPosition(item);
            }
        }

        /// <summary>
        /// 为单个地块创建入口 UI。
        /// </summary>
        private JN.Client.Scene.BuildingItemUI CreateItem(JN.Client.Scene.Tile tile)
        {
            if (tile == null || tileManager == null)
            {
                return null;
            }

            var buildingItemPrefab = ResolveBuildingItemPrefab();
            if (buildingItemPrefab == null)
            {
                return null;
            }

            var itemObject = Instantiate(buildingItemPrefab, ContentRoot);
            var item = itemObject.GetComponent<JN.Client.Scene.BuildingItemUI>();
            if (item == null)
            {
                Destroy(itemObject);
                return null;
            }

            item.Bind(tile);
            itemViews[tile.tileId] = item;
            tile.linkedUI = item;
            return item;
        }

        /// <summary>
        /// 优先从资源路径解析建筑入口 prefab。
        /// </summary>
        private GameObject ResolveBuildingItemPrefab()
        {
            var prefab = GameplayResourceStore.LoadAsset<GameObject>(BuildingItemPrefabPath);
            if (prefab != null)
            {
                return prefab;
            }

            return tileManager != null ? tileManager.buildingUIPrefab : null;
        }

        /// <summary>
        /// 刷新单个入口的屏幕位置。
        /// </summary>
        private void RefreshItemPosition(JN.Client.Scene.BuildingItemUI item)
        {
            RefreshAnchoredItem(
                item,
                item.GetWorldAnchorPosition(),
                (view, position) => view.SetAnchoredPosition(position),
                (view, onScreen) => view.SetVisible(onScreen && view.ShouldDisplayHud()));
        }

        /// <summary>
        /// 清空所有地块入口条目。
        /// </summary>
        private void ClearItems()
        {
            if (tileManager != null)
            {
                foreach (var tile in tileManager.AllTiles.Values)
                {
                    if (tile != null)
                    {
                        tile.linkedUI = null;
                    }
                }
            }

            foreach (var item in itemViews.Values)
            {
                if (item != null)
                {
                    Destroy(item.gameObject);
                }
            }

            itemViews.Clear();
        }
    }
}
