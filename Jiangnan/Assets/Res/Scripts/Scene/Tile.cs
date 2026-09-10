using System.Collections.Generic;
using JN.Client;
using JN.Client.Manager;
using JN.Client.Messages;
using JN.Client.UI;
using QFramework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace JN.Client.Scene
{
    [RequireComponent(typeof(Collider))]
    /// <summary>
    /// 负责地块相关的运行时逻辑。
    /// </summary>
    public class Tile : MonoBehaviour
    {
        private const string AddLandSpritePath = "Assets/Res/Resources/Textures/UI/Panel/TavernScene/add_land.png";
        private const string LandPurchaseSpritePrefabPath = "Assets/Res/Resources/Scenes/Town/LandPurchaseSprite.prefab";
        private const string LandPriceWorldPrefabPath = "Assets/Res/Resources/Scenes/Town/LandPriceWorld.prefab";
        // OtherPlayerShopComingSoonMessage 已废弃：他人店铺可进入访客副本。

        private static int ResolveSelfPlayerId()
        {
            if (DataManager.Instance?.PlayerData == null)
            {
                return 0;
            }

            return int.TryParse(DataManager.Instance.PlayerData.playerId, out var playerId) ? playerId : 0;
        }

        public int tileId;
        public BuildingInfo buildingInfo;
        public BuildingItemUI linkedUI;

        [SerializeField] private GameObject groundIndicator;
        [SerializeField] private SpriteRenderer landPurchaseSpriteRenderer;
        [SerializeField] private LandPriceWorld landPriceWorld;
        [SerializeField] private Vector3 landPurchaseSpriteOffset = new(0f, 0.08f, 0f);
        [SerializeField] private Vector3 landPurchaseSpriteEuler = new(90f, 0f, 0f);
        [SerializeField] private Vector3 landPurchaseSpriteScale = new(30f, 30f, 30f);
        [SerializeField] private float landPurchaseSpriteBoundsPadding = 1.2f;
        [SerializeField] private Transform buildingRoot;
        [SerializeField] private bool snapBuildingToTileCenter = true;

        private GameObject m_CurrentBuildingVisual;
        private int m_CurrentVisualLevel;
        private Sprite m_AddLandSprite;
        private Collider m_TileCollider;
        /// <summary>下一次刷出酒楼模型时播落成光柱（仅建造倒计时结束用）。</summary>
        private bool playCompleteEffectOnNextVisual;

        /// <summary>
        /// 初始化组件引用和运行时状态。
        /// </summary>
        private void Awake()
        {
            GetTileIdFromInternal();
            m_TileCollider = GetComponent<Collider>();
        }

        /// <summary>
        /// 尝试通过场景相机射线命中当前地块。
        /// </summary>
        /// <param name="pointerPosition">点击坐标。</param>
        /// <returns>命中当前地块时返回 true。</returns>
        public static bool TryHandlePointerClick(Vector2 pointerPosition)
        {
            if (IsPointerOverInteractiveUI(pointerPosition))
            {
                return false;
            }

            var cameras = ResolveRaycastCameras();
            for (var cameraIndex = 0; cameraIndex < cameras.Count; cameraIndex++)
            {
                var rayCamera = cameras[cameraIndex];
                if (rayCamera == null)
                {
                    continue;
                }

                var ray = rayCamera.ScreenPointToRay(pointerPosition);
                var hits = Physics.RaycastAll(ray, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                for (var index = 0; index < hits.Length; index++)
                {
                    var hitCollider = hits[index].collider;
                    if (hitCollider == null)
                    {
                        continue;
                    }

                    var hitTile = hitCollider.GetComponentInParent<Tile>();
                    if (hitTile == null)
                    {
                        continue;
                    }

                    hitTile.FocusCameraOnTileCenter();
                    if (hitTile.IsOtherPlayerBuilding())
                    {
                        hitTile.EnterTavernFromUI();
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 设置建筑信息数据。
        /// </summary>
        /// <param name="info">参数值。</param>
        public void SetBuildingInfoData(BuildingInfo info)
        {
            buildingInfo = info;

            if (groundIndicator != null)
            {
                // 没有建筑归属时显示空地提示，方便玩家识别可购买地块。
                groundIndicator.SetActive(false);
            }

            RefreshLandPurchaseSprite();
            RefreshLandPriceWorld();
            RefreshBuildingVisual();
            linkedUI?.SetData(info);
        }

        /// <summary>
        /// 获取地块内部编号。
        /// </summary>
        /// <returns>返回计算后的数值。</returns>
        public int GetTileIdFromInternal()
        {
            if (tileId <= 0)
            {
                var index = gameObject.name.LastIndexOf('_');
                if (index >= 0 && int.TryParse(gameObject.name[(index + 1)..], out var id))
                {
                    tileId = id;
                }
            }

            return tileId;
        }

        /// <summary>
        /// 处理来自界面的主要操作。
        /// </summary>
        public void HandlePrimaryActionFromUI()
        {
            var selfPlayerId = ResolveSelfPlayerId();

            if (buildingInfo != null && buildingInfo.playerId == selfPlayerId && buildingInfo.buildingLevel <= 0)
            {
                TryStartDefaultLevel1BuildFromUI();
                return;
            }

            // 别人的已建成酒楼：进入访客副本。
            if (buildingInfo != null && buildingInfo.playerId != 0 && buildingInfo.playerId != selfPlayerId)
            {
                if (buildingInfo.status == 2 && buildingInfo.buildingLevel > 0)
                {
                    EnterTavernFromUI();
                }

                return;
            }

            // 自家已建成酒楼：点 User 进店（与他人店同一入口）。
            if (buildingInfo != null
                && buildingInfo.playerId == selfPlayerId
                && buildingInfo.status == 2
                && buildingInfo.buildingLevel > 0)
            {
                EnterTavernFromUI();
                return;
            }

            if (buildingInfo != null && buildingInfo.playerId != 0)
            {
                return;
            }

            if (DataManager.Instance == null || !DataManager.Instance.IsSelfTownBuildingField(tileId))
            {
                HudOverlayService.ShowFloatingWarning($"只能在地块 {DataManager.Instance?.GetSelfBuildingFieldId() ?? 0} 建造自家酒楼");
                return;
            }

            if (!DataManager.Instance.TryPurchaseTownLand(tileId, out var message))
            {
                Debug.LogWarning($"[Tile] 购买地块失败：{message}");
                return;
            }

            var coinTransform = GOReferenceManager.Instance != null ? GOReferenceManager.Instance.GetCoinTransform() : null;
            if (coinTransform != null && linkedUI != null)
            {
                GameUIEffects.PlayCoinsFly(coinTransform, linkedUI.transform);
            }

            var newInfo = DataManager.Instance.GetTownBuildingInfos().Find(info => info.tileId == tileId);
            TileManager.Instance.UpdateTile(tileId, newInfo);
            TileManager.Instance.RefreshAllTileViews();
        }

        public Vector3 GetFocusWorldPoint()
        {
            if (m_TileCollider == null)
            {
                m_TileCollider = GetComponent<Collider>();
            }

            return m_TileCollider != null ? m_TileCollider.bounds.center : transform.position;
        }

        /// <summary>
        /// 建造完成光柱锚点：优先酒楼模型包围盒顶部，其次 buildingRoot / 地块中心。
        /// </summary>
        public Vector3 GetBuildingCompleteEffectWorldPosition()
        {
            var target = m_CurrentBuildingVisual != null
                ? m_CurrentBuildingVisual.transform
                : buildingRoot != null ? buildingRoot : transform;
            return ResolveBuildingEffectWorldPosition(target);
        }

        private static Vector3 ResolveBuildingEffectWorldPosition(Transform target)
        {
            if (target == null)
            {
                return Vector3.zero;
            }

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return target.position + Vector3.up * 0.8f;
            }

            var hasBounds = false;
            var bounds = new Bounds(target.position, Vector3.zero);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            return hasBounds
                ? new Vector3(bounds.center.x, bounds.max.y + 0.25f, bounds.center.z)
                : target.position + Vector3.up * 0.8f;
        }

        private void FocusCameraOnTileCenter()
        {
            if (CameraController.Instance == null)
            {
                return;
            }

            CameraController.Instance.FocusOnWorldPoint(GetFocusWorldPoint());
        }

        /// <summary>
        /// 打开自己地块上的建筑建造窗口。
        /// </summary>
        /// <summary>
        /// 打开旧选择窗口，确认建造后统一进入建造视频流程。
        /// </summary>
        private void OpenBuildWindow()
        {
            var data = new NewBuildingWindowControllerData
            {
                tileId = tileId,
                confirmAction = HandleBuildConfirmed
            };

            UIKit.OpenPanel<NewBuildingWindowController>(
                JiangNanUIPanelLayerConfig.Resolve<NewBuildingWindowController>(UILevel.PopUI),
                data);
            Signals.Get<StartBuildingSignal>().Dispatch(tileId);
        }

        /// <summary>
        /// 直接建造默认 1 级建筑。
        /// </summary>
        private void StartDefaultLevel1Build()
        {
            if (!NewBuildingWindowController.TryBuildDefaultLevel1(tileId, null, out var message))
            {
                Debug.LogWarning($"[Tile] 默认建造 1 级建筑失败：{message}");
                return;
            }

            Signals.Get<StartBuildingSignal>().Dispatch(tileId);
        }

        /// <summary>
        /// 建造确认后的统一入口，负责触发建造视频。
        /// </summary>
        /// <summary>
        /// 执行默认 1 级建造；是否播放建造视频由 BuildingItemUI 的 openingBtn 统一决定。
        /// </summary>
        public bool TryStartDefaultLevel1BuildFromUI()
        {
            if (!NewBuildingWindowController.TryBuildDefaultLevel1(tileId, null, out var message))
            {
                Debug.LogWarning($"[Tile] 默认建造 1 级建筑失败：{message}");
                return false;
            }

            Signals.Get<StartBuildingSignal>().Dispatch(tileId);
            if (groundIndicator != null)
            {
                groundIndicator.SetActive(false);
            }

            // 建造中（status=1）由 BuildingItemUI 播 3 秒锤子/烟雾，倒计时结束再落成。
            return true;
        }

        private void HandleBuildConfirmed()
        {
            if (groundIndicator != null)
            {
                groundIndicator.SetActive(false);
            }

        }

        /// <summary>
        /// 兼容旧入口：建造改为 3 秒倒计时落成，不再跳过锤子动画。
        /// </summary>
        public void CompleteGuidedBuildAndEnterTavernFromUI()
        {
            VideoWindowController.HideActiveWindow();
            TileManager.Instance?.RefreshAllTileViews();
        }

        /// <summary>
        /// 自家酒楼落成后刷新城镇底部「进入酒楼」。
        /// </summary>
        public static void NotifyTownOwnedBuildingCompleted()
        {
            Signals.Get<GameplayGuideProgressSignal>().Dispatch();
            UIKit.GetPanel<TownStatusBarPanelController>()?.RefreshAllPanels();
            UIKit.GetPanel<TownBottomNavPanelController>()?.RefreshPanel();
        }

        /// <summary>
        /// 处理来自界面的进入酒楼操作。
        /// </summary>
        /// <summary>
        /// 从地块进入酒馆，成功发起场景切换时返回 true。
        /// </summary>
        public bool EnterTavernFromUI()
        {
            if (buildingInfo == null
                || buildingInfo.status != 2
                || buildingInfo.buildingLevel <= 0
                || buildingInfo.playerId <= 0)
            {
                return false;
            }

            var selfPlayerId = ResolveSelfPlayerId();
            if (buildingInfo.playerId == selfPlayerId)
            {
                // 与底栏 btn_Enter 一致：≥2 星二次确认，1 星直接进店。
                var statusBar = UIKit.GetPanel<TownStatusBarPanelController>();
                if (statusBar != null)
                {
                    statusBar.HandleEnterTavernRequest();
                    return true;
                }

                var tavernLevel = DataManager.Instance != null ? DataManager.Instance.GetTavernLevel() : 1;
                var tile = tileId;
                var level = buildingInfo.buildingLevel;
                if (tavernLevel < 2)
                {
                    StartCoroutine(SceneFlowCoordinator.EnterTavern(tile, level));
                    return true;
                }

                var used = DataManager.Instance != null ? DataManager.Instance.GetJiaoziUsedCapacity() : 0;
                var max = DataManager.Instance != null ? DataManager.Instance.GetJiaoziCapacity() : 0;
                HudOverlayService.ShowConfirmBox(
                    "确定返回酒楼吗？",
                    $"拉客 {used}/{max}",
                    () => StartCoroutine(SceneFlowCoordinator.EnterTavern(tile, level)));
                return true;
            }

            // 他人酒楼：酒楼 2 级解锁轿子后可进入访客副本。
            if (DataManager.Instance == null || !DataManager.Instance.CanVisitOtherTavern())
            {
                HudOverlayService.ShowFloatingWarning("二星酒楼解锁");
                return false;
            }

            StartCoroutine(SceneFlowCoordinator.EnterOtherTavernVisit(
                tileId,
                buildingInfo.buildingLevel,
                buildingInfo.name));
            return true;
        }

        private bool IsOtherPlayerBuilding()
        {
            if (buildingInfo == null
                || buildingInfo.playerId <= 0
                || buildingInfo.status != 2
                || buildingInfo.buildingLevel <= 0)
            {
                return false;
            }

            return buildingInfo.playerId != ResolveSelfPlayerId();
        }

        private static void ShowOtherPlayerShopComingSoonMessage()
        {
            // 保留空实现，兼容旧调用；他人店铺已改为可进入访客副本。
        }

        /// <summary>
        /// 处理指针是否悬停在可交互界面相关逻辑。
        /// </summary>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private static bool IsPointerOverInteractiveUI(Vector2 pointerPosition)
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            var eventData = new PointerEventData(EventSystem.current)
            {
                position = pointerPosition
            };

            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            for (var i = 0; i < results.Count; i++)
            {
                var hit = results[i].gameObject;
                if (hit == null)
                {
                    continue;
                }

                if (hit.GetComponentInParent<Selectable>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 刷新建筑表现。
        /// </summary>
        private void RefreshBuildingVisual()
        {
            if (buildingInfo == null
                || buildingInfo.playerId == 0
                || buildingInfo.buildingLevel <= 0
                || buildingInfo.status != 2)
            {
                ClearBuildingVisual();
                return;
            }

            var visualLevel = ResolveTownExteriorVisualLevel(buildingInfo);
            var prefab = TileManager.Instance != null
                ? TileManager.Instance.GetBuildingPrefabForLevel(visualLevel)
                : null;
            if (prefab == null)
            {
                ClearBuildingVisual();
                TryPlayPendingCompleteEffect();
                return;
            }

            if (m_CurrentBuildingVisual != null && m_CurrentVisualLevel == visualLevel)
            {
                TryPlayPendingCompleteEffect();
                return;
            }

            ClearBuildingVisual();

            var parent = buildingRoot != null ? buildingRoot : transform;
            m_CurrentBuildingVisual = Instantiate(prefab, parent);
            m_CurrentBuildingVisual.name = $"Tile_{tileId}_BuildingLv{visualLevel}";
            if (snapBuildingToTileCenter)
            {
                // 建筑表现默认吸附到地块中心，避免不同 预制体 原点不一致带来偏移。
                m_CurrentBuildingVisual.transform.localPosition = Vector3.zero;
            }

            m_CurrentVisualLevel = visualLevel;
            TryPlayPendingCompleteEffect();
        }

        /// <summary>
        /// 自家：楼梯解锁后用 Prefab_BuildingLv2；他人：沿用 BuildingInfo.buildingLevel。
        /// </summary>
        private int ResolveTownExteriorVisualLevel(BuildingInfo info)
        {
            if (info == null)
            {
                return 1;
            }

            var selfPlayerId = ResolveSelfPlayerId();
            if (info.playerId == selfPlayerId && DataManager.Instance != null)
            {
                return Mathf.Clamp(DataManager.Instance.ResolveOwnTownExteriorBuildingLevel(), 1, 3);
            }

            return Mathf.Clamp(info.buildingLevel, 1, 3);
        }

        private void TryPlayPendingCompleteEffect()
        {
            if (!playCompleteEffectOnNextVisual)
            {
                return;
            }

            playCompleteEffectOnNextVisual = false;
            PlayBuildingCompleteEffect();
        }

        /// <summary>
        /// 建造倒计时结束：下一次生成酒楼模型时播光柱。
        /// </summary>
        public void MarkPlayCompleteEffectOnNextVisual()
        {
            playCompleteEffectOnNextVisual = true;
        }

        /// <summary>
        /// 在酒楼模型位置播建造完成光柱（UI 粒子必须挂 Canvas）。
        /// </summary>
        public void PlayBuildingCompleteEffect()
        {
            var worldPosition = GetBuildingCompleteEffectWorldPosition();
            Transform effectParent = null;
            if (linkedUI != null)
            {
                var canvas = linkedUI.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    effectParent = canvas.transform;
                }
            }

            if (effectParent == null)
            {
                var hud = UIKit.GetPanel<BuildingItemSceneController>();
                if (hud != null)
                {
                    effectParent = hud.transform;
                }
            }

            TavernSceneManager.PlayGuideBuildingSuccessEffect(worldPosition, playAudio: true, effectParent);
        }

        /// <summary>
        /// 清理建筑表现。
        /// </summary>
        private void ClearBuildingVisual()
        {
            if (m_CurrentBuildingVisual != null)
            {
                Destroy(m_CurrentBuildingVisual);
                m_CurrentBuildingVisual = null;
            }

            m_CurrentVisualLevel = 0;
        }

        /// <summary>
        /// 刷新场景内的可购买地块加号；加号使用 SpriteRenderer，不走 UI 层。
        /// </summary>
        private void RefreshLandPurchaseSprite()
        {
            var selfPlayerId = ResolveSelfPlayerId();
            var canPurchaseLand = (buildingInfo == null || buildingInfo.playerId == 0)
                                  && DataManager.Instance != null
                                  && DataManager.Instance.IsSelfTownBuildingField(tileId)
                                  && !DataManager.Instance.IsTownLandCountAtLimit(selfPlayerId, out int hasLandCount);

            var renderer = EnsureLandPurchaseSpriteRenderer();
            if (renderer == null)
            {
                return;
            }

            ApplyLandPurchaseSpriteLayout(renderer);
            renderer.gameObject.SetActive(canPurchaseLand);
        }

        /// <summary>
        /// 刷新场景内的地块价格标牌。
        /// </summary>
        private void RefreshLandPriceWorld()
        {
            if (landPriceWorld == null)
            {
                return;
            }

            landPriceWorld.gameObject.SetActive(false);
        }

        /// <summary>
        /// 获取或创建场景内地块价格标牌。
        /// </summary>
        private LandPriceWorld EnsureLandPriceWorld()
        {
            var prefab = LoadLandPriceWorldPrefab();
            if (landPriceWorld != null && (prefab == null || landPriceWorld.HasConfiguredBindings))
            {
                landPriceWorld.Bind(this);
                return landPriceWorld;
            }

            var existing = transform.Find("LandPrice_World");
            if (existing != null)
            {
                var existingWorld = existing.GetComponent<LandPriceWorld>();
                if (prefab == null && existingWorld != null)
                {
                    landPriceWorld = existingWorld;
                    landPriceWorld.Bind(this);
                    return landPriceWorld;
                }

                Destroy(existing.gameObject);
            }

            if (landPriceWorld != null && landPriceWorld.transform.parent == transform)
            {
                Destroy(landPriceWorld.gameObject);
                landPriceWorld = null;
            }

            if (prefab != null)
            {
                var instance = Instantiate(prefab, transform, false);
                instance.name = "LandPrice_World";
                landPriceWorld = instance.GetComponent<LandPriceWorld>();
            }

            if (landPriceWorld == null)
            {
                var go = new GameObject("LandPrice_World");
                go.transform.SetParent(transform, false);
                landPriceWorld = go.AddComponent<LandPriceWorld>();
            }

            landPriceWorld.Bind(this);
            return landPriceWorld;
        }

        /// <summary>
        /// 读取场景内地块价格标牌预制体。
        /// </summary>
        /// <returns>读取成功返回预制体，否则返回 null。</returns>
        private static GameObject LoadLandPriceWorldPrefab()
        {
            var prefab = GameplayResourceStore.LoadAsset<GameObject>(LandPriceWorldPrefabPath);
            if (prefab != null)
            {
                return prefab;
            }

            return Resources.Load<GameObject>("Scenes/Town/LandPriceWorld");
        }

        /// <summary>
        /// 获取或加载地块上的场景加号 SpriteRenderer。
        /// </summary>
        /// <returns>可购买地块加号渲染器。</returns>
        private SpriteRenderer EnsureLandPurchaseSpriteRenderer()
        {
            if (landPurchaseSpriteRenderer != null)
            {
                return landPurchaseSpriteRenderer;
            }

            if (groundIndicator != null)
            {
                landPurchaseSpriteRenderer = groundIndicator.GetComponent<SpriteRenderer>();
            }

            if (landPurchaseSpriteRenderer == null)
            {
                var prefab = LoadLandPurchaseSpritePrefab();
                if (prefab != null)
                {
                    var spriteObject = Instantiate(prefab, transform, false);
                    spriteObject.name = "LandPurchaseSprite";
                    landPurchaseSpriteRenderer = spriteObject.GetComponent<SpriteRenderer>();
                }
                else
                {
                    Debug.LogWarning($"[Tile] 缺少地块购买提示预制体：{LandPurchaseSpritePrefabPath}，已改为运行时创建精灵节点兜底。");
                    var spriteObject = new GameObject("LandPurchaseSprite");
                    spriteObject.transform.SetParent(transform, false);
                    landPurchaseSpriteRenderer = spriteObject.AddComponent<SpriteRenderer>();
                }
            }

            var loadedAddLandSprite = LoadAddLandSprite();
            if (loadedAddLandSprite != null)
            {
                // 仅在成功读取到新贴图时覆盖，避免把 prefab 自带 sprite 清空。
                landPurchaseSpriteRenderer.sprite = loadedAddLandSprite;
            }

            if (landPurchaseSpriteRenderer.sprite == null)
            {
                landPurchaseSpriteRenderer.gameObject.SetActive(false);
                return landPurchaseSpriteRenderer;
            }

            ApplyLandPurchaseSpriteLayout(landPurchaseSpriteRenderer);
            EnsureLandPurchaseSpriteCollider(landPurchaseSpriteRenderer);
            return landPurchaseSpriteRenderer;
        }

        /// <summary>
        /// 将绿色地块图片铺到当前地块碰撞盒中心，并覆盖整个地块。
        /// </summary>
        /// <param name="renderer">绿色地块渲染器。</param>
        private void ApplyLandPurchaseSpriteLayout(SpriteRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.sortingOrder = 50;
            renderer.transform.localEulerAngles = landPurchaseSpriteEuler;
            // renderer.transform.localScale = ResolveLandPurchaseSpriteScale(renderer.sprite);

            var tileCollider = GetComponent<Collider>();
            if (tileCollider == null)
            {
                renderer.transform.localPosition = landPurchaseSpriteOffset;
                return;
            }

            var worldCenter = tileCollider.bounds.center + transform.TransformVector(landPurchaseSpriteOffset);
            renderer.transform.position = worldCenter;
            EnsureLandPurchaseSpriteCollider(renderer);
        }

        /// <summary>
        /// 根据地块碰撞盒尺寸计算绿色加号缩放，使其覆盖整个地块。
        /// </summary>
        /// <param name="sprite">绿色加号图片。</param>
        /// <returns>适配地块尺寸后的本地缩放。</returns>
        private Vector3 ResolveLandPurchaseSpriteScale(Sprite sprite)
        {
            var tileCollider = GetComponent<Collider>();
            if (sprite == null || tileCollider == null || sprite.bounds.size.x <= 0f || sprite.bounds.size.y <= 0f)
            {
                return landPurchaseSpriteScale;
            }

            var lossyScale = landPurchaseSpriteRenderer != null ? landPurchaseSpriteRenderer.transform.lossyScale : Vector3.one;
            var bounds = tileCollider.bounds;
            var parentScale = landPurchaseSpriteRenderer != null && landPurchaseSpriteRenderer.transform.parent != null
                ? landPurchaseSpriteRenderer.transform.parent.lossyScale
                : transform.lossyScale;
            var safeScaleX = Mathf.Abs(parentScale.x) > 0.0001f ? Mathf.Abs(parentScale.x) : Mathf.Max(0.0001f, Mathf.Abs(lossyScale.x));
            var safeScaleZ = Mathf.Abs(parentScale.z) > 0.0001f ? Mathf.Abs(parentScale.z) : Mathf.Max(0.0001f, Mathf.Abs(lossyScale.z));
            var scaleX = Mathf.Max(bounds.size.x, bounds.size.z) / (sprite.bounds.size.x * safeScaleX);
            var scaleY = Mathf.Max(bounds.size.x, bounds.size.z) / (sprite.bounds.size.y * safeScaleZ);
            return new Vector3(scaleX, scaleY, 1f) * landPurchaseSpriteBoundsPadding;
        }

        /// <summary>
        /// 读取可购买地块使用的绿色加号图片。
        /// </summary>
        /// <returns>读取成功返回 add_land Sprite，否则返回 null。</returns>
        private Sprite LoadAddLandSprite()
        {
            if (m_AddLandSprite != null)
            {
                return m_AddLandSprite;
            }

            m_AddLandSprite = GameplayResourceStore.LoadAsset<Sprite>(AddLandSpritePath);
            return m_AddLandSprite;
        }

        /// <summary>
        /// 读取可购买地块场景提示预制体。
        /// </summary>
        /// <returns>读取成功返回预制体，否则返回 null。</returns>
        private static GameObject LoadLandPurchaseSpritePrefab()
        {
            var prefab = GameplayResourceStore.LoadAsset<GameObject>(LandPurchaseSpritePrefabPath);
            if (prefab != null)
            {
                return prefab;
            }

            // 再兜底一次直接 Resources 路径，规避完整路径转换异常时的漏载。
            return Resources.Load<GameObject>("Scenes/Town/LandPurchaseSprite");
        }

        /// <summary>
        /// 为绿色地块提示节点补齐点击碰撞体，覆盖当前地块区域。
        /// </summary>
        /// <param name="renderer">绿色地块渲染器。</param>
        private void EnsureLandPurchaseSpriteCollider(SpriteRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            var tileCollider = m_TileCollider != null ? m_TileCollider : GetComponent<Collider>();
            if (tileCollider == null)
            {
                return;
            }

            var boxCollider = renderer.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = renderer.gameObject.AddComponent<BoxCollider>();
            }

            boxCollider.isTrigger = true;
            var bounds = tileCollider.bounds;
            boxCollider.center = renderer.transform.InverseTransformPoint(bounds.center);
            boxCollider.size = new Vector3(bounds.size.x, Mathf.Max(0.2f, bounds.size.y + 0.5f), bounds.size.z);
        }

        /// <summary>
        /// 获取可用于场景点击射线的摄像机列表。
        /// </summary>
        /// <returns>按优先级排序后的摄像机列表。</returns>
        private static List<Camera> ResolveRaycastCameras()
        {
            var result = new List<Camera>(4);
            if (Camera.main != null)
            {
                result.Add(Camera.main);
            }

            var cameras = Camera.allCameras;
            for (var index = 0; index < cameras.Length; index++)
            {
                var camera = cameras[index];
                if (camera == null || !camera.isActiveAndEnabled || result.Contains(camera))
                {
                    continue;
                }

                result.Add(camera);
            }

            return result;
        }
    }
}
