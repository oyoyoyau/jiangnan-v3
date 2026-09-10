using System.Collections.Generic;
using JN.Client.Scene;
using QFramework;
using UnityEngine;

namespace JN.Client.UI
{
    /// <summary>
    /// Tavern 世界 HUD 统一布局参数（高度、缩放）。
    /// </summary>
    public static class TavernWorldRuntimeHudLayout
    {
        /// <summary>
        /// 桌位动作 HUD（点单/结账）世界高度偏移。
        /// </summary>
        public const float TableActionHeightOffset = 0.68f;

        /// <summary>
        /// 出餐台上菜按钮世界高度偏移（相对台面）。
        /// </summary>
        public const float FoodTableServeHeightOffset = 0.45f;

        /// <summary>
        /// 出餐台上菜气泡包裹节点缩放（NewOrderBtn prefab 根为 2x，0.7 使按钮视觉尺寸正常）。
        /// </summary>
        public const float FoodTableServeBubbleWrapperScale = 0.7f;

        /// <summary>
        /// 小二任务进度气泡世界高度偏移。
        /// </summary>
        public const float WaiterProgressHeightOffset = 1.45f;

        /// <summary>
        /// 小二/厨师打盹叫醒按钮世界高度偏移（低于任务进度条，避免飘太高）。
        /// </summary>
        public const float StaffNapButtonHeightOffset = 1f;

        /// <summary>
        /// 厨师做菜进度条世界高度偏移。
        /// </summary>
        public const float ChefProgressHeightOffset = 1.45f;

        /// <summary>
        /// 顾客等待进度气泡世界高度偏移。
        /// </summary>
        public const float CustomerWaitHeightOffset = 1.05f;

        /// <summary>
        /// 顾客反馈文字气泡世界高度偏移（略高于面部，仍低于耐心条）。
        /// </summary>
        public const float CustomerReviewHeightOffset = 0.92f;

        /// <summary>
        /// 桌位「被拉客」提示世界高度偏移（与桌位状态接近）。
        /// </summary>
        public const float TablePulledTipHeightOffset = 0.68f;
    }

    /// <summary>
    /// Tavern 世界运行时 HUD 面板数据。
    /// </summary>
    public class TavernWorldRuntimeHudPanelControllerData : UIPanelData
    {
    }

    /// <summary>
    /// Tavern 世界运行时 HUD 容器。
    /// 负责统一管理头顶进度条和状态图标。
    /// </summary>
    public class TavernWorldRuntimeHudPanelController : WorldAnchorHudPanelController<TavernWorldRuntimeHudPanelControllerData, TavernWorldRuntimeHudItemView>
    {
        private const string ItemPrefabPath = "Assets/Res/Resources/UI/Item/TavernWorldRuntimeHudItem.prefab";
        private const string WaitItemPrefabPath = "Assets/Res/Resources/UI/Item/TavernWorldWaitHudItem.prefab";
        private const string DrumUpButtonPrefabPath = "Assets/Res/Resources/UI/Buttons/DrumUpBtn.prefab";
        private const string MyDrumUpButtonPrefabPath = "Assets/Res/Resources/UI/Buttons/MyDrumUpBtn.prefab";
        private const string UpStairButtonPrefabPath = "Assets/Res/Resources/UI/Buttons/UpStairBtn.prefab";
        private const string VipGuestActionPrefabPath = "Assets/Res/Resources/UI/Runtime/VipGuestAction.prefab";
        private const string ReviewTipsPrefabPath = "Assets/Res/Resources/UI/Item/TavernReviewTipsUI.prefab";
        private const string TableActionPrefabPath = "Assets/Res/Resources/UI/Item/TableAreaUI.prefab";
        private const string EmployActionPrefabPath = "Assets/Res/Resources/UI/Item/EmployAreaUI.prefab";
        private const string NewOrderButtonPrefabPath = "Assets/Res/Resources/UI/Buttons/NewOrderBtn.prefab";
        private const string DrumUpTipsPrefabPath = "Assets/Res/Resources/UI/Item/TavernDrumUpTipsItem.prefab";

        private readonly List<TavernWorldRuntimeHudItemView> activeItems = new();
        private readonly List<TavernWorldWaitHudItemView> activeWaitItems = new();
        private readonly List<WorldFollowOrderButtonView> activeOrderButtons = new();
        private readonly List<VipGuestActionView> activeVipGuestActions = new();
        private readonly List<TavernReviewTipsView> activeReviewTips = new();
        private readonly Dictionary<int, TavernDrumUpTipsView> activePulledTips = new();
        private readonly Dictionary<int, TableAreaUI> activeTableItems = new();
        private readonly Dictionary<string, TableAreaUI> activePurchaseItems = new();
        private TableAreaUI activeInteriorWallExpandItem;
        private readonly Dictionary<string, EmployAreaUI> activeEmployItems = new();
        private MyDrumUpBtnView activeMyDrumUpButton;
        private GameObject itemPrefab;
        private GameObject waitItemPrefab;
        private GameObject reviewTipsPrefab;
        private GameObject tableActionPrefab;
        private GameObject employActionPrefab;
        private GameObject orderButtonPrefab;
        private GameObject drumUpTipsPrefab;

        /// <summary>
        /// 打开时确保内容根节点和显隐状态正确。
        /// </summary>
        protected override void OnPanelOpen(TavernWorldRuntimeHudPanelControllerData data)
        {
            EnsureContentRoot();
            ApplyExternalVisibilityState();
        }

        /// <summary>
        /// 关闭时清空所有运行时条目。
        /// </summary>
        protected override void OnPanelClose()
        {
            ClearAllItems();
            ClearAllOrderButtons();
            ClearAllVipGuestActions();
            ClearAllReviewTips();
            ClearAllPulledTips();
            ClearAllWaitItems();
            ClearAllTableItems();
            ClearAllPurchaseItems();
            ReleaseInteriorWallExpandHud();
            ClearAllEmployItems();
            ClearMyDrumUpButton();
        }

        /// <summary>
        /// 每帧刷新世界跟随条目的进度和位置。
        /// </summary>
        private void LateUpdate()
        {
            if (!IsItemsVisible)
            {
                return;
            }

            SceneCamera = Camera.main;
            if (SceneCamera == null)
            {
                return;
            }

            for (var index = activeItems.Count - 1; index >= 0; index--)
            {
                var item = activeItems[index];
                if (item == null)
                {
                    activeItems.RemoveAt(index);
                    continue;
                }

                item.Tick(Time.deltaTime);
                if (item.ShouldRelease)
                {
                    RemoveItemAt(index);
                    continue;
                }

                RefreshAnchoredItem(
                    item,
                    item.GetWorldAnchorPosition(),
                    (view, position) => view.SetAnchoredPosition(position),
                    (view, visible) => view.SetVisible(visible));
            }

            for (var index = activeWaitItems.Count - 1; index >= 0; index--)
            {
                var item = activeWaitItems[index];
                if (item == null)
                {
                    activeWaitItems.RemoveAt(index);
                    continue;
                }

                item.Tick(Time.deltaTime);
                if (item.ShouldRelease)
                {
                    RemoveWaitItemAt(index);
                    continue;
                }

                RefreshAnchoredItem(
                    item,
                    item.GetWorldAnchorPosition(),
                    (view, position) => view.SetAnchoredPosition(position),
                    (view, visible) => view.SetScreenVisible(visible));
            }

            RefreshTableActionItems();
            RefreshPurchaseActionItems();
            RefreshInteriorWallExpandItem();
            RefreshEmployActionItems();
            RefreshOrderButtonItems();
            RefreshMyDrumUpButtonItem();
            RefreshVipGuestActionItems();
            RefreshReviewTipItems();
            RefreshPulledTipItems();
        }

        /// <summary>
        /// 创建定时结束的进度条条目。
        /// </summary>
        public GameObject ShowTimedProgress(Transform target, float duration, Vector3 worldOffset, Sprite icon, string itemName)
        {
            if (target == null)
            {
                return null;
            }

            var item = CreateItem(itemName);
            if (item == null)
            {
                return null;
            }

            item.ConfigureTimedProgress(target, duration, worldOffset, icon);
            return item.gameObject;
        }

        /// <summary>
        /// 创建带点击交互的定时进度条条目。
        /// </summary>
        public GameObject ShowClickableTimedProgress(Transform target, float duration, Vector3 worldOffset, Sprite icon, System.Action onClick, string itemName)
        {
            if (target == null)
            {
                return null;
            }

            var item = CreateItem(itemName);
            if (item == null)
            {
                return null;
            }

            item.ConfigureClickableTimedProgress(target, duration, worldOffset, icon, onClick);
            return item.gameObject;
        }

        /// <summary>
        /// 创建由外部进度驱动的进度条条目。
        /// </summary>
        public GameObject ShowDynamicProgress(Transform target, Sprite icon, System.Func<float> progressProvider, Vector3 worldOffset, string itemName)
        {
            if (target == null)
            {
                return null;
            }

            var item = CreateItem(itemName);
            if (item == null)
            {
                return null;
            }

            item.ConfigureDynamicProgress(target, progressProvider, worldOffset, icon);
            return item.gameObject;
        }

        /// <summary>
        /// 创建持久动态进度条（满进度后不自动回收，由调用方 Release）。
        /// </summary>
        public GameObject ShowPersistentDynamicProgress(
            Transform target,
            Sprite icon,
            System.Func<float> progressProvider,
            Vector3 worldOffset,
            string itemName)
        {
            if (target == null)
            {
                return null;
            }

            var item = CreateItem(itemName);
            if (item == null)
            {
                return null;
            }

            item.ConfigurePersistentDynamicProgress(target, progressProvider, worldOffset, icon);
            return item.gameObject;
        }

        /// <summary>
        /// 创建由外部进度驱动且可点击的进度条条目。
        /// </summary>
        public GameObject ShowClickableDynamicProgress(Transform target, Sprite icon, System.Func<float> progressProvider, Vector3 worldOffset, System.Action onClick, string itemName)
        {
            if (target == null)
            {
                return null;
            }

            var item = CreateItem(itemName);
            if (item == null)
            {
                return null;
            }

            item.ConfigureClickableDynamicProgress(target, progressProvider, worldOffset, icon, onClick);
            return item.gameObject;
        }

        /// <summary>
        /// 创建顾客等待 HUD 条目。
        /// </summary>
        public TavernWorldWaitHudItemView CreateWaitHudItem(
            Transform target,
            Vector3 worldOffset,
            string itemName,
            Sprite icon,
            CustomerWaitHudState state)
        {
            if (target == null)
            {
                return null;
            }

            var item = CreateWaitItem(itemName);
            if (item == null)
            {
                return null;
            }

            item.BindTarget(target, worldOffset);
            item.Configure(state, icon);
            return item;
        }

        /// <summary>
        /// 释放顾客等待 HUD 条目。
        /// </summary>
        public void ReleaseWaitHudItem(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            for (var index = activeWaitItems.Count - 1; index >= 0; index--)
            {
                var item = activeWaitItems[index];
                if (item == null)
                {
                    activeWaitItems.RemoveAt(index);
                    continue;
                }

                if (item.gameObject != root)
                {
                    continue;
                }

                if (!item.ShouldRelease)
                {
                    item.MarkForRelease();
                }

                return;
            }

            Destroy(root);
        }

        /// <summary>
        /// 创建拜访拉客按钮（DrumUpBtn），跟随客人头顶。
        /// </summary>
        /// <param name="capacityInsufficient">容量不够时置灰。</param>
        public GameObject ShowDrumUpButton(
            Transform target,
            Vector3 worldOffset,
            System.Action onClick,
            bool capacityInsufficient = false)
        {
            if (target == null)
            {
                return null;
            }

            EnsureContentRoot();
            var prefab = GameplayResourceStore.LoadAsset<GameObject>(DrumUpButtonPrefabPath);
            if (prefab == null || ContentRoot == null)
            {
                return null;
            }

            var wrapperObject = new GameObject("DrumUpFollow", typeof(RectTransform));
            wrapperObject.transform.SetParent(ContentRoot, false);
            var item = wrapperObject.AddComponent<WorldFollowOrderButtonView>();

            var buttonObject = Instantiate(prefab, wrapperObject.transform, false);
            buttonObject.name = "DrumUpBtn";
            var orderButton = buttonObject.GetComponent<TableOrderButtonUI>();
            if (orderButton == null)
            {
                Destroy(wrapperObject);
                return null;
            }

            item.Initialize(orderButton, 1f);
            item.BindTarget(target, worldOffset);
            orderButton.ShowDrumUpPullAction(onClick, capacityInsufficient);
            TrackOrderButton(item);
            return wrapperObject;
        }

        /// <summary>
        /// 创建上楼按钮（UpStairBtn），跟随楼梯建造挂点。
        /// </summary>
        public GameObject ShowUpStairButton(Transform target, Vector3 worldOffset, System.Action onClick)
        {
            if (target == null)
            {
                return null;
            }

            EnsureContentRoot();
            var prefab = GameplayResourceStore.LoadAsset<GameObject>(UpStairButtonPrefabPath);
            if (prefab == null || ContentRoot == null)
            {
                return null;
            }

            var wrapperObject = new GameObject("UpStairFollow", typeof(RectTransform));
            wrapperObject.transform.SetParent(ContentRoot, false);
            var item = wrapperObject.AddComponent<WorldFollowOrderButtonView>();

            var buttonObject = Instantiate(prefab, wrapperObject.transform, false);
            buttonObject.name = "UpStairBtn";
            var orderButton = buttonObject.GetComponent<TableOrderButtonUI>();
            if (orderButton == null)
            {
                Destroy(wrapperObject);
                return null;
            }

            item.Initialize(orderButton, 1f);
            item.BindTarget(target, worldOffset);
            // Awake 的 ResetVisuals 会藏 DrumUp；此处强制显示「上楼」。
            orderButton.ShowUpStairAction(onClick);
            TrackOrderButton(item);
            return wrapperObject;
        }

        /// <summary>
        /// 创建场景拉客按钮（MyDrumUpBtn），跟随「轿子建造」挂点。
        /// </summary>
        public GameObject ShowMyDrumUpButton(Transform target, Vector3 worldOffset, System.Action onClick)
        {
            if (target == null)
            {
                return null;
            }

            EnsureContentRoot();
            if (activeMyDrumUpButton != null)
            {
                activeMyDrumUpButton.Bind(target, worldOffset, onClick);
                return activeMyDrumUpButton.gameObject;
            }

            var prefab = GameplayResourceStore.LoadAsset<GameObject>(MyDrumUpButtonPrefabPath);
            if (prefab == null || ContentRoot == null)
            {
                Debug.LogWarning($"[TavernWorldRuntimeHud] 缺少拉客按钮预制体：{MyDrumUpButtonPrefabPath}");
                return null;
            }

            var buttonObject = Instantiate(prefab, ContentRoot, false);
            buttonObject.name = "MyDrumUpFollow";
            var item = buttonObject.GetComponent<MyDrumUpBtnView>();
            if (item == null)
            {
                item = buttonObject.AddComponent<MyDrumUpBtnView>();
            }

            item.Bind(target, worldOffset, onClick);
            activeMyDrumUpButton = item;
            return buttonObject;
        }

        /// <summary>销毁场景拉客按钮。</summary>
        public void ClearMyDrumUpButton()
        {
            if (activeMyDrumUpButton == null)
            {
                return;
            }

            if (activeMyDrumUpButton.gameObject != null)
            {
                Destroy(activeMyDrumUpButton.gameObject);
            }

            activeMyDrumUpButton = null;
        }

        /// <summary>
        /// 创建贵客头顶大堂/包厢气泡，跟随贵客。
        /// </summary>
        public GameObject ShowVipGuestAction(
            Transform target,
            Vector3 worldOffset,
            bool usePrivateRoom,
            System.Action onClick,
            bool privateRoomLocked = false)
        {
            if (target == null)
            {
                return null;
            }

            EnsureContentRoot();
            var prefab = GameplayResourceStore.LoadAsset<GameObject>(VipGuestActionPrefabPath);
            if (prefab == null || ContentRoot == null)
            {
                return null;
            }

            var itemObject = Instantiate(prefab, ContentRoot, false);
            itemObject.name = "VipGuestActionFollow";
            var item = itemObject.GetComponent<VipGuestActionView>();
            if (item == null)
            {
                item = itemObject.AddComponent<VipGuestActionView>();
            }

            item.Bind(target, worldOffset, usePrivateRoom, onClick, privateRoomLocked);
            TrackVipGuestAction(item);
            return itemObject;
        }

        /// <summary>
        /// 创建顾客反馈文字气泡；同目标已有气泡时替换。durationSeconds &lt; 0 常驻到模型消失。
        /// </summary>
        public GameObject ShowCustomerReviewTip(
            Transform target,
            string content,
            Vector3 worldOffset,
            float durationSeconds = -1f)
        {
            if (target == null || string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            ReleaseCustomerReviewTip(target);

            EnsureContentRoot();
            if (reviewTipsPrefab == null)
            {
                reviewTipsPrefab = GameplayResourceStore.LoadAsset<GameObject>(ReviewTipsPrefabPath);
            }

            if (reviewTipsPrefab == null || ContentRoot == null)
            {
                Debug.LogWarning($"[TavernWorldRuntimeHud] 缺少反馈气泡预制体：{ReviewTipsPrefabPath}");
                return null;
            }

            var itemObject = Instantiate(reviewTipsPrefab, ContentRoot, false);
            itemObject.name = "CustomerReviewTipFollow";
            var item = itemObject.GetComponent<TavernReviewTipsView>();
            if (item == null)
            {
                item = itemObject.AddComponent<TavernReviewTipsView>();
            }

            item.Bind(target, worldOffset, content, durationSeconds);
            TrackReviewTip(item);
            return itemObject;
        }

        /// <summary>释放指定顾客上的反馈气泡。</summary>
        public void ReleaseCustomerReviewTip(Transform target)
        {
            if (target == null)
            {
                return;
            }

            for (var index = activeReviewTips.Count - 1; index >= 0; index--)
            {
                var item = activeReviewTips[index];
                if (item == null)
                {
                    activeReviewTips.RemoveAt(index);
                    continue;
                }

                if (item.FollowTarget != target)
                {
                    continue;
                }

                RemoveReviewTipAt(index);
            }
        }

        /// <summary>
        /// 桌位「客人被拉走」提示；同桌已有则先替换。
        /// </summary>
        public TavernDrumUpTipsView ShowTablePulledTip(
            Transform tableTarget,
            int tableId,
            Vector3 worldOffset,
            int headIconId,
            string pullerName,
            System.Action onClick = null,
            string displayCaption = null,
            bool useSelfHeadIcon = false,
            bool clickEnabled = true)
        {
            if (tableTarget == null || tableId <= 0)
            {
                return null;
            }

            ReleaseTablePulledTip(tableId);
            EnsureContentRoot();
            drumUpTipsPrefab ??= GameplayResourceStore.LoadAsset<GameObject>(DrumUpTipsPrefabPath);
            if (drumUpTipsPrefab == null || ContentRoot == null)
            {
                Debug.LogWarning($"[TavernWorldRuntimeHud] 缺少被拉客提示预制体：{DrumUpTipsPrefabPath}");
                return null;
            }

            var itemObject = Instantiate(drumUpTipsPrefab, ContentRoot, false);
            itemObject.name = $"TablePulledTip_{tableId}";
            var item = itemObject.GetComponent<TavernDrumUpTipsView>();
            if (item == null)
            {
                item = itemObject.AddComponent<TavernDrumUpTipsView>();
            }

            item.Bind(
                tableTarget,
                tableId,
                worldOffset,
                headIconId,
                pullerName,
                onClick,
                displayCaption,
                useSelfHeadIcon,
                clickEnabled);
            activePulledTips[tableId] = item;
            return item;
        }

        /// <summary>释放指定桌位的被拉客提示。</summary>
        public void ReleaseTablePulledTip(int tableId)
        {
            if (tableId <= 0 || !activePulledTips.TryGetValue(tableId, out var item))
            {
                return;
            }

            activePulledTips.Remove(tableId);
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }

        /// <summary>是否正在显示该桌被拉客提示。</summary>
        public bool HasTablePulledTip(int tableId)
        {
            return tableId > 0
                   && activePulledTips.TryGetValue(tableId, out var item)
                   && item != null
                   && !item.ShouldRelease;
        }

        /// <summary>
        /// 销毁全部 UpStairFollow 上楼按钮。
        /// </summary>
        public void ClearUpStairButtons()
        {
            for (var index = activeOrderButtons.Count - 1; index >= 0; index--)
            {
                var item = activeOrderButtons[index];
                if (item == null)
                {
                    activeOrderButtons.RemoveAt(index);
                    continue;
                }

                if (item.gameObject == null || item.gameObject.name != "UpStairFollow")
                {
                    continue;
                }

                RemoveOrderButtonAt(index);
            }
        }

        /// <summary>
        /// 创建出餐台等世界跟随的上菜按钮（NewOrderBtn）。
        /// </summary>
        public GameObject ShowFoodTableServeBubble(Transform target, Sprite icon, System.Action onClick, Vector3 worldOffset)
        {
            if (target == null || icon == null)
            {
                return null;
            }

            var item = CreateOrderButtonItem("FoodTableServeBubble", TavernWorldRuntimeHudLayout.FoodTableServeBubbleWrapperScale);
            if (item == null)
            {
                return null;
            }

            item.BindTarget(target, worldOffset);
            item.ConfigureServe(icon, onClick);
            return item.gameObject;
        }

        /// <summary>
        /// 创建可点击的状态图标条目。
        /// </summary>
        public GameObject ShowStateIcon(Transform target, Sprite icon, System.Action onClick, Vector3 worldOffset, string itemName)
        {
            if (target == null || icon == null)
            {
                return null;
            }

            var item = CreateItem(itemName);
            if (item == null)
            {
                return null;
            }

            item.ConfigureStateIcon(target, icon, onClick, worldOffset);
            return item.gameObject;
        }

        /// <summary>
        /// 确保指定桌位拥有统一托管的头顶 HUD。
        /// </summary>
        public TableAreaUI EnsureTableActionHud(TableArea table)
        {
            if (table == null)
            {
                return null;
            }

            EnsureContentRoot();
            tableActionPrefab ??= GameplayResourceStore.LoadAsset<GameObject>(TableActionPrefabPath);
            if (tableActionPrefab == null || ContentRoot == null)
            {
                return null;
            }

            var tableId = table.GetTableIdFromInternal();
            if (activeTableItems.TryGetValue(tableId, out var existingItem) && existingItem != null)
            {
                existingItem.BindTable(table);
                existingItem.InitBinding(table.transform);
                return existingItem;
            }

            var itemObject = Instantiate(tableActionPrefab, ContentRoot, false);
            itemObject.name = $"TableActionHud_{tableId}";
            var tableUi = itemObject.GetComponent<TableAreaUI>();
            if (tableUi == null)
            {
                Destroy(itemObject);
                return null;
            }

            tableUi.BindTable(table);
            tableUi.InitBinding(table.transform);
            activeTableItems[tableId] = tableUi;
            return tableUi;
        }

        /// <summary>
        /// 释放指定桌位的头顶 HUD。
        /// </summary>
        public void ReleaseTableActionHud(TableArea table)
        {
            if (table == null)
            {
                return;
            }

            ReleaseTableActionHud(table.GetTableIdFromInternal());
        }

        /// <summary>
        /// 确保指定引导建造点拥有统一托管的头顶 HUD。
        /// </summary>
        public TableAreaUI EnsurePurchaseActionHud(string purchaseKey, Transform target, System.Action onPurchase)
        {
            if (string.IsNullOrWhiteSpace(purchaseKey) || target == null)
            {
                return null;
            }

            EnsureContentRoot();
            tableActionPrefab ??= GameplayResourceStore.LoadAsset<GameObject>(TableActionPrefabPath);
            if (tableActionPrefab == null || ContentRoot == null)
            {
                return null;
            }

            if (activePurchaseItems.TryGetValue(purchaseKey, out var existingItem) && existingItem != null)
            {
                existingItem.InitBinding(target);
                existingItem.BindPurchaseAction(onPurchase);
                return existingItem;
            }

            var itemObject = Instantiate(tableActionPrefab, ContentRoot, false);
            itemObject.name = $"PurchaseActionHud_{purchaseKey}";
            var purchaseUi = itemObject.GetComponent<TableAreaUI>();
            if (purchaseUi == null)
            {
                Destroy(itemObject);
                return null;
            }

            purchaseUi.InitBinding(target);
            purchaseUi.BindPurchaseAction(onPurchase);
            activePurchaseItems[purchaseKey] = purchaseUi;
            return purchaseUi;
        }

        /// <summary>
        /// 确保墙体扩建挂点拥有专用头顶 HUD（group_expand）。
        /// </summary>
        public TableAreaUI EnsureInteriorWallExpandHud(Transform target, int cost, System.Action onExpand)
        {
            if (target == null)
            {
                return null;
            }

            EnsureContentRoot();
            tableActionPrefab ??= GameplayResourceStore.LoadAsset<GameObject>(TableActionPrefabPath);
            if (tableActionPrefab == null || ContentRoot == null)
            {
                return null;
            }

            if (activeInteriorWallExpandItem != null)
            {
                activeInteriorWallExpandItem.InitBinding(target);
                activeInteriorWallExpandItem.HideStatus();
                // 世界偏移由 RefreshInteriorExpandHud → SetWorldOffset 统一写入，这里先清掉桌位默认 Y+0.68。
                activeInteriorWallExpandItem.SetWorldOffset(Vector3.zero);
                activeInteriorWallExpandItem.SetInteriorWallExpandPrompt(true, cost, onExpand);
                return activeInteriorWallExpandItem;
            }

            var itemObject = Instantiate(tableActionPrefab, ContentRoot, false);
            itemObject.name = "InteriorWallExpandHud";
            var expandUi = itemObject.GetComponent<TableAreaUI>();
            if (expandUi == null)
            {
                Destroy(itemObject);
                return null;
            }

            expandUi.InitBinding(target);
            expandUi.HideStatus();
            expandUi.SetWorldOffset(Vector3.zero);
            expandUi.SetInteriorWallExpandPrompt(true, cost, onExpand);
            activeInteriorWallExpandItem = expandUi;
            return expandUi;
        }

        /// <summary>
        /// 释放墙体扩建头顶 HUD。
        /// </summary>
        public void ReleaseInteriorWallExpandHud()
        {
            if (activeInteriorWallExpandItem == null)
            {
                return;
            }

            Destroy(activeInteriorWallExpandItem.gameObject);
            activeInteriorWallExpandItem = null;
        }

        /// <summary>
        /// 释放指定引导建造点的头顶 HUD。
        /// </summary>
        public void ReleasePurchaseActionHud(string purchaseKey)
        {
            if (string.IsNullOrWhiteSpace(purchaseKey))
            {
                return;
            }

            ReleasePurchaseActionHudInternal(purchaseKey);
        }

        /// <summary>
        /// 确保指定招聘地块拥有统一托管的头顶 HUD。
        /// </summary>
        public EmployAreaUI EnsureEmployActionHud(
            string employKey,
            Transform target,
            cfg.StaffPosition position,
            int cost,
            System.Action onEmploy)
        {
            if (string.IsNullOrWhiteSpace(employKey) || target == null)
            {
                return null;
            }

            EnsureContentRoot();
            employActionPrefab ??= GameplayResourceStore.LoadAsset<GameObject>(EmployActionPrefabPath);
            if (employActionPrefab == null || ContentRoot == null)
            {
                return null;
            }

            if (activeEmployItems.TryGetValue(employKey, out var existingItem) && existingItem != null)
            {
                existingItem.BindEmploy(employKey, target, position, cost, onEmploy);
                return existingItem;
            }

            var itemObject = Instantiate(employActionPrefab, ContentRoot, false);
            itemObject.name = $"EmployActionHud_{employKey}";
            var employUi = itemObject.GetComponent<EmployAreaUI>();
            if (employUi == null)
            {
                employUi = itemObject.AddComponent<EmployAreaUI>();
            }

            employUi.BindEmploy(employKey, target, position, cost, onEmploy);
            activeEmployItems[employKey] = employUi;
            return employUi;
        }

        /// <summary>
        /// 兼容旧接口。
        /// </summary>
        public EmployAreaUI EnsureEmployActionHud(string employKey, Transform target, System.Action onEmploy)
        {
            if (string.IsNullOrWhiteSpace(employKey) || target == null)
            {
                return null;
            }

            EnsureContentRoot();
            employActionPrefab ??= GameplayResourceStore.LoadAsset<GameObject>(EmployActionPrefabPath);
            if (employActionPrefab == null || ContentRoot == null)
            {
                return null;
            }

            if (activeEmployItems.TryGetValue(employKey, out var existingItem) && existingItem != null)
            {
                existingItem.BindEmploy(employKey, target, onEmploy);
                return existingItem;
            }

            var itemObject = Instantiate(employActionPrefab, ContentRoot, false);
            itemObject.name = $"EmployActionHud_{employKey}";
            var employUi = itemObject.GetComponent<EmployAreaUI>();
            if (employUi == null)
            {
                employUi = itemObject.AddComponent<EmployAreaUI>();
            }

            employUi.BindEmploy(employKey, target, onEmploy);
            activeEmployItems[employKey] = employUi;
            return employUi;
        }

        /// <summary>
        /// 释放指定招聘地块的头顶 HUD。
        /// </summary>
        public void ReleaseEmployActionHud(string employKey)
        {
            if (string.IsNullOrWhiteSpace(employKey))
            {
                return;
            }

            ReleaseEmployActionHudInternal(employKey);
        }

        /// <summary>
        /// 主动释放某个运行时条目。
        /// </summary>
        public void ReleaseItem(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            if (activeMyDrumUpButton != null && activeMyDrumUpButton.gameObject == root)
            {
                ClearMyDrumUpButton();
                return;
            }

            for (var index = activeOrderButtons.Count - 1; index >= 0; index--)
            {
                var item = activeOrderButtons[index];
                if (item == null)
                {
                    activeOrderButtons.RemoveAt(index);
                    continue;
                }

                if (item.gameObject != root)
                {
                    continue;
                }

                RemoveOrderButtonAt(index);
                return;
            }

            for (var index = activeVipGuestActions.Count - 1; index >= 0; index--)
            {
                var item = activeVipGuestActions[index];
                if (item == null)
                {
                    activeVipGuestActions.RemoveAt(index);
                    continue;
                }

                if (item.gameObject != root)
                {
                    continue;
                }

                RemoveVipGuestActionAt(index);
                return;
            }

            for (var index = activeItems.Count - 1; index >= 0; index--)
            {
                var item = activeItems[index];
                if (item == null)
                {
                    activeItems.RemoveAt(index);
                    continue;
                }

                if (item.gameObject != root)
                {
                    continue;
                }

                RemoveItemAt(index);
                return;
            }

            Destroy(root);
        }

        /// <summary>
        /// 获取或创建一个可复用的运行时条目。
        /// </summary>
        public TavernWorldRuntimeHudItemView GetOrCreateItemView(GameObject existingRoot, string itemName)
        {
            if (existingRoot != null)
            {
                var existingItem = existingRoot.GetComponent<TavernWorldRuntimeHudItemView>();
                if (existingItem == null)
                {
                    existingItem = existingRoot.AddComponent<TavernWorldRuntimeHudItemView>();
                }

                TrackItem(existingItem);
                return existingItem;
            }

            return CreateItem(itemName);
        }

        /// <summary>
        /// 运行时 HUD 默认始终跟随 Tavern HUD 显示。
        /// </summary>
        protected override void ApplyExternalVisibilityState()
        {
            SetSceneItemsVisibleInternal(true);
        }

        private void TrackItem(TavernWorldRuntimeHudItemView item)
        {
            if (item == null || activeItems.Contains(item))
            {
                return;
            }

            activeItems.Add(item);
        }

        /// <summary>
        /// 实例化一个新的运行时条目。
        /// </summary>
        private TavernWorldRuntimeHudItemView CreateItem(string itemName)
        {
            EnsureContentRoot();
            itemPrefab ??= GameplayResourceStore.LoadAsset<GameObject>(ItemPrefabPath);
            if (itemPrefab == null || ContentRoot == null)
            {
                return null;
            }

            var itemObject = Instantiate(itemPrefab, ContentRoot, false);
            itemObject.name = itemName;
            var item = itemObject.GetComponent<TavernWorldRuntimeHudItemView>();
            if (item == null)
            {
                item = itemObject.AddComponent<TavernWorldRuntimeHudItemView>();
            }

            TrackItem(item);
            return item;
        }

        private WorldFollowOrderButtonView CreateOrderButtonItem(string itemName, float wrapperScale = 1f)
        {
            EnsureContentRoot();
            orderButtonPrefab ??= GameplayResourceStore.LoadAsset<GameObject>(NewOrderButtonPrefabPath);
            if (orderButtonPrefab == null || ContentRoot == null)
            {
                return null;
            }

            var wrapperObject = new GameObject(itemName, typeof(RectTransform));
            wrapperObject.transform.SetParent(ContentRoot, false);
            var item = wrapperObject.AddComponent<WorldFollowOrderButtonView>();

            var buttonObject = Instantiate(orderButtonPrefab, wrapperObject.transform, false);
            buttonObject.name = "NewOrderBtn";
            var orderButton = buttonObject.GetComponent<TableOrderButtonUI>();
            if (orderButton == null)
            {
                Destroy(wrapperObject);
                return null;
            }

            item.Initialize(orderButton, wrapperScale);
            TrackOrderButton(item);
            return item;
        }

        private void TrackOrderButton(WorldFollowOrderButtonView item)
        {
            if (item == null || activeOrderButtons.Contains(item))
            {
                return;
            }

            activeOrderButtons.Add(item);
        }

        private void RemoveOrderButtonAt(int index)
        {
            var item = activeOrderButtons[index];
            activeOrderButtons.RemoveAt(index);
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }

        private void ClearAllOrderButtons()
        {
            foreach (var item in activeOrderButtons)
            {
                if (item != null)
                {
                    Destroy(item.gameObject);
                }
            }

            activeOrderButtons.Clear();
        }

        /// <summary>
        /// 每帧同步世界跟随的上菜按钮位置与显隐。
        /// </summary>
        private void RefreshOrderButtonItems()
        {
            for (var index = activeOrderButtons.Count - 1; index >= 0; index--)
            {
                var item = activeOrderButtons[index];
                if (item == null)
                {
                    activeOrderButtons.RemoveAt(index);
                    continue;
                }

                RefreshAnchoredItem(
                    item,
                    item.GetWorldAnchorPosition(),
                    (view, position) => view.SetAnchoredPosition(position),
                    (view, visible) => view.SetVisible(visible));
            }
        }

        /// <summary>
        /// 每帧同步场景拉客按钮位置、显隐与冷却态。
        /// </summary>
        private void RefreshMyDrumUpButtonItem()
        {
            if (activeMyDrumUpButton == null)
            {
                return;
            }

            if (activeMyDrumUpButton.gameObject == null)
            {
                activeMyDrumUpButton = null;
                return;
            }

            activeMyDrumUpButton.TickVisual();
            RefreshAnchoredItem(
                activeMyDrumUpButton,
                activeMyDrumUpButton.GetWorldAnchorPosition(),
                (view, position) => view.SetAnchoredPosition(position),
                (view, visible) => view.SetVisible(visible));
        }

        private void TrackVipGuestAction(VipGuestActionView item)
        {
            if (item == null || activeVipGuestActions.Contains(item))
            {
                return;
            }

            activeVipGuestActions.Add(item);
        }

        private void RemoveVipGuestActionAt(int index)
        {
            var item = activeVipGuestActions[index];
            activeVipGuestActions.RemoveAt(index);
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }

        private void ClearAllVipGuestActions()
        {
            foreach (var item in activeVipGuestActions)
            {
                if (item != null)
                {
                    Destroy(item.gameObject);
                }
            }

            activeVipGuestActions.Clear();
        }

        private void RefreshVipGuestActionItems()
        {
            for (var index = activeVipGuestActions.Count - 1; index >= 0; index--)
            {
                var item = activeVipGuestActions[index];
                if (item == null)
                {
                    activeVipGuestActions.RemoveAt(index);
                    continue;
                }

                item.Tick();
                if (item.ShouldRelease)
                {
                    RemoveVipGuestActionAt(index);
                    continue;
                }

                RefreshAnchoredItem(
                    item,
                    item.GetWorldAnchorPosition(),
                    (view, position) => view.SetAnchoredPosition(position),
                    (view, visible) => view.SetVisible(visible));
            }
        }

        private void TrackReviewTip(TavernReviewTipsView item)
        {
            if (item == null || activeReviewTips.Contains(item))
            {
                return;
            }

            activeReviewTips.Add(item);
        }

        private void RemoveReviewTipAt(int index)
        {
            var item = activeReviewTips[index];
            activeReviewTips.RemoveAt(index);
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }

        private void ClearAllReviewTips()
        {
            foreach (var item in activeReviewTips)
            {
                if (item != null)
                {
                    Destroy(item.gameObject);
                }
            }

            activeReviewTips.Clear();
        }

        private void RefreshReviewTipItems()
        {
            // 限时气泡用 unscaled，避免升星 timeScale=0 时卡死不消失。
            var delta = Time.unscaledDeltaTime;
            for (var index = activeReviewTips.Count - 1; index >= 0; index--)
            {
                var item = activeReviewTips[index];
                if (item == null)
                {
                    activeReviewTips.RemoveAt(index);
                    continue;
                }

                item.Tick(delta);
                if (item.ShouldRelease)
                {
                    RemoveReviewTipAt(index);
                    continue;
                }

                RefreshAnchoredItem(
                    item,
                    item.GetWorldAnchorPosition(),
                    (view, position) => view.SetAnchoredPosition(position),
                    (view, visible) => view.SetScreenVisible(visible));
            }
        }

        private void ClearAllPulledTips()
        {
            foreach (var pair in activePulledTips)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value.gameObject);
                }
            }

            activePulledTips.Clear();
        }

        private void RefreshPulledTipItems()
        {
            if (activePulledTips.Count == 0)
            {
                return;
            }

            var staleIds = new List<int>();
            foreach (var pair in activePulledTips)
            {
                var item = pair.Value;
                if (item == null)
                {
                    staleIds.Add(pair.Key);
                    continue;
                }

                item.Tick();
                if (item.ShouldRelease)
                {
                    staleIds.Add(pair.Key);
                    continue;
                }

                RefreshAnchoredItem(
                    item,
                    item.GetWorldAnchorPosition(),
                    (view, position) => view.SetAnchoredPosition(position),
                    (view, visible) => view.SetScreenVisible(visible));
            }

            for (var index = 0; index < staleIds.Count; index++)
            {
                ReleaseTablePulledTip(staleIds[index]);
            }
        }

        /// <summary>
        /// 从列表中移除并销毁指定条目。
        /// </summary>
        private void RemoveItemAt(int index)
        {
            var item = activeItems[index];
            activeItems.RemoveAt(index);
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }

        /// <summary>
        /// 清空所有运行时条目。
        /// </summary>
        private void ClearAllItems()
        {
            foreach (var item in activeItems)
            {
                if (item != null)
                {
                    Destroy(item.gameObject);
                }
            }

            activeItems.Clear();
        }

        private TavernWorldWaitHudItemView CreateWaitItem(string itemName)
        {
            EnsureContentRoot();
            waitItemPrefab ??= GameplayResourceStore.LoadAsset<GameObject>(WaitItemPrefabPath);
            if (waitItemPrefab == null || ContentRoot == null)
            {
                return null;
            }

            var itemObject = Instantiate(waitItemPrefab, ContentRoot, false);
            itemObject.name = itemName;
            var item = itemObject.GetComponent<TavernWorldWaitHudItemView>();
            if (item == null)
            {
                item = itemObject.AddComponent<TavernWorldWaitHudItemView>();
            }

            TrackWaitItem(item);
            return item;
        }

        private void TrackWaitItem(TavernWorldWaitHudItemView item)
        {
            if (item == null || activeWaitItems.Contains(item))
            {
                return;
            }

            activeWaitItems.Add(item);
        }

        private void RemoveWaitItemAt(int index)
        {
            var item = activeWaitItems[index];
            activeWaitItems.RemoveAt(index);
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }

        private void ClearAllWaitItems()
        {
            foreach (var item in activeWaitItems)
            {
                if (item != null)
                {
                    Destroy(item.gameObject);
                }
            }

            activeWaitItems.Clear();
        }

        /// <summary>
        /// 每帧同步桌位头顶 HUD 的位置和显隐。
        /// </summary>
        private void RefreshTableActionItems()
        {
            if (activeTableItems.Count == 0)
            {
                return;
            }

            var staleIds = new List<int>();
            foreach (var pair in activeTableItems)
            {
                var tableUi = pair.Value;
                if (tableUi == null || tableUi.BoundTable == null)
                {
                    staleIds.Add(pair.Key);
                    continue;
                }

                RefreshAnchoredItem(
                    tableUi,
                    tableUi.GetWorldAnchorPosition(),
                    (view, position) => view.SetAnchoredPosition(position),
                    (view, visible) => view.SetVisible(visible));
            }

            foreach (var staleId in staleIds)
            {
                ReleaseTableActionHud(staleId);
            }

        }

        /// <summary>
        /// 每帧同步引导建造点头顶 HUD 的位置和显隐。
        /// </summary>
        private void RefreshPurchaseActionItems()
        {
            if (activePurchaseItems.Count == 0)
            {
                return;
            }

            var staleKeys = new List<string>();
            foreach (var pair in activePurchaseItems)
            {
                var purchaseUi = pair.Value;
                if (purchaseUi == null || purchaseUi.BoundTarget == null)
                {
                    staleKeys.Add(pair.Key);
                    continue;
                }

                RefreshAnchoredItem(
                    purchaseUi,
                    purchaseUi.GetWorldAnchorPosition(),
                    (view, position) => view.SetAnchoredPosition(position),
                    (view, visible) => view.SetVisible(visible));
            }

            foreach (var staleKey in staleKeys)
            {
                ReleasePurchaseActionHudInternal(staleKey);
            }
        }

        /// <summary>
        /// 每帧同步墙体扩建头顶 HUD 的位置和显隐。
        /// </summary>
        private void RefreshInteriorWallExpandItem()
        {
            if (activeInteriorWallExpandItem == null)
            {
                return;
            }

            if (activeInteriorWallExpandItem.BoundTarget == null)
            {
                ReleaseInteriorWallExpandHud();
                return;
            }

            RefreshAnchoredItem(
                activeInteriorWallExpandItem,
                activeInteriorWallExpandItem.GetWorldAnchorPosition(),
                (view, position) => view.SetAnchoredPosition(position),
                (view, visible) => view.SetVisible(visible));
        }

        /// <summary>
        /// 每帧同步招聘地块头顶 HUD 的位置和显隐。
        /// </summary>
        private void RefreshEmployActionItems()
        {
            if (activeEmployItems.Count == 0)
            {
                return;
            }

            var staleKeys = new List<string>();
            foreach (var pair in activeEmployItems)
            {
                var employUi = pair.Value;
                if (employUi == null || employUi.BoundTarget == null)
                {
                    staleKeys.Add(pair.Key);
                    continue;
                }

                RefreshAnchoredItem(
                    employUi,
                    employUi.GetWorldAnchorPosition(),
                    (view, position) => view.SetAnchoredPosition(position),
                    (view, visible) => view.SetVisible(visible));
            }

            foreach (var staleKey in staleKeys)
            {
                ReleaseEmployActionHudInternal(staleKey);
            }
        }

        /// <summary>
        /// 按桌位编号释放头顶 HUD。
        /// </summary>
        private void ReleaseTableActionHud(int tableId)
        {
            if (!activeTableItems.TryGetValue(tableId, out var tableUi))
            {
                return;
            }

            activeTableItems.Remove(tableId);
            if (tableUi != null)
            {
                Destroy(tableUi.gameObject);
            }
        }

        /// <summary>
        /// 清空所有桌位头顶 HUD。
        /// </summary>
        private void ClearAllTableItems()
        {
            foreach (var pair in activeTableItems)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value.gameObject);
                }
            }

            activeTableItems.Clear();
        }

        /// <summary>
        /// 按键值释放引导建造点头顶 HUD。
        /// </summary>
        private void ReleasePurchaseActionHudInternal(string purchaseKey)
        {
            if (!activePurchaseItems.TryGetValue(purchaseKey, out var purchaseUi))
            {
                return;
            }

            activePurchaseItems.Remove(purchaseKey);
            if (purchaseUi != null)
            {
                Destroy(purchaseUi.gameObject);
            }
        }

        /// <summary>
        /// 清空所有引导建造点头顶 HUD。
        /// </summary>
        private void ClearAllPurchaseItems()
        {
            foreach (var pair in activePurchaseItems)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value.gameObject);
                }
            }

            activePurchaseItems.Clear();
        }

        /// <summary>
        /// 按键值释放招聘头顶 HUD。
        /// </summary>
        private void ReleaseEmployActionHudInternal(string employKey)
        {
            if (!activeEmployItems.TryGetValue(employKey, out var employUi))
            {
                return;
            }

            activeEmployItems.Remove(employKey);
            if (employUi != null)
            {
                Destroy(employUi.gameObject);
            }
        }

        /// <summary>
        /// 清空所有招聘头顶 HUD。
        /// </summary>
        private void ClearAllEmployItems()
        {
            foreach (var pair in activeEmployItems)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value.gameObject);
                }
            }

            activeEmployItems.Clear();
        }
    }
}
