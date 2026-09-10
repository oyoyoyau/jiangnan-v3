using System.Collections.Generic;
using DG.Tweening;
using JN.Client;
using JN.Client.Manager;
using JN.Client.Messages;
using JN.Client.UI;
using QFramework;
using TMPro;
using UnityEngine;
using ApiProtocols = global::JN.Client.Protocols.Protocols;

namespace JN.Client.Scene
{
    /// <summary>
    /// 负责地块相关的运行时逻辑。
    /// </summary>
    public class TileManager : MonoBehaviour
    {
        private const string WarningPrefabPath = "Assets/Res/Resources/UI/Menu/WarningPrefab.prefab";

        public static TileManager Instance;

        public Dictionary<int, BuildingInfo> AllBuildingDatas = new();
        public Dictionary<int, BuildingItemUI> AllBuildingUIs = new();
        public Dictionary<int, Tile> AllTiles = new();

        [Header("地块 UI 配置")]
        [SerializeField] public GameObject buildingUIPrefab;
        [SerializeField] public Camera SceneCamera;

        [Header("建筑等级 Prefab")]
        [SerializeField] private GameObject buildingLevel1Prefab;
        [SerializeField] private GameObject buildingLevel2Prefab;
        [SerializeField] private GameObject buildingLevel3Prefab;
        
        [SerializeField] private GameObject warningPrefab;

        [Header("调试测试")]
        [SerializeField] private bool useVirtualTestData = true;

        private bool m_Initialized;

        /// <summary>
        /// 初始化组件引用和运行时状态。
        /// </summary>
        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// 在场景启动后补齐依赖并刷新初始显示。
        /// </summary>
        private void Start()
        {
            if (m_Initialized)
            {
                return;
            }

            m_Initialized = true;
            JiangNanUIKitBootstrap.Initialize();
            // 进小镇时按楼梯解锁同步自家店铺外观层数（2 → Prefab_BuildingLv2）。
            DataManager.Instance?.TrySyncOwnTownExteriorBuildingLevel();
            FetchBuildingDataFromSave();
            //ApplyVirtualTestDataIfNeeded();
            //InitTiles();
            LoadTownBuildingViews();
            
        }

        /// <summary>
        /// 获取tile
        /// </summary>
        /// <param name="tileID"></param>
        /// <returns></returns>
        public Tile GetTile(int tileID)
        {
            return AllTiles[tileID];
        }

        /// <summary>
        /// 获取场景相机。
        /// </summary>
        /// <returns>返回方法执行后的结果。</returns>
        public Camera GetSceneCamera()
        {
            return SceneCamera != null ? SceneCamera : Camera.main;
        }

        /// <summary>
        /// 按等级获取建筑预制体。
        /// </summary>
        /// <param name="buildingLevel">等级。</param>
        /// <returns>返回匹配到的对象引用。</returns>
        public GameObject GetBuildingPrefabForLevel(int buildingLevel)
        {
            return buildingLevel switch
            {
                1 => buildingLevel1Prefab,
                2 => buildingLevel2Prefab,
                3 => buildingLevel3Prefab,
                _ => null
            };
        }

        /// <summary>
        /// 更新地块。
        /// </summary>
        /// <param name="tileId">数据编号。</param>
        /// <param name="new信息">参数值。</param>
        public void UpdateTile(int tileId, BuildingInfo newInfo)
        {
            AllBuildingDatas[tileId] = newInfo;

            // 本地存档模式始终回写；联网测试虚拟数据模式下跳过，避免覆盖真实服务端数据。
            if (LocalSaveMode.Enabled || !useVirtualTestData)
            {
                DataManager.Instance.UpsertBuildingInfo(newInfo);
            }

            if (AllTiles.TryGetValue(tileId, out var tile))
            {
                tile.SetBuildingInfoData(newInfo);
            }

            UIKit.GetPanel<BuildingItemSceneController>()?.RefreshTile(tileId);
        }

        /// <summary>
        /// 刷新全部地块的场景表现和跟随 UI。
        /// </summary>
        public void RefreshAllTileViews()
        {
            DataManager.Instance?.TrySyncOwnTownExteriorBuildingLevel();
            if (DataManager.Instance != null)
            {
                FetchBuildingDataFromSave();
            }

            foreach (var tilePair in AllTiles)
            {
                var tileId = tilePair.Key;
                var tile = tilePair.Value;
                if (tile == null)
                {
                    continue;
                }

                tile.SetBuildingInfoData(AllBuildingDatas.TryGetValue(tileId, out var info) ? info : null);
            }

            UIKit.GetPanel<BuildingItemSceneController>()?.RefreshAllTiles();
        }

        /// <summary>
        /// 初始化地块列表和虚拟建筑数据。
        /// </summary>
        private void InitTiles()
        {
            AllTiles.Clear();
            var tilesInScene = FindObjectsByType<Tile>(FindObjectsSortMode.None);

            foreach (var tile in tilesInScene)
            {
                var id = tile.GetTileIdFromInternal();
                AllTiles[id] = tile;

                if (AllBuildingDatas.TryGetValue(id, out var info))
                {
                    tile.SetBuildingInfoData(info);
                }
                else
                {
                    // 没有存档数据的地块按空地处理。
                    tile.SetBuildingInfoData(null);
                }
            }
        }

        /// <summary>
        /// 打开建筑场景面板。
        /// </summary>
        private void OpenBuildingScenePanel()
        {
            var panelData = new BuildingItemSceneControllerData
            {
                TileManager = this
            };

            var panel = UIKit.GetPanel<BuildingItemSceneController>();
            if (panel == null)
            {
                UIKit.OpenPanel<BuildingItemSceneController>(
                    JiangNanUIPanelLayerConfig.Resolve<BuildingItemSceneController>(),
                    panelData);
            }
            else
            {
                panel.Open(panelData);
            }
           
        }

        /// <summary>
        /// 加载大地图地块视图：本地存档模式只读本地数据，联网模式可拉取全服玩家地块。
        /// </summary>
        private void LoadTownBuildingViews()
        {
            if (LocalSaveMode.Enabled || string.IsNullOrWhiteSpace(DataManager.Instance.AuthToken))
            {
                InitTiles();
                OpenBuildingScenePanel();
                return;
            }

            GetAllPlayersData();
        }

        private void GetAllPlayersData()
        {
            if (LocalSaveMode.Enabled || string.IsNullOrWhiteSpace(DataManager.Instance.AuthToken))
            {
                InitTiles();
                OpenBuildingScenePanel();
                return;
            }

            ApiProtocols.Instance.GetAllPlayers(
                onSuccess: players =>
                {
                    var count = players?.Count ?? 0;
                    Debug.Log($"[BuildingItemSceneController] 成功获取所有玩家数据，数量：{count}");

                    if (players == null || players.Count == 0)
                    {
                        Debug.Log("[BuildingItemSceneController] 玩家列表为空。");
                        InitTiles();
                        OpenBuildingScenePanel();
                        return;
                    }

                    for (var i = 0; i < players.Count; i++)
                    {
                        var player = players[i];
                        if (player == null)
                        {
                            Debug.LogWarning($"[BuildingItemSceneController] 玩家[{i}] 为空。");
                            continue;
                        }
                        print("请求成功#");
                        if (TryResolveTileIdFromLand(player.land, out var tileId))
                        {
                            if (int.TryParse(DataManager.Instance.PlayerData.playerId, out var pid) && int.TryParse(player.userId, out var parsedUserId) && parsedUserId == pid)
                            {
                                Manager.DataManager.Instance.PlayerData.buildId = tileId;
                            }
                           
                            ApplyVirtualBuilding(
                                tileId,
                                int.TryParse(player.userId, out var userId) ? userId : 0,
                                player.userName,
                                Mathf.Max(1, player.level),
                                2,
                                0,
                                0);
                        }

                        Debug.Log(
                            $"[BuildingItemSceneController] 玩家[{i}] id={player.id}, userId={player.userId},land={(player.land == null ? "null" : JsonUtility.ToJson(player.land))}, userName={player.userName}, playerPic={player.playerPic}, coins={player.coins}, level={player.level}, experience={player.experience}, lastLogin={player.lastLogin}, updatedAt={player.updatedAt}");
                    }

                    InitTiles();
                    OpenBuildingScenePanel();
                },
                onError: error =>
                {
                    SpawnWarning("#获取玩家数据失败#", transform);
                    Debug.LogWarning($"[BuildingItemSceneController] #获取所有玩家数据失败：{error}");
                    InitTiles();
                    OpenBuildingScenePanel();
                });
        }

        /// <summary>
        /// 从存档读取大地图建筑数据。
        /// </summary>
        private void FetchBuildingDataFromSave()
        {
            AllBuildingDatas.Clear();
            foreach (var info in DataManager.Instance.GetTownBuildingInfos())
            {
                AllBuildingDatas[info.tileId] = new BuildingInfo
                {
                    tileId = info.tileId, //8个
                    name = info.name,
                    playerId = info.playerId,
                    status = info.status,
                    buildingLevel = info.buildingLevel,
                    buildingTime = info.buildingTime,
                    buildingId = info.buildingId,
                    value = info.value,
                    celebrationTime = info.celebrationTime,
                    displayedAchievementId = info.displayedAchievementId
                };
            }
        }

        /// <summary>
        /// 应用虚拟测试数据如果需要。
        /// </summary>
        private void ApplyVirtualTestDataIfNeeded()
        {
            if (!useVirtualTestData)
            {
                return;
            }

            // 大地图 测试模式下先放 4 个不属于自己的建筑，方便验证 界面 和点击限制。
            ApplyVirtualBuilding(1, 21, "董政", Random.Range(1, 4), 2, 0, 0);
            ApplyVirtualBuilding(2, 22, "奇泽", Random.Range(1, 4), 2, 0, 0);
            ApplyVirtualBuilding(3, 23, "笛子", Random.Range(1, 4), 2, 0, 0);
            ApplyVirtualBuilding(5, 24, "春华", Random.Range(1, 4), 2, 0, 0);
        }

        private void ApplyVirtualBuilding(
            int tileId,
            int playerId,
            string name,
            int buildingLevel,
            int status,
            int buildingTime,
            int celebrationTime)
        {
            AllBuildingDatas[tileId] = new BuildingInfo
            {
                tileId = tileId,
                playerId = playerId,
                name = name,
                buildingId = 1,
                buildingLevel = buildingLevel,
                status = status,
                buildingTime = buildingTime,
                celebrationTime = celebrationTime
            };
        }

        /// <summary>
        /// 从后端 land 信息里解析地块编号，兼容 id 与 areaCode 两种字段。
        /// </summary>
        /// <param name="land">后端返回的地块信息。</param>
        /// <param name="tileId">解析出的地块编号。</param>
        /// <returns>解析成功返回 true。</returns>
        private static bool TryResolveTileIdFromLand(ApiProtocols.LandInfo land, out int tileId)
        {
            tileId = 0;
            if (land == null)
            {
                return false;
            }

            // areaCode 才是场景地块编号，id 可能只是后端土地记录编号。
            if (TryParsePositiveInt(land.areaCode, out tileId))
            {
                return true;
            }

            if (TryParsePositiveInt(land.id, out tileId))
            {
                return true;
            }

            // areaCode 可能是类似 "tile_3" / "A3" 这类格式，兜底提取其中的数字。
            var source = string.IsNullOrWhiteSpace(land.areaCode) ? land.id : land.areaCode;
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            var digits = System.Text.RegularExpressions.Regex.Match(source, "\\d+").Value;
            return TryParsePositiveInt(digits, out tileId);
        }

        /// <summary>
        /// 解析正整数文本，过滤 0 与非法值。
        /// </summary>
        /// <param name="raw">原始文本。</param>
        /// <param name="value">解析值。</param>
        /// <returns>解析成功返回 true。</returns>
        private static bool TryParsePositiveInt(string raw, out int value)
        {
            if (int.TryParse(raw, out value) && value > 0)
            {
                return true;
            }

            value = 0;
            return false;
        }
        
        private void SpawnWarning(string text, Transform parent, GameObject obj = null, bool isRed = true)
        {
            var prefab = ResolveWarningPrefab();
            if (prefab == null)
            {
                Debug.LogWarning($"[TileManager] 缺少提示预制体：{WarningPrefabPath}");
                return;
            }

            var warning = Instantiate(prefab, parent);
            var warningText = warning.GetComponent<TMP_Text>();
            if (warningText == null)
            {
                warningText = warning.GetComponentInChildren<TMP_Text>(true);
            }

            if (warningText != null)
            {
                warningText.text = text;
            }

            if (!isRed)
            {
                if (warningText != null)
                {
                    warningText.color = Color.white;
                }
            }
            var currentPos = warning.transform.position;
            if (obj == null)
            {
                warning.transform.position = new Vector3(currentPos.x + 25, currentPos.y + 45, currentPos.z);
            }
            else
            {
                var pos = Camera.main.WorldToScreenPoint(obj.transform.position);
                warning.transform.position = new Vector3(pos.x + 25, pos.y + 45, pos.z);
            }
            var newPos = warning.transform.position;

            warning.transform.DOMove(new Vector3(newPos.x + 45f, newPos.y + 65f, newPos.z), 1.5f).SetEase(Ease.InQuad);
            var canvas = warning.GetComponent<CanvasGroup>();
            canvas.DOFade(0f, 1f).SetDelay(.5f);
            Destroy(warning.gameObject, 2f);
        }

        /// <summary>
        /// 获取提示预制体，优先用场景拖拽，其次走运行时资源加载。
        /// </summary>
        /// <returns>提示预制体。</returns>
        private GameObject ResolveWarningPrefab()
        {
            if (warningPrefab != null)
            {
                return warningPrefab;
            }

            warningPrefab = GameplayResourceStore.LoadAsset<GameObject>(WarningPrefabPath);
            return warningPrefab;
        }

        /// <summary>
        /// 销毁时释放监听、协程和运行时缓存。
        /// </summary>
        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            var panel = UIKit.GetPanel<BuildingItemSceneController>();
            if (panel != null)
            {
                UIKit.ClosePanel<BuildingItemSceneController>();
            }
        }
    }
}
