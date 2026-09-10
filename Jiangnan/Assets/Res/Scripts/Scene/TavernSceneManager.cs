using System;
using System.Collections;
using System.Collections.Generic;
using cfg;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.Model;
using JN.Client.UI;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JN.Client.Scene
{
    /// <summary>
    /// 负责酒楼场景相关的运行时逻辑。
    /// </summary>
    public partial class TavernSceneManager : MonoBehaviour
    {
        private const float NavMeshSampleDistance = 2f;
        private const int GrantCanCheckoutTechId = 103;
        private const int GrantVisitCustomerTechId = 104;
        /// <summary>单轮营业时长（秒）；运行时由 TbConfig.businessHours 覆盖，默认与表一致。</summary>
        public float BusinessHours = 180f;

        public static TavernSceneManager Instance;

        public Dictionary<int, TableArea> AllTables = new();
        private readonly List<GameObject> customerTemplates = new();
        /// <summary>按酒楼等级过滤后的普通顾客模板缓存（避免每次刷客分配）。</summary>
        private readonly List<GameObject> levelFilteredCustomerTemplates = new();
        private readonly List<GameObject> vipCustomerTemplates = new();
        /// <summary>稀客模板池（固定 CustomerM6）。</summary>
        private readonly List<GameObject> rareCustomerTemplates = new();
        private readonly List<GameObject> dishPrefabs = new();
        private readonly List<StagedDishEntry> stagedDishEntries = new();
        private readonly List<Transform> queuePointAnchors = new();
        private readonly Dictionary<int, CookOrderTicket> tableCookOrderTickets = new();
        private readonly List<TavernCustomerRuntimeController> activeCustomers = new();
        private readonly List<TavernCustomerRuntimeController> queuedCustomers = new();
        private readonly Dictionary<TavernCustomerRuntimeController, GameObject> vipGuestActionRoots = new();
        private readonly HashSet<TavernCustomerRuntimeController> pendingSecondFloorVipCustomers = new();
        private readonly Dictionary<int, TavernCustomerRuntimeController> tableCustomers = new();
        private readonly Dictionary<int, List<TavernCustomerRuntimeController>> tableCustomerGroups = new();
        /// <summary>前台点单软预留：桌位 → 仍在排队、由前台处理点单的顾客。</summary>
        private readonly Dictionary<int, List<TavernCustomerRuntimeController>> frontCounterOrderBindings = new();
        /// <summary>前台点单进行中的协程（按桌）。</summary>
        private readonly Dictionary<int, Coroutine> frontCounterOrderRoutines = new();
        /// <summary>掌柜头顶点单进度：各桌开始时间。</summary>
        private readonly Dictionary<int, float> frontCounterOrderProgressStarts = new();
        /// <summary>掌柜头顶点单进度：各桌预计时长。</summary>
        private readonly Dictionary<int, float> frontCounterOrderProgressDurations = new();
        /// <summary>掌柜头顶点单进度条 HUD 根节点。</summary>
        private GameObject shopkeeperOrderProgressHud;
        /// <summary>场景 Objects/FrontTableOrder：前台「点单中」状态文案跟随锚点。</summary>
        private GameObject frontTableOrderAnchor;
        /// <summary>挂在 FrontTableOrder 上的「点单中」世界标签。</summary>
        private GuideWorldLabel frontTableOrderStatusLabel;
        private const string FrontTableOrderAnchorName = "FrontTableOrder";
        private const string FrontTableOrderStatusLabelName = "FrontTableOrderStatus";
        private const string FrontTableOrderStatusText = "点单中";
        private const string FrontTableOrderStatusPrefabPath =
            "Assets/Res/Resources/UI/Runtime/FrontTableOrderStatus.prefab";
        private const int FrontCounterOrderSlotCount = 2;
        private readonly Dictionary<int, Coroutine> autoCleanRoutines = new();
        private readonly Dictionary<int, GameObject> activeCleanSmokeEffects = new();
        private readonly Dictionary<int, string> checkoutRuntimeTextOverrides = new();
        private readonly HashSet<int> checkoutCoinFlyPreplayedTableIds = new();
        private readonly TavernTableStateService tableStateService = new();
        private readonly TavernWaitSatisfactionTracker waitSatisfactionTracker = new();
        private readonly TavernWaiterTaskWaitTracker waiterTaskWaitTracker = new();
        private readonly TavernCustomerFlowService customerFlowService = new();
        private readonly TavernCustomerPlacementService customerPlacementService = new();
        private readonly TavernCustomerSpawnService customerSpawnService = new();
        private readonly Dictionary<GameObject, ChefCharacter> chefRuntimeContexts = new();
        private readonly Dictionary<TavernCustomerRuntimeController, CustomerCharacter> customerRuntimeContexts = new();
        private readonly Dictionary<GameObject, Coroutine> chefTaskRoutines = new();
        private readonly Dictionary<GameObject, int> chefCookAssignments = new();
        private readonly HashSet<int> assignedCookTableIds = new();
        private readonly HashSet<GameObject> busyChefs = new();
        private readonly Dictionary<int, Coroutine> vipOrderInteractionTimeoutRoutines = new();
        private readonly Dictionary<int, Coroutine> orderBubbleAutoHideRoutines = new();
        private const float OrderBubbleDisplayDurationSeconds = 5f;
        private readonly TavernChefDispatchService chefTaskDispatchService = new();
        // 处于待升级流程的桌位编号集合：阻止顾客入座，保证升级动画期间桌子始终空闲
        private readonly HashSet<int> pendingUpgradeTableIds = new();
        private readonly HashSet<int> guidePendingTablePlacementIds = new();
        private readonly Dictionary<string, GameObject> guideStaffVisuals = new();
        private readonly Dictionary<string, List<GameObject>> guideStaffVisualGroups = new();
        // 正在播放入场动画的员工集合：刷新世界状态时不要把它们瞬移回锚点。
        private readonly HashSet<GameObject> staffVisualsBeingAnimated = new();
        private readonly HashSet<string> guidePendingKitchenItems = new();
        private readonly List<GuideWorldButton> guideWorldButtons = new();
        private readonly List<GuideWorldLabel> guideWorldLabels = new();
        private readonly List<GuidePurchaseAnchor> guideKitchenAnchors = new();
        /// <summary>三星上楼按钮（跟随「楼梯建造」挂点）。</summary>
        private GameObject upStairButtonRoot;
        /// <summary>拉客按钮（跟随「轿子建造」挂点）。</summary>
        private GameObject myDrumUpButtonRoot;
        private readonly Dictionary<GameObject, Coroutine> waiterTaskRoutines = new();
        private readonly Dictionary<GameObject, Coroutine> waiterHomeReturnRoutines = new();
        private readonly Dictionary<GameObject, GameObject> activeWaiterOrderCookProgress = new();
        private readonly HashSet<GameObject> busyWaiters = new();
        private int reservedServeDishCount;

        [Header("UI 跟随设置")]
        [SerializeField] public Transform canvasParent;
        [SerializeField] public Camera SceneCamera;
        [SerializeField] private List<GameObject> tableMovePrefabList = new();

        [Header("Gameplay")]
        [SerializeField] private List<GameObject> customerPrefabAssets = new();
        [SerializeField, Tooltip("贵客/稀客专用预制体：贵客固定 CustomerM5，稀客固定 CustomerM6；不进入普通随机池。刷贵客只读 vipSpawnChancePermille，不依赖科技；稀客按 rareSpawnChancePermille 刷出。")]
        private List<GameObject> vipCustomerPrefabAssets = new();
        private const float DefaultVipSpawnChance = 0.35f;
        private const float DefaultRareSpawnChance = 0.5f;
        private const float DefaultVipAttractSpawnChanceMultiplier = 1f;
        private float vipSpawnChance = DefaultVipSpawnChance;
        private float rareSpawnChance = DefaultRareSpawnChance;
        private float vipAttractSpawnChanceMultiplier = DefaultVipAttractSpawnChanceMultiplier;
        private const int MaxConcurrentShopVipCustomers = 1;
        private const int MaxConcurrentShopRareCustomers = 3;
        [SerializeField] private float customerSpawnInterval = 14f;
        [SerializeField] private float dishCookInterval = 5f;
        [SerializeField] private float dishEatDuration = 5f;
        [SerializeField] private float autoCleanDuration = 2f;
        [SerializeField] private float weekSpeedUpDuration = 10f;
        [SerializeField, Tooltip("点单读条时长（秒），来自 tbconfig.orderTime，固定值。")]
        private float waiterOrderDuration = 3f;
        [SerializeField, HideInInspector] private float waiterOrderDurationSkilled = 3f;
        [SerializeField, Tooltip("桌边上菜服务读条时长（秒），来自 tbconfig.waiterServeTime，固定值，不含寻路。")]
        private float waiterServeDuration = 3f;
        [SerializeField, HideInInspector] private float waiterServeDurationSkilled = 3f;
        [SerializeField] private float waiterCheckoutDuration = 3f;
        [SerializeField] private float waiterStealDuration = 3f;
        [SerializeField] private float waiterStealCooldown = 10f;
        [SerializeField] private int tableCheckoutIncome = 120;
        [SerializeField] private int maxQueueSize = 4;
        [SerializeField] private int maxActiveCustomers = 8;
        [SerializeField] private float queueSpacing = 0.3f;
        [SerializeField] private float spawnLaneSpacing = 0.2f;

        private Transform customerEntryPoint;
        private Transform customerSpawnPoint;
        private Transform waiterAttractPoint;
        private Transform customerExitPoint;
        private Transform objectMovePoint;
        private Transform sceneObjectsRoot;
        private Canvas sceneCanvas;
        private GameObject guideCounterObject;
        private GameObject guideCounterBuildBase;
        private GameObject guideStoveObject;
        private GameObject guideStoveBuildBase;
        private readonly GameObject[] guideStoveFireObjects = new GameObject[3];
        private static readonly string[] GuideStoveFireObjectNames = { "CFXR Fire_1", "CFXR Fire_2", "CFXR Fire_3" };
        private GameObject foodTableObject;
        private GameObject guideSteamerObject;
        private GameObject platePrefab;
        private readonly List<GameObject> guideStoveSceneObjects = new();
        private GuideWorldButton guideCounterButton;
        private GuideWorldButton guideStoveButton;
        private GuideWorldButton guideShopkeeperButton;
        private GuideWorldButton guideChefButton;
        private GuideWorldButton guideWaiterButton;
        private GameObject guideShopkeeperRecruitBase;
        private GameObject guideChefRecruitBase;
        private GameObject guideWaiterRecruitBase;
        private GuideWorldLabel nextCustomerTimerLabel;
        private Coroutine chefServiceRoutine;
        private Coroutine waiterServiceRoutine;
        private Coroutine waiterTaskRoutine;
        private Coroutine closeBusinessRoutine;
        private bool customerSpawnLoopActive;
        private bool isClosingBusiness;
        private bool softClosingStarted;
        private bool postCloseCleanupActive;
        private Coroutine softClosingDismissRoutine;
        private Coroutine postCloseCleanupRoutine;
        /// <summary>离店写快照前已中断全部协程；阻止 Update 在切场景前再拉起服务循环。</summary>
        private bool runtimeCoroutinesHaltedForLeave;
        public const float SoftClosingLeadSeconds = 10f;
        private const float ClosingCustomerExitTimeoutSeconds = 120f;
        private const float ClosingTableCleanupTimeoutSeconds = 60f;
        private bool hasNavMesh;
        private float nextCustomerSpawnRemaining = -1f;
        private float businessOpenElapsedSeconds;
        /// <summary>本次营业是否已触发开业后第二波高峰刷客。</summary>
        private bool peakSecondWaveTriggered;
        /// <summary>用于检测酒楼星级提升并触发高峰（声望变化信号也会发，需对比等级）。</summary>
        private int lastHandledTavernLevel = -1;
        /// <summary>已与楼梯解锁状态对齐过贵客气泡，避免进店时误当成刚建成二楼。</summary>
        private bool vipBubbleStairsUnlockSynced;
        private bool vipBubbleSyncedStairsUnlocked;
        /// <summary>高峰刷客期间临时抬高活跃顾客上限；0 表示不覆盖。</summary>
        private int peakSpawnActiveCapacityOverride;
        /// <summary>低谷刷客期间临时抬高活跃顾客上限；0 表示不覆盖。</summary>
        private int valleySpawnActiveCapacityOverride;
        /// <summary>高峰分批：本波还剩多少预定进客。</summary>
        private int peakSpawnRemainingGuests;
        /// <summary>低谷分批：本波还剩多少预定进客。</summary>
        private int valleySpawnRemainingGuests;
        /// <summary>高峰分批是否进行中。</summary>
        private bool peakSpawnBatchActive;
        /// <summary>低谷分批是否进行中。</summary>
        private bool valleySpawnBatchActive;
        /// <summary>高峰分批下一批倒计时（秒）。</summary>
        private float peakSpawnBatchCooldown;
        /// <summary>低谷分批下一批倒计时（秒）。</summary>
        private float valleySpawnBatchCooldown;
        private float queuedCustomerAssignCooldown;
        private float customerCoefficient = 1f;
        private float priceCoefficient = 1f;
        private float serviceSpeedCoefficient = 1f;
        private int nextChefCookIndex;

        public bool IsClosingBusiness => isClosingBusiness;
        public bool IsBusinessActive =>
            DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern
                ? visitSimulationActive && !isClosingBusiness
                : DataManager.Instance?.TavernData != null
                  && DataManager.Instance.TavernData.isOpen
                  && customerSpawnLoopActive
                  && !isClosingBusiness;

        private class GuideWorldButton
        {
            public RectTransform rectTransform;
            public Button button;
            public Image image;
            public Text text;
            public TMP_Text tmpText;
            public Transform target;
            public Vector3 worldOffset;
            public Vector3 scale = Vector3.one;
        }

        private class GuideWorldLabel
        {
            public RectTransform rectTransform;
            public Text text;
            public TMP_Text tmpText;
            public Image progressBackground;
            public Image progressFill;
            public Image queueBackground;
            public CanvasGroup canvasGroup;
            public Transform target;
            public Vector3 worldOffset;
            public Vector3 scale = Vector3.one;
            public Sprite defaultProgressSprite;
            public Sprite queuedProgressSprite;
        }

        private class GuidePurchaseAnchor
        {
            public string itemKey;
            public string displayName;
            public GameObject sceneObject;
            public GameObject buildBase;
            public GuideWorldButton button;
            public string carrierPrefabPath;
        }

        private class StagedDishEntry
        {
            public int tableId;
            public int slotIndex;
            public int stackLayer;
            public GameObject rootObject;
            public GameObject dishPrefab;
        }

        private class CookOrderTicket
        {
            public int tableId;
            public Sprite icon;
            public float cookStartedAt;
            public float cookDuration;
            public bool isChefNotified;
            public bool isCooking;
            public bool isCompleted;
        }

        /// <summary>
        /// 处理点击场景中的购买提示底板。
        /// </summary>
        /// <param name="pointerPosition">屏幕坐标。</param>
        /// <returns>命中购买提示并消费点击时返回 true。</returns>
        public static bool TryHandlePurchasePointerClick(Vector2 pointerPosition)
        {
            if (Camera.main == null)
            {
                return false;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return false;
            }

            if (Instance != null)
            {
                var ray = Camera.main.ScreenPointToRay(pointerPosition);
                var hits = Physics.RaycastAll(ray, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
                for (var index = 0; index < hits.Length; index++)
                {
                    var hitCollider = hits[index].collider;
                    if (hitCollider == null)
                    {
                        continue;
                    }

                    if (Instance.TryHandleGuideBuildBaseClick(hitCollider))
                    {
                        GameAudioManager.PlayButtonClick();
                        return true;
                    }
                }
            }

            // 二楼场景无 TavernSceneManager，建造板点击改由二楼购买控制器处理。
            return TavernSecondFloorFacilityPurchaseController.TryHandlePurchasePointerClick(pointerPosition);
        }

        /// <summary>
        /// 点击场景中的厨师/小二模型。
        /// 当前屏蔽场景点击打开员工详情弹窗，面板本身与其它 UI 入口保留。
        /// </summary>
        public static bool TryHandleStaffPointerClick(Vector2 pointerPosition, int pointerId = -1)
        {
            return false;
        }

        /// <summary>
        /// 获取当前运行时已缓存的餐盘预制体。
        /// </summary>
        /// <returns>餐盘预制体；未加载时返回 null。</returns>
        public GameObject GetPlatePrefab()
        {
            return platePrefab;
        }

        /// <summary>
        /// 初始化组件引用和运行时状态。
        /// </summary>
        private void Awake()
        {
            Instance = this;
            tableStateService.BindTaskWaitTracker(waiterTaskWaitTracker);
            Signals.Get<TavernBusinessStateSignal>().AddListener(HandleBusinessStateChanged);
            Signals.Get<GameplayGuideProgressSignal>().AddListener(RefreshGuideWorldState);
            Signals.Get<TavernRuntimeChangedSignal>().AddListener(HandleTavernRuntimeChanged);
            Signals.Get<TavernPrestigeChangedSignal>().RemoveListener(HandleTavernPrestigeChanged);
            Signals.Get<TavernPrestigeChangedSignal>().AddListener(HandleTavernPrestigeChanged);
        }

        /// <summary>
        /// 销毁时释放监听、协程和运行时缓存。
        /// </summary>
        private void OnDestroy()
        {
            Signals.Get<TavernBusinessStateSignal>().RemoveListener(HandleBusinessStateChanged);
            Signals.Get<GameplayGuideProgressSignal>().RemoveListener(RefreshGuideWorldState);
            Signals.Get<TavernRuntimeChangedSignal>().RemoveListener(HandleTavernRuntimeChanged);
            Signals.Get<TavernPrestigeChangedSignal>().RemoveListener(HandleTavernPrestigeChanged);
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 在场景启动后补齐依赖并刷新初始显示。
        /// </summary>
        private void Start()
        {
            ResolveSceneAnchors();
            ConfigureSceneUiCanvas();
            ResolveGuideSceneObjects();
            InitTablesAndUIs();
            EnsureGuideWorldButtons();
            EnsureGuideWorldLabels();
            CacheCustomerTemplates();
            CacheDishPrefabs();
            ApplyTimingConfig();
            hasNavMesh = TryGetNavMeshPosition(customerEntryPoint != null ? customerEntryPoint.position : Vector3.zero, out _);

            DataManager.Instance.RemoveTemporaryGuideWaiters();
            // 拜访他人酒楼：禁止 ResetTransient（避免把自家营业桌态清 Idle 落盘），只应用解锁/等级。
            if (DataManager.Instance.IsVisitingOtherTavern)
            {
                ApplyUnlockedTablesOnly();
                RefreshGuideWorldState();
                StartCoroutine(DeferredApplyPurchasedFacilityBuildVisuals());
                RefreshNextCustomerTimerLabel();
                RefreshAllTableRuntimeState();
                RefreshBackgroundCrowdVolume();
                TryBeginVisitPullSimulation();
                if (hasNavMesh)
                {
                    StartBusinessLoop();
                }

                return;
            }

            // 本次启动第一次进自家店：会话轮次从第 1 轮起。
            DataManager.Instance.NotifyEnteredOwnTavernSessionTurn();
            var freshSessionRound = DataManager.Instance.ConsumeSessionBusinessCountdownFreshStart();

            // 首次开业前进店保持停业，等开业按钮；
            // 已开业过则保持/恢复营业，进入三分钟循环，不再打烊。
            if (DataManager.Instance.GetBusinessOpenCount() <= 0)
            {
                if (DataManager.Instance.TavernData.isOpen)
                {
                    DataManager.Instance.SetTavernOpen(false, countAsNewRound: false);
                }
            }
            else if (!DataManager.Instance.TavernData.isOpen)
            {
                DataManager.Instance.SetTavernOpen(true, countAsNewRound: false);
            }

            var restoreFromSnapshot = DataManager.Instance.TavernData.isOpen
                                      && DataManager.Instance.HasValidTavernRuntimeSnapshot();

            // 有有效快照且营业中：跳过 Reset，保留存档桌态后按快照重建客人/工单。
            // 否则按原逻辑清临时桌态（并清空快照）。
            if (!restoreFromSnapshot)
            {
                DataManager.Instance.ResetTransientTavernState();
            }

            // 本会话首次进店：无快照时营业倒计时与峰谷计时从完整一轮重新开始。
            // 有快照时由 Restore 写回 businessOpenElapsed，避免清零。
            if (freshSessionRound && !restoreFromSnapshot)
            {
                ResetBusinessRoundClockForSessionStart();
            }

            ApplySavedTableStates();
            RefreshGuideWorldState();
            ApplyGuideFirstEnterCameraX();
            StartCoroutine(DeferredApplyPurchasedFacilityBuildVisuals());

            // 引导/员工视觉刷新后再 Restore，便于厨师续跑工单直接占位。
            if (restoreFromSnapshot)
            {
                RestoreOwnTavernRuntimeSnapshot();
            }

            // 缓存当前星级，避免进店时误把声望变化当成升星高峰。
            lastHandledTavernLevel = Mathf.Max(1, DataManager.Instance.GetTavernLevel());

            RefreshNextCustomerTimerLabel();
            RefreshAllTableRuntimeState();
            TryRevealTableLv2UpgradeFeature();
            RefreshBackgroundCrowdVolume();
            // 自家默认隐藏饺子；仅有待进店被拉客人时才显示。
            RefreshVisitJiaoziVisibility();
            // 有待卸客：轿子进场 → 间隔卸客 → 归位并清空容量。
            ScheduleHomeUnloadFinalizeIfNeeded();
            // 从城镇回店：按桌掷「被拉客」提示（上下楼不触发）。
            TryRollOwnTavernPulledTipsAfterEnter();
            // 引导：首次进店先播 enterTavern 视频，再 first_enter →（可能）employ / opening
            StartCoroutine(HudOverlayService.DeferredTryShowGuideDialogsAfterEnterOwnTavern());

            if (restoreFromSnapshot && hasNavMesh && DataManager.Instance.TavernData.isOpen)
            {
                StartBusinessLoop();
            }
        }

        /// <summary>
        /// 本会话首次进自家店：重置本轮计时，并按配置刷新完整营业时长。
        /// </summary>
        private void ResetBusinessRoundClockForSessionStart()
        {
            businessOpenElapsedSeconds = 0f;
            peakSecondWaveTriggered = false;
            StopPeakCustomerBatch();
            StopValleyCustomerBatch();
            RefreshTimingConfig();

            if (DataManager.Instance?.TavernData != null && DataManager.Instance.TavernData.isOpen)
            {
                Signals.Get<TavernBusinessStateSignal>().Dispatch(true);
            }
        }

        /// <summary>
        /// 进店首帧后再刷一次建成态，确保厨房桌子子节点切到不透明。
        /// </summary>
        private System.Collections.IEnumerator DeferredApplyPurchasedFacilityBuildVisuals()
        {
            yield return null;
            ForceApplyPurchasedKitchenBuildVisuals();
        }

        /// <summary>
        /// 开业
        /// </summary>
        public void OpenTavernBusiness()
        {
            if (!DataManager.Instance.CanOpenTavernBusiness())
            {
                return;
            }

            if (closeBusinessRoutine != null)
            {
                StopCoroutine(closeBusinessRoutine);
                closeBusinessRoutine = null;
            }

            isClosingBusiness = false;
            softClosingStarted = false;
            postCloseCleanupActive = false;
            if (postCloseCleanupRoutine != null)
            {
                StopCoroutine(postCloseCleanupRoutine);
                postCloseCleanupRoutine = null;
            }
            StopSoftClosingDismissRoutine();
            businessOpenElapsedSeconds = 0f;
            peakSecondWaveTriggered = false;
            StopPeakCustomerBatch();
            StopValleyCustomerBatch();
            TavernBusinessModifierService.Instance.ResetAll();
            RefreshTimingConfig();
            if (waiterServiceRoutine != null)
            {
                StopCoroutine(waiterServiceRoutine);
                waiterServiceRoutine = null;
            }

            ResetWaiterTaskState();
            ClearPreparedDishesForBusinessEnd();
            DataManager.Instance.ResetTransientTavernState();
            DataManager.Instance.SetTavernOpen(true);
            waitSatisfactionTracker.ClearAll();
            waiterTaskWaitTracker.ClearAll();
            ResetAllWaiterStamina();
            RefreshAllWaiterStateHuds();
            StartCounterRandomRewardTimer();
            // 刚开业同步进入高峰并弹出提示。
            BeginPeakCustomerWaveWithWarning();
        }

        /// <summary>
        /// 三分钟营业循环续轮：累加开业轮次并重置高峰计时；本轮高峰仍等配置秒数触发，不立刻刷客。
        /// </summary>
        public void BeginNextBusinessRound()
        {
            if (DataManager.Instance?.TavernData == null || !DataManager.Instance.TavernData.isOpen)
            {
                return;
            }

            if (isClosingBusiness || softClosingStarted)
            {
                isClosingBusiness = false;
                softClosingStarted = false;
                StopSoftClosingDismissRoutine();
            }

            DataManager.Instance.AdvanceBusinessOpenRound();
            businessOpenElapsedSeconds = 0f;
            peakSecondWaveTriggered = false;
            StopPeakCustomerBatch();
            StopValleyCustomerBatch();
            RefreshTimingConfig();

            if (!customerSpawnLoopActive)
            {
                customerSpawnLoopActive = true;
            }

            // 续轮后空桌可能已在，排队客需重新拉起前台点单/入座，避免只排队不入座。
            TryPrepareFrontCounterOrders();
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        /// <summary>
        /// 软打烊已停用：首次开业后改为三分钟循环，不停止接客。
        /// </summary>
        public void BeginSoftClosing()
        {
        }

        /// <summary>
        /// 打烊入口：已首次开业时改为续轮；引导期未开业仍保留旧协程以防异常调用。
        /// </summary>
        public void CloseTavernBusiness()
        {
            if (DataManager.Instance != null && DataManager.Instance.GetBusinessOpenCount() > 0)
            {
                BeginNextBusinessRound();
                return;
            }

            if (isClosingBusiness)
            {
                return;
            }

            if (closeBusinessRoutine != null)
            {
                StopCoroutine(closeBusinessRoutine);
            }

            closeBusinessRoutine = StartCoroutine(CloseTavernBusinessRoutine());
        }

        /// <summary>
        /// 打烊收尾：最后一名顾客离店后即可结算；桌位清理在后台继续完成。
        /// </summary>
        private IEnumerator CloseTavernBusinessRoutine()
        {
            isClosingBusiness = true;
            softClosingStarted = true;
            ResetCounterRandomReward();
            TavernBusinessModifierService.Instance.ResetAll();
            StopCustomerIntakeForClosing();
            PrepareWaiterTasksForNaturalClosing();

            // 未入座/排队顾客继续陆续离店；已入座的按点单/上菜/结账正常走完。
            if (softClosingDismissRoutine == null)
            {
                softClosingDismissRoutine = StartCoroutine(DismissNonSeatedCustomersGraduallyRoutine());
            }

            yield return WaitUntilClosingCustomersExited();
            StopSoftClosingDismissRoutine();

            var cleanupTableIds = CollectTablesNeedingClosingCleanup();
            MarkTablesForClosingCleanup(cleanupTableIds);
            var hasCleanupPending = HasClosingCleanupPending(cleanupTableIds);

            isClosingBusiness = false;
            softClosingStarted = false;
            closeBusinessRoutine = null;
            postCloseCleanupActive = hasCleanupPending;

            DataManager.Instance.RemoveTemporaryGuideWaiters();
            if (!hasCleanupPending)
            {
                DataManager.Instance.ResetTransientTavernState();
            }

            DataManager.Instance.SetTavernOpen(false);

            if (hasCleanupPending)
            {
                postCloseCleanupRoutine = StartCoroutine(PostCloseCleanupRoutine(cleanupTableIds));
            }
        }

        /// <summary>
        /// 结算弹出后继续等待小二清桌，完成后恢复空闲态。
        /// </summary>
        private IEnumerator PostCloseCleanupRoutine(HashSet<int> tableIds)
        {
            var wait = new WaitForSeconds(0.25f);
            var elapsed = 0f;
            while (HasClosingCleanupPending(tableIds))
            {
                if (DataManager.Instance.GetHiredGuideWaiterCount() <= 0
                    || GetGuideStaffVisuals(GuideWaiterVisualKey).Length <= 0)
                {
                    ForceFinishClosingCleanup(tableIds);
                    break;
                }

                if (waiterServiceRoutine == null)
                {
                    waiterServiceRoutine = StartCoroutine(WaiterServiceLoop());
                }

                elapsed += 0.25f;
                if (elapsed >= ClosingTableCleanupTimeoutSeconds)
                {
                    ForceFinishClosingCleanup(tableIds);
                    break;
                }

                yield return wait;
            }

            postCloseCleanupActive = false;
            postCloseCleanupRoutine = null;
            ClearPreparedDishesForBusinessEnd();
            DataManager.Instance.ResetTransientTavernState();

            if (DataManager.Instance?.TavernData != null && !DataManager.Instance.TavernData.isOpen)
            {
                if (waiterServiceRoutine != null)
                {
                    StopCoroutine(waiterServiceRoutine);
                    waiterServiceRoutine = null;
                }

                ResetWaiterTaskState();
            }
        }

        /// <summary>
        /// 打烊后只停接客，保留厨师/小二服务，让座位顾客把餐走完。
        /// </summary>
        private void StopCustomerIntakeForClosing()
        {
            customerSpawnLoopActive = false;
            nextCustomerSpawnRemaining = -1f;
            StopPeakCustomerBatch();
            StopValleyCustomerBatch();
        }

        private void StopSoftClosingDismissRoutine()
        {
            if (softClosingDismissRoutine == null)
            {
                return;
            }

            StopCoroutine(softClosingDismissRoutine);
            softClosingDismissRoutine = null;
        }

        /// <summary>
        /// 轻量打烊准备：叫醒小二、停止回位，保留进行中的点单/上菜/结账。
        /// </summary>
        private void PrepareWaiterTasksForNaturalClosing()
        {
            foreach (var pair in waiterHomeReturnRoutines)
            {
                if (pair.Value != null)
                {
                    StopCoroutine(pair.Value);
                }
            }

            waiterHomeReturnRoutines.Clear();
            WakeAllNappingWaitersForClosing();
        }

        /// <summary>
        /// 排队与未真正入座用餐的顾客陆续离店。
        /// </summary>
        private IEnumerator DismissNonSeatedCustomersGraduallyRoutine()
        {
            var wait = new WaitForSeconds(0.75f);
            while (DataManager.Instance != null
                   && DataManager.Instance.TavernData != null
                   && DataManager.Instance.TavernData.isOpen)
            {
                var dismissed = TryDismissOneNonSeatedCustomer();
                if (!dismissed)
                {
                    if (isClosingBusiness)
                    {
                        // 硬打烊阶段把剩余非座位顾客一次清完，避免卡死。
                        DismissAllNonSeatedCustomers();
                        yield break;
                    }

                    yield return wait;
                    continue;
                }

                yield return wait;
            }

            softClosingDismissRoutine = null;
        }

        private bool TryDismissOneNonSeatedCustomer()
        {
            for (var index = 0; index < queuedCustomers.Count; index++)
            {
                var customer = queuedCustomers[index];
                if (customer == null)
                {
                    continue;
                }

                RecordClosingWaitSatisfaction(customer);
                // LeaveTavern → NotifyCustomerLeftQueue：出列并让后排补到前排点位。
                customer.LeaveTavern();
                return true;
            }

            for (var index = 0; index < activeCustomers.Count; index++)
            {
                var customer = activeCustomers[index];
                if (customer == null || IsSeatedCustomerFinishingMeal(customer))
                {
                    continue;
                }

                RecordClosingWaitSatisfaction(customer);
                customer.LeaveTavern();
                return true;
            }

            return false;
        }

        private void DismissAllNonSeatedCustomers()
        {
            while (TryDismissOneNonSeatedCustomer())
            {
            }
        }

        /// <summary>
        /// 已入座并处于点单/上菜/用餐/结账流程中的顾客，打烊时继续正常完成。
        /// </summary>
        private bool IsSeatedCustomerFinishingMeal(TavernCustomerRuntimeController customer)
        {
            if (customer == null || customer.TableId <= 0)
            {
                return false;
            }

            var tableData = DataManager.Instance != null
                ? DataManager.Instance.GetTableData(customer.TableId)
                : null;
            if (tableData == null || !tableData.isUnlocked)
            {
                return false;
            }

            var state = (TavernTableRuntimeState)tableData.runtimeState;
            return state == TavernTableRuntimeState.WaitingOrder
                   || state == TavernTableRuntimeState.WaitingServe
                   || state == TavernTableRuntimeState.Dining
                   || state == TavernTableRuntimeState.Checkout;
        }

        /// <summary>
        /// 打烊开始后停止接客（兼容旧调用名）。
        /// </summary>
        private void StopCustomerIntakeAndCookingForClosing()
        {
            StopCustomerIntakeForClosing();
        }

        /// <summary>
        /// 收集打烊时需要清理或恢复的桌位。
        /// </summary>
        private HashSet<int> CollectTablesNeedingClosingCleanup()
        {
            var tableIds = new HashSet<int>();
            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance.GetTableData(tablePair.Key);
                if (tableData == null || !tableData.isUnlocked)
                {
                    continue;
                }

                var state = (TavernTableRuntimeState)tableData.runtimeState;
                if (state != TavernTableRuntimeState.Idle && state != TavernTableRuntimeState.Locked)
                {
                    tableIds.Add(tablePair.Key);
                }
            }

            foreach (var pair in tableCustomerGroups)
            {
                tableIds.Add(pair.Key);
            }

            foreach (var pair in tableCustomers)
            {
                tableIds.Add(pair.Key);
            }

            return tableIds;
        }

        /// <summary>
        /// 通知店内和队列中的所有顾客离店。
        /// </summary>
        private void DismissAllCustomersForClosing()
        {
            var customers = new List<TavernCustomerRuntimeController>(activeCustomers);
            for (var index = 0; index < customers.Count; index++)
            {
                var customer = customers[index];
                if (customer == null)
                {
                    continue;
                }

                RecordClosingWaitSatisfaction(customer);
                customer.LeaveTavern();
            }
        }

        private void RecordClosingWaitSatisfaction(TavernCustomerRuntimeController customer)
        {
            if (customer == null || DataManager.Instance == null)
            {
                return;
            }

            var queueWait = waitSatisfactionTracker.PeekQueueWait(customer.GetInstanceID());
            var orderWait = 0f;
            var serveWait = 0f;
            var checkoutWait = 0f;
            if (customer.TableId > 0)
            {
                var incomplete = waitSatisfactionTracker.PeekIncomplete(customer.TableId);
                orderWait = incomplete.OrderSeconds;
                serveWait = incomplete.ServeSeconds;
                checkoutWait = incomplete.CheckoutSeconds;
                if (incomplete.QueueSeconds > queueWait)
                {
                    queueWait = incomplete.QueueSeconds;
                }
            }

            DataManager.Instance.RecordForcedClosingWaitSatisfaction(
                queueWait,
                orderWait,
                serveWait,
                checkoutWait,
                customer.CountsAsInterruptedDiningOnClose);
            waitSatisfactionTracker.OnCustomerLeftQueue(customer.GetInstanceID());
        }

        /// <summary>
        /// 等待所有顾客真正离开场景。
        /// </summary>
        private IEnumerator WaitUntilClosingCustomersExited()
        {
            var elapsed = 0f;
            while (activeCustomers.Count > 0)
            {
                activeCustomers.RemoveAll(customer => customer == null);
                if (activeCustomers.Count == 0)
                {
                    break;
                }

                elapsed += Time.deltaTime;
                if (elapsed >= ClosingCustomerExitTimeoutSeconds)
                {
                    ForceExitRemainingCustomersForClosing();
                    break;
                }

                yield return null;
            }

            customerFlowService.ClearTrackingForClosing(queuedCustomers, tableCustomers, tableCustomerGroups);
            ClearAllFrontCounterOrderRoutines();
            frontCounterOrderBindings.Clear();
        }

        private void ForceExitRemainingCustomersForClosing()
        {
            var customers = new List<TavernCustomerRuntimeController>(activeCustomers);
            for (var index = 0; index < customers.Count; index++)
            {
                var customer = customers[index];
                if (customer == null)
                {
                    continue;
                }

                RecordClosingWaitSatisfaction(customer);
                customer.ForceExitTavern();
            }

            activeCustomers.RemoveAll(customer => customer == null);
        }

        /// <summary>
        /// 打烊/结算后清掉误刷新的顾客，避免未开业时仍有客人入内。
        /// </summary>
        private void PurgeStrayCustomersWhenClosed()
        {
            if (DataManager.Instance?.TavernData != null && DataManager.Instance.TavernData.isOpen)
            {
                return;
            }

            while (queuedCustomers.Count > 0)
            {
                var queued = queuedCustomers[0];
                if (queued == null)
                {
                    queuedCustomers.RemoveAt(0);
                    continue;
                }

                queued.LeaveTavern();
                // 兜底：若未成功出列，避免死循环。
                if (queuedCustomers.Count > 0 && queuedCustomers[0] == queued)
                {
                    queuedCustomers.RemoveAt(0);
                }
            }

            var customers = new List<TavernCustomerRuntimeController>(activeCustomers);
            for (var index = 0; index < customers.Count; index++)
            {
                customers[index]?.ForceExitTavern();
            }

            activeCustomers.RemoveAll(customer => customer == null);
            customerFlowService.ClearTrackingForClosing(queuedCustomers, tableCustomers, tableCustomerGroups);
            ClearAllFrontCounterOrderRoutines();
            frontCounterOrderBindings.Clear();
        }

        /// <summary>
        /// 打烊开始后，把需要收尾的桌位统一切到清理状态。
        /// </summary>
        private void MarkTablesForClosingCleanup(HashSet<int> tableIds)
        {
            foreach (var tableId in tableIds)
            {
                var tableData = DataManager.Instance.GetTableData(tableId);
                if (tableData == null || !tableData.isUnlocked)
                {
                    continue;
                }

                var alreadyCleaning = assignedCleanTableIds.Contains(tableId);
                DataManager.Instance.SetTableRuntimeState(tableId, TavernTableRuntimeState.Cleaning);
                waiterTaskWaitTracker.OnTableWaitStateChanged(tableId, TavernTableRuntimeState.Cleaning);
                if (!alreadyCleaning && AllTables.TryGetValue(tableId, out var table) && table != null)
                {
                    table.RefreshRuntimeState(TavernTableRuntimeState.Cleaning, "等待清理");
                    table.linkedUI?.StopStateCountdown();
                }
            }

            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        /// <summary>
        /// 判断打烊清理是否仍有未完成桌位。
        /// </summary>
        private bool HasClosingCleanupPending(HashSet<int> tableIds)
        {
            foreach (var tableId in tableIds)
            {
                var tableData = DataManager.Instance.GetTableData(tableId);
                if (tableData == null || !tableData.isUnlocked)
                {
                    continue;
                }

                var state = (TavernTableRuntimeState)tableData.runtimeState;
                if (state != TavernTableRuntimeState.Idle && state != TavernTableRuntimeState.Locked)
                {
                    return true;
                }

                if (assignedCleanTableIds.Contains(tableId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 缺少可用小二表现时兜底完成清理，避免打烊流程卡死。
        /// </summary>
        private void ForceFinishClosingCleanup(HashSet<int> tableIds)
        {
            foreach (var tableId in tableIds)
            {
                var tableData = DataManager.Instance.GetTableData(tableId);
                if (tableData == null || !tableData.isUnlocked)
                {
                    continue;
                }

                var state = (TavernTableRuntimeState)tableData.runtimeState;
                if (state != TavernTableRuntimeState.Idle && state != TavernTableRuntimeState.Locked)
                {
                    FinishCleaning(tableId);
                }
            }
        }

        /// <summary>
        /// 从 TbConfig 表读取玩法时间配置，并覆盖当前场景的默认时长。
        /// </summary>
        public void RefreshTimingConfig()
        {
            ApplyTimingConfig();
        }

        /// <summary>
        /// 从 TbConfig 表读取玩法时间配置，并覆盖当前场景的默认时长。
        /// </summary>
        private void ApplyTimingConfig()
        {
            customerSpawnInterval = TbConfigRuntime.GetCustomerRefreshTime(customerSpawnInterval);
            dishCookInterval = TbConfigRuntime.GetChefCookTime(dishCookInterval);
            dishEatDuration = TbConfigRuntime.GetCustomerEatTime(dishEatDuration);
            autoCleanDuration = TbConfigRuntime.GetTableCleanTime(autoCleanDuration);
            weekSpeedUpDuration = TbConfigRuntime.GetWeakSpeedUpTime(weekSpeedUpDuration);
            ApplyWaiterStaminaConfigFromTable();
            waiterOrderDuration = TbConfigRuntime.GetOrderTime(waiterOrderDuration);
            // 保留字段兼容旧序列化，与固定点单时长同步。
            waiterOrderDurationSkilled = waiterOrderDuration;
            waiterServeDuration = TbConfigRuntime.GetWaiterServeTime(waiterServeDuration);
            waiterServeDurationSkilled = waiterServeDuration;
            waiterCheckoutDuration = TbConfigRuntime.GetWaiterCheckoutTime(waiterCheckoutDuration);
            waiterStealDuration = TbConfigRuntime.GetWaiterStealTime(waiterStealDuration);
            waiterStealCooldown = TbConfigRuntime.GetWaiterStealCooldown(waiterStealCooldown);
            // 回退用表默认 180，避免场景序列化残留 20 导致首帧倒计时只剩 20 秒。
            BusinessHours = TbConfigRuntime.GetBusinessHours(180f);
            // 贵客概率按当前场景酒楼等级实时取表，此处仅缓存默认档便于 Inspector 观察。
            var spawnLevel = DataManager.Instance != null
                ? DataManager.Instance.GetSceneTavernLevelForSpawn()
                : 1;
            vipSpawnChance = TbConfigRuntime.GetVipSpawnChanceForLevel(spawnLevel, DefaultVipSpawnChance);
            rareSpawnChance = TbConfigRuntime.GetRareSpawnChanceForLevel(spawnLevel, DefaultRareSpawnChance);
            vipAttractSpawnChanceMultiplier = TbConfigRuntime.GetVipAttractSpawnChanceMultiplier(DefaultVipAttractSpawnChanceMultiplier);
            if (DataManager.Instance != null)
            {
                BusinessHours *= DataManager.Instance.GetTechBusinessHoursMul();
            }
        }

        /// <summary>
        /// 当前生效的单轮营业秒数（每轮开始时从配置刷新，避免沿用场景默认值）。
        /// </summary>
        public float GetResolvedBusinessHoursSeconds()
        {
            RefreshTimingConfig();
            return Mathf.Max(1f, BusinessHours);
        }

        /// <summary>
        /// 本轮经营剩余秒数（顶栏倒计时与快照共用；冻结续跑以该值为准）。
        /// </summary>
        public float GetBusinessRemainingSeconds()
        {
            var total = GetResolvedBusinessHoursSeconds();
            return Mathf.Max(0f, total - businessOpenElapsedSeconds);
        }

        /// <summary>
        /// 逐帧处理输入、状态推进或动画刷新。
        /// </summary>
        private void Update()
        {
            if (runtimeCoroutinesHaltedForLeave)
            {
                return;
            }

            if (DataManager.Instance == null || DataManager.Instance.TavernData == null)
            {
                return;
            }

            RefreshBackgroundCrowdVolume();

            // 拜访他人酒楼：独立模拟（无峰谷、可拉客），不依赖自家 isOpen。
            if (DataManager.Instance.IsVisitingOtherTavern)
            {
                if (!visitSimulationActive)
                {
                    TryBeginVisitPullSimulation();
                }

                if (!customerSpawnLoopActive && hasNavMesh)
                {
                    StartBusinessLoop();
                }

                TickVisitPullSimulation(Time.deltaTime);
                RefreshNextCustomerTimerLabel();
                return;
            }

            if (visitSimulationActive)
            {
                EndVisitPullSimulation();
            }

            // 回店卸客时序不依赖营业中：进店后即可播轿子进场/卸客。
            TickPendingPulledCustomerEnter(Time.deltaTime);

            if (!DataManager.Instance.TavernData.isOpen)
            {
                nextCustomerSpawnRemaining = -1f;
                businessOpenElapsedSeconds = 0f;
                peakSecondWaveTriggered = false;
                StopPeakCustomerBatch();
                StopValleyCustomerBatch();
                ResetCounterRandomReward();
                RefreshNextCustomerTimerLabel();
                return;
            }

            if (isClosingBusiness)
            {
                customerSpawnLoopActive = false;
                nextCustomerSpawnRemaining = -1f;
                ResetCounterRandomReward();
                RefreshNextCustomerTimerLabel();
                return;
            }

            // 厨师做菜 / 前台点单：计时会话推进（非协程）。
            TickChefCookSessions();
            TickFrontCounterOrderSessions();

            businessOpenElapsedSeconds += Time.deltaTime;

            if (!customerSpawnLoopActive && hasNavMesh)
            {
                StartBusinessLoop();
            }

            UpdateWaiterPassiveStaminaRecovery(Time.deltaTime);
            UpdateChefPassiveStaminaRecovery(Time.deltaTime);
            TickCounterRandomReward(Time.deltaTime);
            // 固定时间高峰已停用：改由酒楼升级触发，见 TryTriggerPeakWaveAfterTavernUpgrade。
            // TickPeakCustomerSecondWave();
            TickPeakCustomerBatch(Time.deltaTime);
            TickValleyCustomerWave();
            TickValleyCustomerBatch(Time.deltaTime);

            if (customerSpawnLoopActive && nextCustomerSpawnRemaining < 0f)
            {
                nextCustomerSpawnRemaining = GetEffectiveCustomerSpawnInterval();
            }

            // 高峰/低谷分批进客期间暂停常规刷客，结束后再恢复倒计时。
            // 卸客时序已在上方 TickPendingPulledCustomerEnter 推进。
            if (customerSpawnLoopActive
                && nextCustomerSpawnRemaining >= 0f
                && !peakSpawnBatchActive
                && !valleySpawnBatchActive)
            {
                nextCustomerSpawnRemaining = Mathf.Max(0f, nextCustomerSpawnRemaining - Time.deltaTime);
                if (nextCustomerSpawnRemaining <= 0f)
                {
                    SpawnCustomerIfPossible();
                    nextCustomerSpawnRemaining = GetEffectiveCustomerSpawnInterval();
                }
            }

            RefreshNextCustomerTimerLabel();
            TickFrontCounterOrderPreparation(Time.deltaTime);
            TickAchievementSystems(Time.deltaTime);
        }

        /// <summary>
        /// 刷新全部桌位运行时状态。
        /// </summary>
        private void HandleTavernRuntimeChanged()
        {
            RefreshAllTableRuntimeState();
            RefreshCustomerSpawnInterval();
            RefreshInteriorExpandHud();
        }

        private void RefreshAllTableRuntimeState()
        {
            if (DataManager.Instance == null)
            {
                return;
            }

            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance.GetTableData(tablePair.Key);
                if (tableData == null || !tableData.isUnlocked)
                {
                    tablePair.Value.ApplySaveState(tableData);
                    continue;
                }

                var state = (TavernTableRuntimeState)tableData.runtimeState;
                tablePair.Value.RefreshRuntimeState(state, ResolveTableRuntimeTextOverride(tablePair.Key, state));
            }
        }

        private string ResolveTableRuntimeTextOverride(int tableId, TavernTableRuntimeState state)
        {
            if (state != TavernTableRuntimeState.Checkout)
            {
                return null;
            }

            return checkoutRuntimeTextOverrides.TryGetValue(tableId, out var customText)
                ? customText
                : null;
        }

        private void SetCheckoutRuntimeTextOverride(int tableId, string customText)
        {
            if (tableId <= 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(customText))
            {
                checkoutRuntimeTextOverrides.Remove(tableId);
                return;
            }

            checkoutRuntimeTextOverrides[tableId] = customText;
        }

        private void ClearCheckoutRuntimeTextOverride(int tableId)
        {
            if (tableId <= 0)
            {
                return;
            }

            checkoutRuntimeTextOverrides.Remove(tableId);
        }

        private bool TryPlayCoinBurstThenFlyToTop(Transform source, Action onAllCoinsSettled = null)
        {
            var coinTarget = GOReferenceManager.Instance != null ? GOReferenceManager.Instance.GetCoinTransform() : null;
            if (source == null || coinTarget == null)
            {
                onAllCoinsSettled?.Invoke();
                return false;
            }

            GameUIEffects.PlayCoinsBurstThenFly(source, coinTarget, onAllCoinsSettled: onAllCoinsSettled);
            return true;
        }

        private bool TryPlayCoinFlyToTop(Transform source, Action onAllCoinsSettled = null)
        {
            var coinTarget = GOReferenceManager.Instance != null ? GOReferenceManager.Instance.GetCoinTransform() : null;
            if (source == null || coinTarget == null)
            {
                onAllCoinsSettled?.Invoke();
                return false;
            }

            GameUIEffects.PlayCoinsFly(source, coinTarget, onAllCoinsSettled);
            return true;
        }

        private void MarkCheckoutCoinFlyPreplayed(int tableId)
        {
            if (tableId <= 0)
            {
                return;
            }

            checkoutCoinFlyPreplayedTableIds.Add(tableId);
        }

        private bool ConsumeCheckoutCoinFlyPreplayed(int tableId)
        {
            return tableId > 0 && checkoutCoinFlyPreplayedTableIds.Remove(tableId);
        }

        private void ClearCheckoutCoinFlyPreplayed(int tableId)
        {
            if (tableId <= 0)
            {
                return;
            }

            checkoutCoinFlyPreplayedTableIds.Remove(tableId);
        }

        /// <summary>
        /// 启动移动桌位。
        /// </summary>
        /// <param name="桌位编号">数据编号。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        public bool StartMoveTable(int tableId)
        {
            if (tableId <= 0 || tableId > tableMovePrefabList.Count)
            {
                return false;
            }

            var tableMovePrefab = tableMovePrefabList[tableId - 1];
            if (tableMovePrefab == null)
            {
                return false;
            }

            PrepareMovePrefabForManualMovement(tableMovePrefab);

            var moveSignal = tableMovePrefab.GetComponent<MoveRotateSignal>();
            if (moveSignal != null)
            {
                moveSignal.ConfigureTableId(tableId);
                moveSignal.OnArrived -= HandleTableMoveArrived;
                moveSignal.OnArrived += HandleTableMoveArrived;
                // 升级时同一个 prefab 会被多次激活，必须先把内部状态机和位姿
                // 还原到初始点，否则会出现 finished=true 立刻不动的卡住现象。
                moveSignal.ResetMovement();
            }

            tableMovePrefab.SetActive(true);
            FacilityBuildVisualUtility.ApplyBuiltState(tableMovePrefab);
            return true;

            void HandleTableMoveArrived()
            {
                if (AllTables.TryGetValue(tableId, out var table))
                {
                    table.MarkUnlocked();
                    PlayGuideBuildingSuccessEffect(ResolveGuideDeliveryEffectPosition(table.transform));
                }

                Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
            }
        }

        /// <summary>
        /// 标记或清除桌位的待升级状态。被标记的桌位在升级动画结束前不会再分配新顾客。
        /// </summary>
        /// <param name="tableId">桌位编号。</param>
        /// <param name="upgrading">true 表示进入待升级状态，false 表示清除。</param>
        public void MarkTableUpgrading(int tableId, bool upgrading)
        {
            if (upgrading)
            {
                pendingUpgradeTableIds.Add(tableId);
            }
            else
            {
                pendingUpgradeTableIds.Remove(tableId);
            }
        }

        /// <summary>
        /// 判断桌位是否处于待升级状态。
        /// </summary>
        /// <param name="tableId">桌位编号。</param>
        /// <returns>处于待升级流程时返回 true。</returns>
        public bool IsTableUpgrading(int tableId)
        {
            return pendingUpgradeTableIds.Contains(tableId);
        }

        /// <summary>
        /// 判断桌位当前是否仍被顾客或服务任务占用。升级流程在该方法返回 false 后才允许真正搬走桌子。
        /// </summary>
        /// <param name="tableId">桌位编号。</param>
        /// <returns>有顾客就坐或仍有未完成的服务/清扫派发时返回 true。</returns>
        public bool IsTableOccupied(int tableId)
        {
            if (TryGetTableCustomerGroup(tableId, out var customers) && customers.Count > 0)
            {
                return true;
            }

            if (IsTableBlockedByWaiterNap(tableId))
            {
                return true;
            }

            if (assignedServeTableIds.Contains(tableId)
                || assignedOrderTableIds.Contains(tableId)
                || assignedCleanTableIds.Contains(tableId))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断桌位是否仍存在会阻塞升级开始的占用。
        /// 这里仅关注“当前顾客是否还没离开”以及“是否还有未完成的上菜任务”，
        /// 不再把清扫视为升级阻塞条件，保证顾客离场后可以直接搬桌。
        /// </summary>
        /// <param name="tableId">桌位编号。</param>
        /// <returns>存在升级阻塞占用时返回 true，否则返回 false。</returns>
        public bool HasUpgradeBlockingOccupancy(int tableId)
        {
            if (TryGetTableCustomerGroup(tableId, out var customers) && customers.Count > 0)
            {
                return true;
            }

            return IsTableBlockedByWaiterNap(tableId)
                   || assignedServeTableIds.Contains(tableId)
                   || assignedOrderTableIds.Contains(tableId);
        }

        /// <summary>
        /// 判断桌位当前是否被打盹的小二占用。
        /// </summary>
        public bool IsTableBlockedByWaiterNap(int tableId)
        {
            return IsTableBlockedByWaiterNapInternal(tableId);
        }

        /// <summary>
        /// 桌位进入待升级流程前，取消自动清理和清扫派发，
        /// 避免顾客离桌后又先跑去清理，导致升级继续排队。
        /// </summary>
        /// <param name="tableId">桌位编号。</param>
        public void PreparePendingTableUpgrade(int tableId)
        {
            StopAutoClean(tableId);
            CancelWaiterCleanTask(tableId);
        }

        /// <summary>
        /// 处理桌位交互操作。
        /// </summary>
        /// <param name="桌位编号">数据编号。</param>
        public void HandleTableInteraction(int tableId)
        {
            var tableData = DataManager.Instance.GetTableData(tableId);
            if (tableData == null || !AllTables.TryGetValue(tableId, out var table))
            {
                return;
            }

            var state = (TavernTableRuntimeState)tableData.runtimeState;
            switch (state)
            {
                case TavernTableRuntimeState.WaitingOrder:
                    // 前台点单流程：桌边不再响应点单交互，由小二自动去前台点单。
                    break;
                case TavernTableRuntimeState.WaitingServe:
                    if (DataManager.Instance.TavernData.availableDishes <= 0)
                    {
                        table.RefreshRuntimeState(TavernTableRuntimeState.WaitingServe, "待上菜");
                        return;
                    }

                    if (!TryStartWaiterServeTask(tableId, playerDirected: true))
                    {
                        table.RefreshRuntimeState(TavernTableRuntimeState.WaitingServe, "待上菜");
                        HudOverlayService.ShowFloatingWarning("暂无空闲小二可上菜");
                    }

                    break;
                case TavernTableRuntimeState.Checkout:
                    // 玩家点结账：只瞬时收账，不派小二、不改小二位置/状态。
                    if (!TryInstantCompleteTableCheckout(tableId))
                    {
                        table.RefreshRuntimeState(TavernTableRuntimeState.Checkout, "待结账");
                    }

                    break;
            }
        }

        /// <summary>
        /// 点单气泡：仍有小二不会点单时显示可点击气泡；贵客桌始终需玩家点单。
        /// </summary>
        public bool RequiresPlayerClickForOrder()
        {
            return !CanAutoDispatchWaiterOrder();
        }

        /// <summary>
        /// 指定桌位是否需玩家亲自点单（贵客猜菜）。前台点单模式下关闭猜菜，贵客与普通客一样走自动点单。
        /// </summary>
        public bool TableRequiresVipOrderInteraction(int tableId)
        {
            return false;
        }

        /// <summary>
        /// 是否显示桌边点单按钮。前台点单模式下一律关闭。
        /// </summary>
        public bool ShouldShowTableSideOrderButton(int tableId)
        {
            return false;
        }

        /// <summary>
        /// 桌位当前是否有贵客。
        /// </summary>
        public bool TableHasVipCustomer(int tableId)
        {
            if (tableId <= 0)
            {
                return false;
            }

            if (TryGetTableCustomerGroup(tableId, out var customers) && customers != null)
            {
                for (var index = 0; index < customers.Count; index++)
                {
                    if (customers[index] != null && customers[index].IsVip)
                    {
                        return true;
                    }
                }

                return false;
            }

            return tableCustomers.TryGetValue(tableId, out var customer)
                   && customer != null
                   && customer.IsVip;
        }

        /// <summary>
        /// 当前解锁桌位中有 seated 顾客的比例（0–100），供管理天赋 1304 等使用。
        /// </summary>
        public float GetTableOccupancyPercent()
        {
            var unlocked = DataManager.Instance != null ? DataManager.Instance.GetUnlockedTableCount() : 0;
            if (unlocked <= 0)
            {
                return 0f;
            }

            var occupied = 0;
            for (var tableId = 1; tableId <= unlocked; tableId++)
            {
                if (TableHasSeatedCustomer(tableId))
                {
                    occupied++;
                }
            }

            return occupied * 100f / unlocked;
        }

        private bool TableHasSeatedCustomer(int tableId)
        {
            if (TryGetTableCustomerGroup(tableId, out var customers) && customers != null)
            {
                for (var index = 0; index < customers.Count; index++)
                {
                    if (customers[index] != null && customers[index].IsSeated)
                    {
                        return true;
                    }
                }

                return false;
            }

            return tableCustomers.TryGetValue(tableId, out var customer)
                   && customer != null
                   && customer.IsSeated;
        }

        private float GetEffectiveVipSpawnChance(float sourceMultiplier = 1f)
        {
            if (!CanSpawnShopVipCustomerNow())
            {
                return 0f;
            }

            // 按当前场景酒楼等级取表：自家用星级，拜访用对方建筑等级。
            var spawnLevel = DataManager.Instance != null
                ? DataManager.Instance.GetSceneTavernLevelForSpawn()
                : 1;
            var baseChance = TbConfigRuntime.GetVipSpawnChanceForLevel(spawnLevel, DefaultVipSpawnChance);
            vipSpawnChance = baseChance;

            // 拜访他人店不吃自家员工贵客加成，保证按对方等级纯表驱动。
            var chance = DataManager.Instance != null && DataManager.Instance.IsVisitingOtherTavern
                ? baseChance
                : StaffTalentConfigUtility.ApplyVipSpawnChanceBonus(baseChance);
            return Mathf.Clamp01(chance * Mathf.Max(0f, sourceMultiplier));
        }

        /// <summary>
        /// 稀客刷出概率：按酒楼等级读 rareSpawnChancePermille（拜访用对方等级）。
        /// </summary>
        private float GetEffectiveRareSpawnChance()
        {
            if (CountShopRareCustomers() >= MaxConcurrentShopRareCustomers)
            {
                return 0f;
            }

            var spawnLevel = DataManager.Instance != null
                ? DataManager.Instance.GetSceneTavernLevelForSpawn()
                : 1;
            var baseChance = TbConfigRuntime.GetRareSpawnChanceForLevel(spawnLevel, DefaultRareSpawnChance);
            rareSpawnChance = baseChance;
            return Mathf.Clamp01(baseChance);
        }

        /// <summary>
        /// 一楼是否还能刷贵客：店内（含排队）或二楼包厢已有贵客时暂停；离店或上楼途中（Leaving）后一楼名额释放，但二楼仍占时继续暂停。
        /// </summary>
        private bool CanSpawnShopVipCustomerNow()
        {
            if (CountShopVipCustomers() >= MaxConcurrentShopVipCustomers)
            {
                return false;
            }

            return !HasBlockingSecondFloorVipGuest();
        }

        /// <summary>自家店二楼包厢是否仍占用贵客（含已上楼、会话进行中）。</summary>
        private static bool HasBlockingSecondFloorVipGuest()
        {
            var dataManager = DataManager.Instance;
            if (dataManager == null || dataManager.IsVisitingOtherTavern)
            {
                return false;
            }

            return TavernSecondFloorVipService.HasSecondFloorVipGuest();
        }

        /// <summary>
        /// 统计店内（含排队）尚未离店的贵客数量，达到上限后暂停继续刷贵客。
        /// </summary>
        private int CountShopVipCustomers()
        {
            var count = 0;
            for (var index = 0; index < activeCustomers.Count; index++)
            {
                var customer = activeCustomers[index];
                if (customer != null && customer.IsVip && !customer.IsLeavingTavern)
                {
                    count++;
                }
            }

            for (var index = 0; index < queuedCustomers.Count; index++)
            {
                if (queuedCustomers[index] != null && queuedCustomers[index].IsVip)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 统计店内（含排队）尚未离店的稀客数量。
        /// </summary>
        private int CountShopRareCustomers()
        {
            var count = 0;
            for (var index = 0; index < activeCustomers.Count; index++)
            {
                var customer = activeCustomers[index];
                if (customer != null && customer.IsRare && !customer.IsLeavingTavern)
                {
                    count++;
                }
            }

            for (var index = 0; index < queuedCustomers.Count; index++)
            {
                if (queuedCustomers[index] != null && queuedCustomers[index].IsRare)
                {
                    count++;
                }
            }

            return count;
        }

        private void ShowVipGuestOrderPanelThenDispatch(int tableId)
        {
            CancelVipOrderInteractionTimeout(tableId);
            HideWaitingOrderBubbleForTable(tableId);

            if (!TableHasVipCustomer(tableId))
            {
                TryStartWaiterOrderAfterVipPanel(tableId);
                return;
            }

            if (!VipGuestDishGuessService.RequiresPlayerOrderClick(tableId))
            {
                TryStartWaiterOrderAfterVipPanel(tableId);
                return;
            }

            if (!VipGuestDishGuessService.CanOpen(tableId))
            {
                EnterVipOrderReadyState(tableId);
                return;
            }

            VipGuestDishGuessService.NotifyOrderPanelOpened(tableId);
            HudOverlayService.ShowVipGuestDishGuessPanel(tableId, false, () => EnterVipOrderReadyState(tableId));
        }

        /// <summary>
        /// 贵客气泡超时或猜菜完成后：标记可落单；前台协程检测到后会完成点单。
        /// </summary>
        private void EnterVipOrderReadyState(int tableId)
        {
            if (tableId <= 0)
            {
                return;
            }

            CancelVipOrderInteractionTimeout(tableId);
            VipGuestDishGuessService.NotifyOrderPanelClosed(tableId);
            HideWaitingOrderBubbleForTable(tableId);
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();

            // 若前台协程未在跑（例如交互从外部触发），直接落单。
            if (!frontCounterOrderRoutines.ContainsKey(tableId)
                && IsTableInState(tableId, TavernTableRuntimeState.WaitingOrder))
            {
                CompleteFrontCounterOrder(tableId);
            }
        }

        /// <summary>
        /// 是否存在已完成猜菜交互、等待小二自动点单的贵客桌。
        /// </summary>
        private bool HasVipTableReadyToDispatchOrder()
        {
            foreach (var tablePair in AllTables)
            {
                if (!TableHasVipCustomer(tablePair.Key)
                    || !VipGuestDishGuessService.ShouldAutoDispatchOrder(tablePair.Key)
                    || assignedOrderTableIds.Contains(tablePair.Key))
                {
                    continue;
                }

                var tableData = DataManager.Instance?.GetTableData(tablePair.Key);
                if (tableData != null
                    && tableData.isUnlocked
                    && (TavernTableRuntimeState)tableData.runtimeState == TavernTableRuntimeState.WaitingOrder)
                {
                    return true;
                }
            }

            return false;
        }

        private void TryStartWaiterOrderAfterVipPanel(int tableId)
        {
            // 贵客交互结束后由前台直接落单，不再派小二点单。
            CompleteFrontCounterOrder(tableId);
        }

        private void BeginWaitingOrderBubbleFlow(int tableId)
        {
            if (tableId <= 0)
            {
                return;
            }

            CancelOrderBubbleAutoHide(tableId);
            CancelVipOrderInteractionTimeout(tableId);

            // 前台点单：不启动贵客猜菜，也不弹出桌边可点击点单气泡。
            if (frontCounterOrderBindings.ContainsKey(tableId) || !ShouldShowTableSideOrderButton(tableId))
            {
                HideWaitingOrderBubbleForTable(tableId);
                return;
            }

            if (TableHasVipCustomer(tableId))
            {
                VipGuestDishGuessService.BeginWaitingOrderInteraction(tableId);
                RefreshVipWaitingOrderBubble(tableId);
                vipOrderInteractionTimeoutRoutines[tableId] = StartCoroutine(VipOrderInteractionTimeoutRoutine(tableId));
                return;
            }

            if (RequiresPlayerClickForOrder())
            {
                return;
            }

            orderBubbleAutoHideRoutines[tableId] = StartCoroutine(OrderBubbleAutoHideRoutine(tableId));
        }

        private IEnumerator VipOrderInteractionTimeoutRoutine(int tableId)
        {
            yield return new WaitForSeconds(VipGuestDishGuessService.PlayerOrderBubbleDurationSeconds);
            vipOrderInteractionTimeoutRoutines.Remove(tableId);

            if (!VipGuestDishGuessService.RequiresPlayerOrderClick(tableId))
            {
                yield break;
            }

            EnterVipOrderReadyState(tableId);
        }

        private IEnumerator OrderBubbleAutoHideRoutine(int tableId)
        {
            yield return new WaitForSeconds(OrderBubbleDisplayDurationSeconds);
            orderBubbleAutoHideRoutines.Remove(tableId);

            var tableData = DataManager.Instance?.GetTableData(tableId);
            if (tableData == null || (TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.WaitingOrder)
            {
                yield break;
            }

            HideWaitingOrderBubbleForTable(tableId);
        }

        private void RefreshVipWaitingOrderBubble(int tableId)
        {
            if (tableId <= 0 || !AllTables.TryGetValue(tableId, out var table) || table == null)
            {
                return;
            }

            var tableData = DataManager.Instance?.GetTableData(tableId);
            if (tableData == null
                || (TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.WaitingOrder)
            {
                return;
            }

            table.linkedUI?.RestoreWaitingOrderDisplay();
            table.RefreshRuntimeState(TavernTableRuntimeState.WaitingOrder);
        }

        private void CancelVipOrderInteractionTimeout(int tableId)
        {
            if (!vipOrderInteractionTimeoutRoutines.TryGetValue(tableId, out var routine))
            {
                return;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            vipOrderInteractionTimeoutRoutines.Remove(tableId);
        }

        private void CancelOrderBubbleAutoHide(int tableId)
        {
            if (!orderBubbleAutoHideRoutines.TryGetValue(tableId, out var routine))
            {
                return;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            orderBubbleAutoHideRoutines.Remove(tableId);
        }

        private void HideWaitingOrderBubbleForTable(int tableId)
        {
            if (tableId <= 0 || !AllTables.TryGetValue(tableId, out var table) || table == null)
            {
                return;
            }

            table.linkedUI?.HideWaitingOrderDisplay();
        }

        private void ClearWaitingOrderBubbleFlow(int tableId)
        {
            CancelVipOrderInteractionTimeout(tableId);
            CancelOrderBubbleAutoHide(tableId);
            VipGuestDishGuessService.ClearOrderInteraction(tableId);
        }

        /// <summary>
        /// 上菜气泡：仍有小二不会上菜时显示可点击气泡；否则自动上菜。
        /// </summary>
        public bool RequiresPlayerClickForServe()
        {
            return !CanAutoDispatchWaiterServe();
        }

        /// <summary>
        /// 结账气泡：小二默认不会自动收账，需玩家点击派工；研究解锁收账后可自动派单。
        /// </summary>
        public bool RequiresPlayerClickForCheckout()
        {
            return !CanAutoDispatchWaiterCheckout();
        }

        /// <summary>
        /// 已废弃略小结账气泡；保留 API 兼容，始终返回 false。
        /// </summary>
        public bool ShouldShowCompactCheckoutBubble()
        {
            return false;
        }

        /// <summary>
        /// 桌位头顶 txt_RuntimeStatus：自动收账开启后改由角色动作/气泡表达，不再显示状态文案。
        /// </summary>
        public bool ShouldShowTableRuntimeStatusText()
        {
            return !CanAutoDispatchWaiterCheckout();
        }

        /// <summary>
        /// 是否满足自动点单条件：全体在职小二都会点单（默认开启，与上菜一致）。
        /// </summary>
        public bool CanAutoDispatchWaiterOrder()
        {
            return AllOwnedWaitersCan(profile => profile.CanOrder);
        }

        /// <summary>
        /// 是否可尝试自动派点单：至少一名在职小二会点单（派给有技能者）。
        /// </summary>
        public bool CanAttemptAutoDispatchWaiterOrder()
        {
            return AnyOwnedWaiterCan(profile => profile.CanOrder);
        }

        /// <summary>
        /// 是否满足自动上菜条件：全体在职小二都会上菜（默认开启，与点单一致）。
        /// </summary>
        public bool CanAutoDispatchWaiterServe()
        {
            return AllOwnedWaitersCan(profile => profile.CanServe);
        }

        /// <summary>
        /// 是否满足自动结账条件：小二默认不会收账，需科技解锁或全员 CanCheckout 后才自动派单。
        /// </summary>
        public bool CanAutoDispatchWaiterCheckout()
        {
            return AllOwnedWaitersCan(profile => profile.CanCheckout);
        }

        /// <summary>
        /// 兼容旧调用：点单是否仍需点击（与 <see cref="RequiresPlayerClickForOrder"/> 相同）。
        /// </summary>
        public bool RequiresPlayerClickForWaiterTasks()
        {
            return RequiresPlayerClickForOrder();
        }

        /// <summary>
        /// 是否全部在职小二都具备指定技能。无在职小二时视为未全部学会（继续显示气泡）。
        /// </summary>
        private static bool AllOwnedWaitersCan(System.Func<JN.Client.Config.StaffRuntimeProfile, bool> skillCheck)
        {
            if (skillCheck == null)
            {
                return false;
            }

            var owned = DataManager.Instance != null ? DataManager.Instance.GetOwnedStaffList() : null;
            if (owned == null || owned.Count == 0)
            {
                return false;
            }

            var waiterCount = 0;
            for (var index = 0; index < owned.Count; index++)
            {
                var save = owned[index];
                if (save == null || save.temporary || save.staffId <= 0)
                {
                    continue;
                }

                var config = JN.Client.Config.StaffConfigUtility.GetOrNull(save.staffId);
                if (config == null || config.Position != StaffPosition.Waiter)
                {
                    continue;
                }

                waiterCount++;
                var profile = JN.Client.Config.StaffConfigUtility.GetProfile(save.staffId, save);
                if (profile == null || !skillCheck(profile))
                {
                    return false;
                }
            }

            return waiterCount > 0;
        }

        /// <summary>
        /// 是否至少一名在职小二具备指定技能。
        /// </summary>
        private static bool AnyOwnedWaiterCan(System.Func<JN.Client.Config.StaffRuntimeProfile, bool> skillCheck)
        {
            if (skillCheck == null)
            {
                return false;
            }

            var owned = DataManager.Instance != null ? DataManager.Instance.GetOwnedStaffList() : null;
            if (owned == null || owned.Count == 0)
            {
                return false;
            }

            for (var index = 0; index < owned.Count; index++)
            {
                var save = owned[index];
                if (save == null || save.temporary || save.staffId <= 0)
                {
                    continue;
                }

                var config = JN.Client.Config.StaffConfigUtility.GetOrNull(save.staffId);
                if (config == null || config.Position != StaffPosition.Waiter)
                {
                    continue;
                }

                var profile = JN.Client.Config.StaffConfigUtility.GetProfile(save.staffId, save);
                if (profile != null && skillCheck(profile))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 获取排队顾客数量。
        /// </summary>
        /// <returns>返回计算后的数值。</returns>
        public int GetQueueCustomerCount()
        {
            return queuedCustomers.Count;
        }

        /// <summary>
        /// 获取下一位顾客刷新倒计时剩余秒数。
        /// </summary>
        public float GetNextCustomerSpawnRemaining()
        {
            return nextCustomerSpawnRemaining;
        }

        /// <summary>
        /// 获取顾客刷新间隔。
        /// </summary>
        public float GetCustomerSpawnInterval()
        {
            return GetEffectiveCustomerSpawnInterval();
        }

        public void SetBusinessAdjustment(float customerCoefficient, float priceCoefficient)
        {
            this.customerCoefficient = Mathf.Max(0.01f, customerCoefficient);
            this.priceCoefficient = Mathf.Max(0.01f, priceCoefficient);
            RefreshCustomerSpawnInterval();
        }

        public void ResetBusinessAdjustment()
        {
            customerCoefficient = 1f;
            priceCoefficient = 1f;
        }

        public void SetServiceSpeedCoefficient(float speedCoefficient)
        {
            serviceSpeedCoefficient = Mathf.Max(1f, speedCoefficient);
        }

        public void ResetServiceSpeedCoefficient()
        {
            serviceSpeedCoefficient = 1f;
        }

        /// <summary>
        /// 解锁桌位或研究影响刷客间隔的科技后，立刻按新公式收紧剩余倒计时。
        /// </summary>
        public void RefreshCustomerSpawnInterval()
        {
            if (DataManager.Instance?.TavernData == null
                || !DataManager.Instance.TavernData.isOpen
                || !customerSpawnLoopActive
                || nextCustomerSpawnRemaining < 0f)
            {
                return;
            }

            var newInterval = GetEffectiveCustomerSpawnInterval();
            if (nextCustomerSpawnRemaining > newInterval)
            {
                nextCustomerSpawnRemaining = newInterval;
            }
        }

        public float GetEffectiveCustomerSpawnInterval()
        {
            // 固定间隔 = (按酒楼等级的刷客间隔 - 科技减秒) / customerCoefficient × 大众菜单倍率
            var tavernLevel = DataManager.Instance != null ? DataManager.Instance.GetTavernLevel() : 0;
            var levelInterval = TbConfigRuntime.GetCustomerRefreshTimeForLevel(tavernLevel, customerSpawnInterval);
            var refreshBonus = DataManager.Instance != null
                ? DataManager.Instance.GetTechCustomerRefreshSecondsBonus()
                : 0f;
            var adjustedRefresh = Mathf.Max(0.1f, levelInterval - refreshBonus);
            var menuMul = DataManager.Instance != null
                ? DataManager.Instance.GetActiveTavernMenuCustomerRefreshMul()
                : 1f;
            return Mathf.Max(0.1f, adjustedRefresh / Mathf.Max(0.01f, customerCoefficient) * menuMul);
        }

        public int GetEffectiveMaxQueueSize()
        {
            var bonus = DataManager.Instance != null ? DataManager.Instance.GetTechQueueCapBonus() : 0;
            return Mathf.Max(1, maxQueueSize + bonus);
        }

        public int ApplyPriceCoefficientToIncome(int baseIncome, GameObject servingWaiter = null)
        {
            var tipPercent = DataManager.Instance != null ? DataManager.Instance.GetTechTipBonusPercent() : 0;
            var profile = ResolveStaffRuntimeProfile(servingWaiter);
            if (profile != null)
            {
                tipPercent += profile.TipBonusPercent;
            }

            var priceProfitPercent = DataManager.Instance != null
                ? DataManager.Instance.GetTechPriceProfitBonusPercent()
                : 0;
            var postTenTablePriceMul = DataManager.Instance != null
                ? DataManager.Instance.GetPostTenTableBusinessPriceMultiplier()
                : 1f;
            var effectivePriceCoeff = priceCoefficient * postTenTablePriceMul * (1f + priceProfitPercent / 100f);
            var tipMul = 1f + tipPercent / 100f;
            return Mathf.Max(0, Mathf.RoundToInt(baseIncome * Mathf.Max(0.01f, effectivePriceCoeff) * tipMul));
        }

        private float GetEffectiveWaiterOrderDuration(GameObject waiter = null)
        {
            // 点单时长读 orderTime 固定值，不再按科技/学会与否分档。
            var duration = waiterOrderDuration / Mathf.Max(1f, serviceSpeedCoefficient);
            var profile = ResolveStaffRuntimeProfile(waiter);
            if (profile != null)
            {
                duration *= profile.OrderTimeMul;
            }

            return Mathf.Max(0.1f, duration);
        }

        private float GetEffectiveWaiterCheckoutDuration(GameObject waiter = null)
        {
            var duration = waiterCheckoutDuration / Mathf.Max(1f, serviceSpeedCoefficient);
            var profile = ResolveStaffRuntimeProfile(waiter);
            if (profile != null)
            {
                duration *= profile.CheckoutTimeMul;
            }

            return Mathf.Max(0.1f, duration);
        }

        private float GetEffectiveWaiterServeDuration(GameObject waiter = null)
        {
            // 上菜时长读 waiterServeTime 固定值，不再按科技学会与否分档。
            var duration = waiterServeDuration / Mathf.Max(1f, serviceSpeedCoefficient);
            var profile = ResolveStaffRuntimeProfile(waiter);
            if (profile != null)
            {
                duration *= profile.ServeTimeMul;
            }

            return Mathf.Max(0.1f, duration);
        }

        private float GetEffectiveWaiterStealDuration()
        {
            return Mathf.Max(0.1f, waiterStealDuration / Mathf.Max(1f, serviceSpeedCoefficient));
        }

        private float GetEffectiveAutoCleanDuration(GameObject waiter = null)
        {
            var duration = autoCleanDuration / Mathf.Max(1f, serviceSpeedCoefficient);
            var profile = ResolveStaffRuntimeProfile(waiter);
            if (profile != null)
            {
                duration *= profile.CleanTimeMul;
            }

            return Mathf.Max(0.1f, duration);
        }

        private float GetEffectiveDishCookDuration()
        {
            // 只读 TbConfig.chefCookTime（ApplyTimingConfig 写入 dishCookInterval）。
            // 营业加速系数只作用于小二点单/收账/上菜/清扫，不缩短做菜。
            return Mathf.Max(0.1f, dishCookInterval);
        }

        private float GetEffectiveWaiterMoveSpeed(GameObject waiter = null)
        {
            // 场景移动基准用 WaiterMoveSpeed；Staff 画像只提供相对倍率。
            // 注意：serviceSpeedCoefficient 只应缩短点单/收账/清扫等耗时，不能当移速基底，
            // 否则开智升级（MoveSpeedMul 升高）再叠加加速/叫醒 buff 会移速爆炸。
            var baseSpeed = WaiterMoveSpeed;
            var profile = ResolveStaffRuntimeProfile(waiter);
            if (profile == null)
            {
                return baseSpeed;
            }

            return Mathf.Max(0.1f, baseSpeed * profile.MoveSpeedMultiplier);
        }

        private static JN.Client.Config.StaffRuntimeProfile ResolveStaffRuntimeProfile(GameObject visual)
        {
            if (visual == null)
            {
                return null;
            }

            var waiter = visual.GetComponent<WaiterCharacter>();
            if (waiter != null && waiter.StaffId > 0)
            {
                return waiter.GetRuntimeProfile();
            }

            var chef = visual.GetComponent<ChefCharacter>();
            return chef != null && chef.StaffId > 0 ? chef.GetRuntimeProfile() : null;
        }

        /// <summary>
        /// 设置场景中顾客进店倒计时标签显隐。
        /// </summary>
        public void SetWorldCustomerEnterProgressVisible(bool visible)
        {
            if (nextCustomerTimerLabel?.rectTransform != null)
            {
                nextCustomerTimerLabel.rectTransform.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 获取当前顾客数量。
        /// </summary>
        /// <returns>返回计算后的数值。</returns>
        public int GetActiveCustomerCount()
        {
            return activeCustomers.Count;
        }

        /// <summary>
        /// 处理顾客入座通知。
        /// </summary>
        /// <param name="customer">顾客对象。</param>
        public void NotifyCustomerSeated(TavernCustomerRuntimeController customer)
        {
            if (customer == null || !AllTables.TryGetValue(customer.TableId, out var table))
            {
                return;
            }

            // 有新客人入座：收起该桌「被拉客」提示。
            ClearPulledTipOnTable(customer.TableId);

            // 贵客在一楼坐下后隐藏包厢按钮（含置灰不可点态）。
            if (customer.IsVip)
            {
                ClearVipGuestActionBubble(customer);
            }

            TrackCustomerState(customer, CustomerStateKeys.Seated);
            waitSatisfactionTracker.OnCustomerSeated(customer.GetInstanceID(), customer.TableId);
            TavernMenuGuestReactionService.TryShowSeatedReactionTip(customer);

            if (TryGetTableCustomerGroup(customer.TableId, out var customers))
            {
                for (var index = 0; index < customers.Count; index++)
                {
                    if (customers[index] == null || !customers[index].IsSeated)
                    {
                        return;
                    }
                }
            }

            var tableId = customer.TableId;
            // 贵客菜单普通客：点单入座齐人后弹气泡再离店（点单过程可见）。
            if (TryRejectVipMenuRegularGuestsAfterSeated(tableId))
            {
                return;
            }

            var tableData = DataManager.Instance != null
                ? DataManager.Instance.GetTableData(tableId)
                : null;
            var runtimeState = tableData != null
                ? (TavernTableRuntimeState)tableData.runtimeState
                : TavernTableRuntimeState.Idle;

            // 前台已点完：入座后直接等菜，禁止再切回 WaitingOrder / 桌边点单气泡。
            if (runtimeState == TavernTableRuntimeState.WaitingServe)
            {
                return;
            }

            // 兜底：若仍有未走前台点单的入座路径，保持旧待点单逻辑。
            tableStateService.SetWaitingOrder(tableId, table);
            waitSatisfactionTracker.OnWaitingOrder(tableId);
            TrackCustomerState(customer, CustomerStateKeys.WaitingOrder);
            BeginWaitingOrderBubbleFlow(tableId);
        }

        /// <summary>
        /// 处理顾客等待结账通知。
        /// </summary>
        /// <param name="customer">顾客对象。</param>
        public void NotifyCustomerReadyCheckout(TavernCustomerRuntimeController customer)
        {
            if (customer == null || !AllTables.TryGetValue(customer.TableId, out var table))
            {
                return;
            }

            TrackCustomerState(customer, CustomerStateKeys.ReadyCheckout);

            if (TryGetTableCustomerGroup(customer.TableId, out var customers))
            {
                for (var index = 0; index < customers.Count; index++)
                {
                    if (customers[index] == null || !customers[index].IsReadyCheckout)
                    {
                        return;
                    }
                }
            }

            tableStateService.SetCheckout(customer.TableId, table, showEmptyPlateVisual: true);
            ClearTableDiningTiming(customer.TableId);
            waitSatisfactionTracker.OnCheckout(customer.TableId);
        }

        /// <summary>
        /// 处理顾客离店通知。
        /// </summary>
        /// <param name="customer">顾客对象。</param>
        public void NotifyCustomerExited(TavernCustomerRuntimeController customer)
        {
            TrackCustomerState(customer, CustomerStateKeys.Leaving);
            HandleCustomerExitedAchievement(customer);
            RemoveCustomerFromFrontCounterBinding(customer);
            ClearVipGuestActionBubble(customer);

            if (customer != null && pendingSecondFloorVipCustomers.Remove(customer))
            {
                TavernSecondFloorVipService.SetSecondFloorVipGuest(true);
            }

            customerFlowService.HandleCustomerExited(
                customer,
                activeCustomers,
                queuedCustomers,
                tableCustomers,
                tableCustomerGroups);

            // 兜底：若仍有人在队，确保下标与站位对齐（正常离队已在 NotifyCustomerLeftQueue 刷过）。
            if (queuedCustomers.Count > 0)
            {
                UpdateQueuePositions();
            }

            if (customer != null)
            {
                ReleaseCustomerContext(customer);
                Destroy(customer.gameObject);
            }

            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        /// <summary>
        /// 顾客离队/离店时清理前台点单软预留；组空则释放桌位。
        /// </summary>
        private void RemoveCustomerFromFrontCounterBinding(TavernCustomerRuntimeController customer)
        {
            if (customer == null || customer.TableId <= 0)
            {
                return;
            }

            var tableId = customer.TableId;
            if (!frontCounterOrderBindings.TryGetValue(tableId, out var list) || list == null)
            {
                return;
            }

            list.Remove(customer);
            list.RemoveAll(item => item == null);
            if (list.Count > 0)
            {
                return;
            }

            frontCounterOrderBindings.Remove(tableId);
            var tableData = DataManager.Instance != null
                ? DataManager.Instance.GetTableData(tableId)
                : null;
            if (tableData == null
                || (TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.WaitingOrder
                || !AllTables.TryGetValue(tableId, out var table)
                || table == null)
            {
                return;
            }

            // 仅在仍待点单且组已空时释放，避免点完待上菜阶段误清桌。
            if (!assignedOrderTableIds.Contains(tableId))
            {
                waitSatisfactionTracker.ClearTable(tableId);
                tableStateService.SetIdle(tableId, table, dispatchRuntimeChanged: false);
            }
        }

        /// <summary>
        /// 获取指定桌位上的全部顾客。
        /// </summary>
        /// <param name="tableId">桌位编号。</param>
        /// <param name="customers">输出的顾客列表。</param>
        /// <returns>找到有效顾客组时返回 true，否则返回 false。</returns>
        private bool TryGetTableCustomerGroup(int tableId, out List<TavernCustomerRuntimeController> customers)
        {
            return customerFlowService.TryGetTableCustomerGroup(tableCustomerGroups, tableId, out customers);
        }

        /// <summary>
        /// 获取或创建厨师运行时上下文，并同步当前逻辑状态键。
        /// </summary>
        internal ChefCharacter TrackChefState(GameObject chefVisual, string stateKey, ChefTask currentTask = null)
        {
            var context = GetOrCreateChefContext(chefVisual);
            if (context == null)
            {
                return null;
            }

            context.CurrentTask = currentTask ?? InferChefTask(stateKey);
            context.SetPassiveState(CreateChefState(stateKey));
            return context;
        }

        /// <summary>
        /// 获取或创建顾客运行时上下文，并同步当前逻辑状态键。
        /// </summary>
        internal CustomerCharacter TrackCustomerState(TavernCustomerRuntimeController customer, string stateKey, CustomerAssignmentTask currentTask = null)
        {
            if (customer == null)
            {
                return null;
            }

            if (!customerRuntimeContexts.TryGetValue(customer, out var context) || context == null)
            {
                context = customer;
                context.InitializeOwner(this);
                customerRuntimeContexts[customer] = context;
            }

            context.CurrentTask = currentTask ?? InferCustomerTask(stateKey);
            context.SetPassiveState(CreateCustomerState(stateKey));
            return context;
        }

        /// <summary>
        /// 顾客真正离场后，清理对应的运行时上下文。
        /// </summary>
        internal void ReleaseCustomerContext(TavernCustomerRuntimeController customer)
        {
            if (customer != null)
            {
                customerRuntimeContexts.Remove(customer);
            }
        }

        /// <summary>
        /// 为厨师状态补齐默认任务语义，方便后续继续拆行为。
        /// </summary>
        private static ChefTask InferChefTask(string stateKey)
        {
            return stateKey switch
            {
                ChefStateKeys.Cooking => new CookDishTask(),
                _ => null
            };
        }

        private static ICharacterState<ChefCharacter> CreateChefState(string stateKey)
        {
            return stateKey switch
            {
                ChefStateKeys.Blocked => new ChefBlockedState(),
                ChefStateKeys.Cooking => new ChefCookingState(),
                ChefStateKeys.Napping => new ChefNappingState(),
                ChefStateKeys.ReturningHome => new ChefReturningHomeState(),
                _ => new ChefIdleState()
            };
        }

        /// <summary>
        /// 为顾客状态补齐默认任务语义，方便后续继续拆行为。
        /// </summary>
        private static CustomerAssignmentTask InferCustomerTask(string stateKey)
        {
            return stateKey switch
            {
                CustomerStateKeys.Queueing => new QueueCustomerAssignmentTask(),
                CustomerStateKeys.MovingToTable => new SeatCustomerAssignmentTask(),
                CustomerStateKeys.Leaving => new LeaveCustomerAssignmentTask(),
                _ => null
            };
        }

        private static ICharacterState<CustomerCharacter> CreateCustomerState(string stateKey)
        {
            return stateKey switch
            {
                CustomerStateKeys.Queueing => new CustomerQueueingState(),
                CustomerStateKeys.MovingToTable => new CustomerMovingToTableState(),
                CustomerStateKeys.Seated => new CustomerSeatedState(),
                CustomerStateKeys.WaitingOrder => new CustomerWaitingOrderState(),
                CustomerStateKeys.Dining => new CustomerDiningState(),
                CustomerStateKeys.ReadyCheckout => new CustomerReadyCheckoutState(),
                CustomerStateKeys.Leaving => new CustomerLeavingState(),
                _ => new CustomerSpawningState()
            };
        }

        /// <summary>
        /// 初始化桌位和界面绑定。
        /// </summary>
        private void InitTablesAndUIs()
        {
            AllTables.Clear();
            var tablesInScene = FindObjectsByType<TableArea>(FindObjectsSortMode.None);
            foreach (var table in tablesInScene)
            {
                var id = table.GetTableIdFromInternal();
                AllTables[id] = table;

                var uiScript = HudOverlayService.RegisterTableActionHud(table);
                if (uiScript == null)
                {
                    continue;
                }

                table.linkedUI = uiScript;
                uiScript.InitBinding(table.transform);
            }
        }

        /// <summary>
        /// 配置场景界面使用的画布。
        /// </summary>
        private void ConfigureSceneUiCanvas()
        {
            sceneCanvas = canvasParent != null ? canvasParent.GetComponentInParent<Canvas>() : null;
            if (sceneCanvas == null)
            {
                return;
            }

            sceneCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            sceneCanvas.worldCamera = null;

            var billboard = sceneCanvas.GetComponent<Billboard>();
            if (billboard != null)
            {
                billboard.enabled = false;
            }
        }

        /// <summary>
        /// 启动营业循环。
        /// </summary>
        private void StartBusinessLoop()
        {
            if (runtimeCoroutinesHaltedForLeave
                || isClosingBusiness
                || DataManager.Instance?.TavernData == null
                || !DataManager.Instance.TavernData.isOpen)
            {
                return;
            }

            if (!customerSpawnLoopActive)
            {
                // 开业瞬间不再刷高峰；本轮仅在 peakCustomerWaveSeconds 到达时触发一次。
                customerSpawnLoopActive = true;
            }

            if (chefServiceRoutine == null)
            {
                chefServiceRoutine = StartCoroutine(ChefServiceLoop());
            }

            if (waiterServiceRoutine == null)
            {
                waiterServiceRoutine = StartCoroutine(WaiterServiceLoop());
            }

            // 快照恢复可能已写入剩余刷客倒计时，勿覆盖。
            if (nextCustomerSpawnRemaining < 0f)
            {
                nextCustomerSpawnRemaining = GetEffectiveCustomerSpawnInterval();
            }

            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
        }

        /// <summary>
        /// 停止营业循环。
        /// </summary>
        private void StopBusinessLoop()
        {
            customerSpawnLoopActive = false;
            nextCustomerSpawnRemaining = -1f;
            ClearAllWaiterAttracting();
            PurgeStrayCustomersWhenClosed();

            if (chefServiceRoutine != null)
            {
                StopCoroutine(chefServiceRoutine);
                chefServiceRoutine = null;
            }

            if (postCloseCleanupActive)
            {
                ClearPreparedDishesForBusinessEnd();
                if (waiterServiceRoutine == null)
                {
                    waiterServiceRoutine = StartCoroutine(WaiterServiceLoop());
                }

                return;
            }

            if (waiterServiceRoutine != null)
            {
                StopCoroutine(waiterServiceRoutine);
                waiterServiceRoutine = null;
            }

            if (waiterTaskRoutine != null)
            {
                StopCoroutine(waiterTaskRoutine);
                waiterTaskRoutine = null;
            }

            // 清理小二任务派发缓存，下次开张能从干净状态重新开始
            ResetChefTaskState();
            ResetWaiterTaskState();
            pendingUpgradeTableIds.Clear();
            guidePendingTablePlacementIds.Clear();
            DataManager.Instance?.ClearGuideBuildPlacementPending();
            staffVisualsBeingAnimated.Clear();

            foreach (var routine in autoCleanRoutines.Values)
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
            }

            autoCleanRoutines.Clear();
            ClearPreparedDishesForBusinessEnd();
            ResetAllGuideStaffServiceAnimations();
        }

        /// <summary>
        /// 随机获取顾客点单文案。
        /// </summary>
        /// <returns>返回方法执行后的结果。</returns>
        private List<string> GetRandomOrderNames()
        {
            var result = new List<string>();
            if (UnityEngine.Random.value > 0.45f)
            {
                return result;
            }

            var products = SO_Product.GetAll();
            if (products == null || products.Count == 0)
            {
                result.Add("包子");
                return result;
            }

            var randomIndex = UnityEngine.Random.Range(0, products.Count);
            var product = products[randomIndex];
            if (product != null && !string.IsNullOrWhiteSpace(product.displayName))
            {
                result.Add(product.displayName);
            }

            if (result.Count == 0)
            {
                result.Add("包子");
            }

            return result;
        }

        /// <summary>
        /// 统计当前有顾客入座的桌数。
        /// </summary>
        /// <summary>
        /// 统计当前仍在店内的顾客（含排队、入座、离店途中），用于背景人声音量。
        /// </summary>
        private int GetInStoreCrowdCount()
        {
            var count = 0;
            for (var index = 0; index < activeCustomers.Count; index++)
            {
                var customer = activeCustomers[index];
                if (customer != null && !customer.IsLeavingTavern)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 供 GameAudioManager 与营业状态切换时刷新背景人声。
        /// </summary>
        public void RefreshBackgroundCrowdVolumeNow()
        {
            RefreshBackgroundCrowdVolume();
        }

        /// <summary>
        /// 按店内人数刷新 GameAudioManager 上的循环背景人声音量。
        /// 营业倒计时结束进入打烊后仍保留人声，直到最后一名顾客离店。
        /// </summary>
        private void RefreshBackgroundCrowdVolume()
        {
            var crowdCount = GetInStoreCrowdCount();
            GameAudioManager.RefreshTavernBackgroundCrowdVolume(crowdCount);
        }
    }

}
