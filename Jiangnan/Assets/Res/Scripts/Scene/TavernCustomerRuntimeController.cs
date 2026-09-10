using System.Collections;
using System.Collections.Generic;
using JN.Client.UI;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace JN.Client.Scene
{
    /// <summary>
    /// 负责酒楼顾客运行时相关的运行时逻辑。
    /// </summary>
    public class TavernCustomerRuntimeController : CustomerCharacter
    {
        private const float NavMeshSampleDistance = 2f;
        private const float VipMinWalkToSeatDistance = 0.85f;
        private const float VipMinWalkToSeatSeconds = 0.35f;
        /// <summary>普通客人也必须从出生/排队点走出可见距离，防止无路径时被误判已到达而瞬移入座。</summary>
        private const float MinWalkToSeatDistance = 0.75f;
        private const float MinWalkToSeatSeconds = 0.45f;
        /// <summary>Agent 走完路径不等于到达座位；距 SeatSlot 超过此值时继续走向接近点。</summary>
        private const float SeatArrivalMaxPlanarDistance = 1.15f;
        /// <summary>寻路无法继续逼近座位时的最终入座时限，仅防软锁。</summary>
        private const float SeatApproachForceArrivalSeconds = 15f;
        /// <summary>排队寻路过久仍未站定时强制站定，避免队首堵死前台点单。</summary>
        private const float QueueArriveForceReadySeconds = 4f;
        /// <summary>距排队目标足够近时也可强制站定（不必等 NavMesh 完全停稳）。</summary>
        private const float QueueArriveNearForceDistance = 0.55f;
        private const float StuckVelocityThreshold = 0.01f;
        private const float RepathDelay = 0.75f;
        private const float LeavingForceExitSeconds = 12f;
        private const float GroundOffset = -0.1f;
        private const float SitBlendDelay = 0.2f;
        private const float StandBlendDelay = 0.2f;
        private const float SeatTowardTableOffset = 0.08f;
        private const float StuckSideStepDistance = 0.4f;
        private const float EatLoopRetriggerInterval = 1.1f;
        private const float OrderBubbleDisplayDuration = 3f;
        private const string OrderBubblePrefabResourcePath = "UI/Guides/CustomerOrderBubble";
        private static readonly Vector3 OrderBubbleWorldBaseOffset = new(0f, 0.8f, -0.05f);
        private const float OrderBubbleRightSideOffsetDistance = 0.15f;
        private const float OrderBubbleLeftSideOffsetDistance = 0.1f;
        private static readonly string[] DiningSpeechTexts =
        {
            "不错哦",
            "好吃好吃",
            "一般般",
            "大厨水平不错"
        };

        /// <summary>
        /// 定义顾客状态可用的枚举类型。
        /// </summary>
        private enum CustomerState
        {
            None,
            Queueing,
            MovingToTable,
            Dining,
            Leaving
        }

        private TavernSceneManager owner;
        private NavMeshAgent agent;
        private Animator animator;
        private Vector3 exitPosition;
        private Vector3 currentDestination;
        private Quaternion queueTargetRotation = Quaternion.identity;
        private CustomerState state;
        private int speedHash = -1;
        private bool hasSpeedParam;
        private float stuckTimer;
        private float leavingElapsed;
        private float queueArriveElapsed;
        private float offNavMeshRecoverTimer;
        private bool isWaitingAtQueueSlot;
        private Vector3 tableAssignOrigin;
        private float tableAssignElapsed;
        /// <summary>入座接近点（与 currentDestination 分离，避免 SkipMove 后 Resume 仍走向门口）。</summary>
        private Vector3 seatApproachDestination;
        private bool hasSeatApproachDestination;
        private bool hasSitDownTrigger;
        private bool hasStandUpTrigger;
        private bool hasStartEatTrigger;
        private bool hasStopEatTrigger;
        private bool hasIsSittingBool;
        private bool hasIsEatingBool;
        private SO_Product desiredProduct;
        private Coroutine hideOrderBubblesCoroutine;
        private readonly List<GameObject> activeOrderBubbles = new();
        /// <summary>未加速时的 NavMesh 移速基准。</summary>
        private float defaultMoveSpeed = 0.95f;

        public int TableId { get; private set; }
        public int SeatIndex { get; private set; }
        public bool IsSeated { get; private set; }
        public bool IsReadyCheckout { get; private set; }

        /// <summary>
        /// 快照恢复等到结账：标记已用餐完毕，可被结账流程接走。
        /// </summary>
        public void MarkReadyCheckoutForRestore()
        {
            IsReadyCheckout = true;
            owner?.TrackCustomerState(this, CustomerStateKeys.ReadyCheckout);
        }
        /// <summary>
        /// 是否为贵客（独立预制体池刷出，CustomerM5）。
        /// </summary>
        public bool IsVip { get; private set; }
        /// <summary>
        /// 是否为稀客（独立预制体池刷出，CustomerM6）。
        /// </summary>
        public bool IsRare { get; private set; }
        /// <summary>贵客是否已到达排队位并站定（分配空桌前置条件）。</summary>
        internal bool IsQueueSlotReady => isWaitingAtQueueSlot;

        /// <summary>
        /// 贵客头顶气泡未点选大堂/包厢前，不参与正常入座分配。
        /// </summary>
        public bool IsAwaitingVipFloorChoice { get; private set; }

        /// <summary>
        /// 进店入队时已占用「前台点单前 2 名额」；到排队位后再通知前台点单。
        /// </summary>
        public bool IsFrontCounterOrderCandidate { get; private set; }

        public void SetAwaitingVipFloorChoice(bool awaiting)
        {
            IsAwaitingVipFloorChoice = awaiting;
        }

        public void SetFrontCounterOrderCandidate(bool value)
        {
            IsFrontCounterOrderCandidate = value;
        }
        /// <summary>
        /// 中途离场原因；正常结账离店为 None。
        /// </summary>
        public CustomerWalkoutReason WalkoutReason { get; private set; }
        public bool IsLeavingTavern => state == CustomerState.Leaving;

        public int WaitHudGroupId { get; private set; }
        public SO_Product DesiredProduct => desiredProduct;

        public void SetWaitHudGroupId(int groupId)
        {
            WaitHudGroupId = groupId;
        }

        /// <summary>
        /// 打烊清场时是否算作「打断用餐」（已入座 / 就餐 / 待结账）。
        /// </summary>
        public bool CountsAsInterruptedDiningOnClose =>
            state == CustomerState.Dining
            || state == CustomerState.MovingToTable
            || IsSeated
            || IsReadyCheckout;

        /// <summary>
        /// 标记为贵客（由贵客独立池刷出后调用）。
        /// 贵客入座待点单时，玩家点击点单气泡会先弹出猜菜面板。
        /// </summary>
        public void MarkAsVip()
        {
            IsVip = true;
            IsRare = false;
        }

        /// <summary>
        /// 标记为稀客（由稀客独立池刷出后调用；外观 CustomerM6，无贵客猜菜/收入加成）。
        /// </summary>
        public void MarkAsRare()
        {
            IsRare = true;
            IsVip = false;
        }

        /// <summary>
        /// 临时提高移速（拜访拉客上轿）；销毁时随对象消失，无需手动还原。
        /// </summary>
        public void ApplyMoveSpeedMultiplier(float multiplier)
        {
            if (agent == null)
            {
                return;
            }

            if (defaultMoveSpeed <= 0.01f)
            {
                defaultMoveSpeed = Mathf.Max(0.1f, agent.speed);
            }

            agent.speed = defaultMoveSpeed * Mathf.Max(0.1f, multiplier);
        }

        /// <summary>
        /// 注入运行时依赖并刷新初始显示。
        /// </summary>
        /// <param name="tavernSceneManager">参数值。</param>
        /// <param name="startPosition">坐标。</param>
        /// <param name="targetExitPosition">目标对象。</param>
        public void Initialize(TavernSceneManager tavernSceneManager, Vector3 startPosition, Vector3 targetExitPosition)
        {
            owner = tavernSceneManager;
            InitializeOwner(tavernSceneManager);
            exitPosition = targetExitPosition;
            agent = GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                agent = GetComponentInChildren<NavMeshAgent>(true);
            }

            DisableChildNavMeshAgents();

            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            EnsureAnimationEventRelay();
            TableId = -1;
            SeatIndex = 0;
            IsSeated = false;
            IsReadyCheckout = false;
            IsVip = false;
            IsRare = false;
            IsAwaitingVipFloorChoice = false;
            desiredProduct = ResolveRandomDesiredProduct();
            currentDestination = startPosition;
            queueTargetRotation = transform.rotation;
            stuckTimer = 0f;
            isWaitingAtQueueSlot = false;
            IsFrontCounterOrderCandidate = false;
            tableAssignElapsed = 0f;
            tableAssignOrigin = startPosition;
            hasSeatApproachDestination = false;
            seatApproachDestination = startPosition;

            // 动画参数是否存在取决于具体模型控制器，因此初始化时先做一次能力探测。
            CacheAnimatorState();
            PrepareAgentForSpawn(startPosition);
            owner?.TrackCustomerState(this, CustomerStateKeys.Spawning);
        }

        /// <summary>
        /// 移动前往排队目标位姿。
        /// </summary>
        /// <param name="queueTarget">目标位姿。</param>
        internal void MoveToQueue(TavernQueueTarget queueTarget)
        {
            // 入座/离队途中不应再被刷回排队点。
            // 注意：刚刷出时 state 仍为 None，必须放行，否则会卡在门口不动。
            if (state == CustomerState.Leaving
                || state == CustomerState.MovingToTable
                || state == CustomerState.Dining
                || IsSeated)
            {
                return;
            }

            queueTargetRotation = queueTarget.Rotation;
            if (isWaitingAtQueueSlot && IsSameQueueDestination(queueTarget.Position))
            {
                // NavMesh 吸附可能导致不同排队锚点目标重合：目标未变但人仍在后方时强制补走。
                if (!IsAlreadyAtQueueDestination(queueTarget.Position))
                {
                    isWaitingAtQueueSlot = false;
                    queueArriveElapsed = 0f;
                    state = CustomerState.Queueing;
                    owner?.TrackCustomerState(this, CustomerStateKeys.Queueing);
                    EnsureMovementReady();
                    ForceMoveTo(queueTarget.Position);
                    if (state == CustomerState.Queueing && IsAlreadyAtQueueDestination(queueTarget.Position))
                    {
                        OnReachQueue();
                    }

                    return;
                }

                ApplyQueueRotation();
                // 位置未变但可能刚被补标前台名额：补发站定通知，避免空桌却无人点单。
                if (IsFrontCounterOrderCandidate)
                {
                    owner?.NotifyCustomerReachedQueueSlotForFrontOrder(this);
                }

                return;
            }

            isWaitingAtQueueSlot = false;
            queueArriveElapsed = 0f;
            state = CustomerState.Queueing;
            owner?.TrackCustomerState(this, CustomerStateKeys.Queueing);
            EnsureMovementReady();
            // 补位前进用 ForceMoveTo，避免与旧目标过近时被 ShouldSkipMoveTo 吞掉。
            ForceMoveTo(queueTarget.Position);

            // ForceMoveTo 后若已在目标点，不会再走寻路完成回调，这里直接视为站定。
            if (state == CustomerState.Queueing && IsAlreadyAtQueueDestination(queueTarget.Position))
            {
                OnReachQueue();
            }
        }

        /// <summary>
        /// 清除前台点单软预留绑定（桌号/座位），不改变排队状态。
        /// </summary>
        internal void ClearFrontCounterOrderBind()
        {
            TableId = -1;
            SeatIndex = 0;
        }

        /// <summary>
        /// 把顾客绑定到桌位但不走向餐桌（前台点单软预留）。
        /// </summary>
        internal void BindTableForFrontCounterOrder(int tableId, int seatIndex)
        {
            TableId = tableId;
            SeatIndex = Mathf.Max(0, seatIndex);
            IsSeated = false;
            IsReadyCheckout = false;
        }

        /// <summary>
        /// 拜访开场：直接坐到指定座位（不走路、不等动画延迟）。
        /// </summary>
        public void InstantSeatAtTable(int tableId, int seatIndex)
        {
            TableId = tableId;
            SeatIndex = Mathf.Max(0, seatIndex);
            IsReadyCheckout = false;
            isWaitingAtQueueSlot = false;
            hasSeatApproachDestination = false;
            if (IsFrontCounterOrderCandidate)
            {
                SetFrontCounterOrderCandidate(false);
            }

            if (agent != null)
            {
                if (agent.isActiveAndEnabled && agent.isOnNavMesh)
                {
                    agent.ResetPath();
                    agent.isStopped = true;
                }

                agent.enabled = false;
            }

            state = CustomerState.None;
            SnapToSeatPose();
            // 开场已就座：直接切入 Sitting 终帧，跳过 SitDown /「坐」过渡动画。
            // 贵客控制器的 Sitting 绑定的是非循环「坐.anim」，从 0 播会整段坐下；须从末帧切入。
            PlaySittingEndPose();

            IsSeated = true;
            owner?.TrackCustomerState(this, CustomerStateKeys.Seated);
        }

        /// <summary>
        /// 二楼包厢等无桌位场景：直接落在世界坐标并切入坐下终帧。
        /// </summary>
        public void InstantSitAtWorldPose(Vector3 worldPosition, Quaternion worldRotation)
        {
            TableId = -1;
            SeatIndex = 0;
            IsReadyCheckout = false;
            isWaitingAtQueueSlot = false;
            hasSeatApproachDestination = false;
            if (IsFrontCounterOrderCandidate)
            {
                SetFrontCounterOrderCandidate(false);
            }

            agent = GetComponent<NavMeshAgent>() ?? GetComponentInChildren<NavMeshAgent>(true);
            if (agent != null)
            {
                if (agent.isActiveAndEnabled && agent.isOnNavMesh)
                {
                    agent.ResetPath();
                    agent.isStopped = true;
                }

                agent.enabled = false;
            }

            // 关掉子节点嵌套 Agent，避免贵客 FBX 二次驱动。
            DisableChildNavMeshAgents();

            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
            CacheAnimatorState();

            state = CustomerState.None;
            transform.SetPositionAndRotation(worldPosition, worldRotation);
            PlaySittingEndPose();
            IsSeated = true;
        }

        private void PlaySittingEndPose()
        {
            if (animator == null)
            {
                return;
            }

            if (hasSitDownTrigger)
            {
                animator.ResetTrigger("SitDown");
            }

            if (hasIsSittingBool)
            {
                animator.SetBool("IsSitting", true);
            }

            // 直接切到 Sitting 末帧；贵客「坐」片段非循环，从 0 播会整段坐下。
            animator.Play("Sitting", 0, 1f);
            animator.Update(0f);
        }

        /// <summary>
        /// 处理分配到桌位相关逻辑。
        /// </summary>
        /// <param name="桌位编号">数据编号。</param>
        /// <param name="tablePosition">坐标。</param>
        public void AssignToTable(int tableId, Vector3 tablePosition, int seatIndex = 0)
        {
            TableId = tableId;
            SeatIndex = Mathf.Max(0, seatIndex);
            IsSeated = false;
            IsReadyCheckout = false;
            isWaitingAtQueueSlot = false;
            state = CustomerState.MovingToTable;
            tableAssignOrigin = transform.position;
            tableAssignElapsed = 0f;
            seatApproachDestination = tablePosition;
            hasSeatApproachDestination = true;
            if (IsFrontCounterOrderCandidate)
            {
                SetFrontCounterOrderCandidate(false);
            }

            owner?.TrackCustomerState(this, CustomerStateKeys.MovingToTable);
            EnsureMovementReady();
            // 入座必须重新下发目标，避免与门口/排队点过近时被 ShouldSkipMoveTo 跳过。
            ForceMoveTo(tablePosition);
        }

        /// <summary>
        /// 处理开始用餐相关逻辑。
        /// </summary>
        /// <param name="duration">持续时间。</param>
        public void BeginDining(float duration)
        {
            StopAllCoroutines();
            ClearOrderBubbles();
            IsReadyCheckout = false;
            owner?.TrackCustomerState(this, CustomerStateKeys.Dining);
            StartCoroutine(DiningRoutine(duration));
        }

        public void SetExitPosition(Vector3 worldPosition)
        {
            exitPosition = worldPosition;
        }

        /// <summary>
        /// 处理顾客离开酒楼流程。
        /// </summary>
        public void LeaveTavern(CustomerWalkoutReason walkoutReason = CustomerWalkoutReason.None)
        {
            WalkoutReason = walkoutReason;
            StopAllCoroutines();
            ClearOrderBubbles();
            owner?.ReleaseCustomerWaitHudForCustomer(this);
            // 等待超时走客：反馈气泡常驻到模型消失（不跟随耐心条一起清掉）。
            if (IsWaitTimeoutWalkoutReason(walkoutReason))
            {
                HudOverlayService.ShowWaitTimeoutReviewTip(transform);
            }

            // 离队当帧出列并让后排补位，避免走向出口途中仍占排队下标 0/1。
            owner?.NotifyCustomerLeftQueue(this);
            var previousState = state;
            leavingElapsed = 0f;
            state = CustomerState.Leaving;
            owner?.TrackCustomerState(this, CustomerStateKeys.Leaving);
            var shouldStandUpFirst = previousState == CustomerState.Dining;
            if (!shouldStandUpFirst && animator != null && hasIsSittingBool)
            {
                shouldStandUpFirst = animator.GetBool("IsSitting");
            }

            // 顾客如果还处于坐下状态，先站起再走，避免直接平移离桌。
            if (shouldStandUpFirst)
            {
                StartCoroutine(LeaveAfterStandUpRoutine());
                return;
            }

            MoveTo(exitPosition);
        }

        private static bool IsWaitTimeoutWalkoutReason(CustomerWalkoutReason reason)
        {
            return reason is CustomerWalkoutReason.QueueTooLong
                or CustomerWalkoutReason.OrderTooLong
                or CustomerWalkoutReason.ServeTooSlow
                or CustomerWalkoutReason.CheckoutTooLong;
        }

        /// <summary>
        /// 打烊兜底：NavMesh 失效或长时间未到达出口时强制离店，避免 close routine 永久等待。
        /// </summary>
        internal void ForceExitTavern()
        {
            if (state == CustomerState.None)
            {
                return;
            }

            StopAllCoroutines();
            ClearOrderBubbles();
            owner?.ReleaseCustomerWaitHudForCustomer(this);
            owner?.NotifyCustomerLeftQueue(this);
            state = CustomerState.None;
            owner?.NotifyCustomerExited(this);
        }

        /// <summary>
        /// 离店写快照 / 切场景前中断用餐、离店等协程，避免 Coroutine continue failure。
        /// 不改顾客逻辑状态，供场景侧采样快照。
        /// </summary>
        public void HaltRuntimeCoroutinesForSceneLeave()
        {
            StopAllCoroutines();
            hideOrderBubblesCoroutine = null;
        }

        /// <summary>
        /// 逐帧处理输入、状态推进或动画刷新。
        /// </summary>
        private void Update()
        {
            // 二楼包厢等已就座展示：禁止掉网格恢复把 Agent 重新打开，打断坐下动画。
            if (IsSeated
                && state != CustomerState.Dining
                && state != CustomerState.Leaving
                && state != CustomerState.MovingToTable)
            {
                return;
            }

            if (state == CustomerState.Leaving)
            {
                TickLeavingState();
                return;
            }

            // 掉网格时不能整帧空转：持续 Warp/重寻路，否则队首永远不站定、整队不点单。
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
            {
                TickOffNavMeshRecovery();
                return;
            }

            offNavMeshRecoverTimer = 0f;
            UpdateAnimator();
            RecoverIfStuck();
            if (state == CustomerState.MovingToTable)
            {
                tableAssignElapsed += Time.deltaTime;
            }
            else if (state == CustomerState.Queueing && !isWaitingAtQueueSlot)
            {
                queueArriveElapsed += Time.deltaTime;
                if (TryForceQueueSlotReadyIfStalled())
                {
                    return;
                }
            }

            if (agent.pathPending)
            {
                // pathPending 挂起过久时也做站定兜底，避免永久等不到 OnReachQueue。
                if (state == CustomerState.Queueing && !isWaitingAtQueueSlot)
                {
                    TryForceQueueSlotReadyIfStalled();
                }

                return;
            }

            // 无路径时 remainingDistance 常为 0，不能当成“已走到”。
            if (!agent.hasPath)
            {
                if (state == CustomerState.MovingToTable)
                {
                    ResumeSeatApproachMovement();
                }
                else if (state == CustomerState.Queueing)
                {
                    if (!isWaitingAtQueueSlot && TryForceQueueSlotReadyIfStalled())
                    {
                        return;
                    }

                    ForceMoveTo(currentDestination);
                }

                return;
            }

            if (agent.remainingDistance > agent.stoppingDistance + 0.05f)
            {
                if (state == CustomerState.Queueing && !isWaitingAtQueueSlot)
                {
                    TryForceQueueSlotReadyIfStalled();
                }

                return;
            }

            switch (state)
            {
                case CustomerState.Queueing:
                    OnReachQueue();
                    break;
                case CustomerState.MovingToTable:
                    if (ShouldDeferSeatArrival())
                    {
                        ResumeSeatApproachMovement();
                        return;
                    }

                    if (!CanCompleteSeatApproach())
                    {
                        ResumeSeatApproachMovement();
                        return;
                    }

                    OnReachTable();
                    break;
            }
        }

        /// <summary>
        /// Agent 掉出 NavMesh 时周期性 Warp 回网格并重发寻路。
        /// </summary>
        private void TickOffNavMeshRecovery()
        {
            if (state != CustomerState.Queueing && state != CustomerState.MovingToTable)
            {
                return;
            }

            offNavMeshRecoverTimer += Time.deltaTime;
            if (offNavMeshRecoverTimer < 0.35f)
            {
                return;
            }

            offNavMeshRecoverTimer = 0f;
            EnsureMovementReady();
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
            {
                return;
            }

            if (state == CustomerState.MovingToTable)
            {
                ResumeSeatApproachMovement();
            }
            else if (state == CustomerState.Queueing && !isWaitingAtQueueSlot)
            {
                queueArriveElapsed += 0.35f;
                ForceMoveTo(currentDestination);
                TryForceQueueSlotReadyIfStalled();
            }
        }

        /// <summary>
        /// 排队超时或已贴近目标时强制站定，解锁前台点单事件链。
        /// </summary>
        private bool TryForceQueueSlotReadyIfStalled()
        {
            if (state != CustomerState.Queueing || isWaitingAtQueueSlot)
            {
                return false;
            }

            var nearTarget = false;
            if (currentDestination != Vector3.zero)
            {
                var delta = transform.position - currentDestination;
                delta.y = 0f;
                nearTarget = delta.sqrMagnitude <= QueueArriveNearForceDistance * QueueArriveNearForceDistance;
            }

            if (!nearTarget && queueArriveElapsed < QueueArriveForceReadySeconds)
            {
                return false;
            }

            OnReachQueue();
            return true;
        }

        /// <summary>
        /// 场景侧兜底：仅在超时或已贴近目标时强制站定（与内部兜底同一条件）。
        /// </summary>
        internal bool TryForceMarkQueueSlotReadyIfStalled()
        {
            return TryForceQueueSlotReadyIfStalled();
        }

        private void TickLeavingState()
        {
            leavingElapsed += Time.deltaTime;
            var agentReady = agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;
            if (!agentReady)
            {
                EnsureMovementReady();
                agentReady = agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;
                if (agentReady)
                {
                    MoveTo(exitPosition);
                }
            }

            if (agentReady)
            {
                UpdateAnimator();
                RecoverIfStuck();
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.05f)
                {
                    state = CustomerState.None;
                    owner?.NotifyCustomerExited(this);
                    return;
                }
            }

            if (leavingElapsed >= LeavingForceExitSeconds)
            {
                ForceExitTavern();
            }
        }

        /// <summary>
        /// 处理用餐协程相关逻辑。
        /// </summary>
        /// <param name="duration">持续时间。</param>
        /// <returns>协程迭代器。</returns>
        private IEnumerator DiningRoutine(float duration)
        {
            state = CustomerState.Dining;
            StartEatingAnimation();
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
            }

            var elapsed = 0f;
            var nextRetriggerTime = EatLoopRetriggerInterval;
            while (elapsed < duration)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (hasStartEatTrigger && elapsed >= nextRetriggerTime)
                {
                    animator?.SetTrigger("StartEat");
                    nextRetriggerTime += EatLoopRetriggerInterval;
                }
            }

            StopEatingAnimation();
            ShowDiningSpeech();
            IsReadyCheckout = true;
            owner?.TrackCustomerState(this, CustomerStateKeys.ReadyCheckout);
            owner.NotifyCustomerReadyCheckout(this);
        }

        /// <summary>
        /// 缓存动画器状态。
        /// </summary>
        private void CacheAnimatorState()
        {
            if (animator == null)
            {
                return;
            }

            speedHash = Animator.StringToHash("Speed");
            foreach (var parameter in animator.parameters)
            {
                if (parameter.nameHash == speedHash && parameter.type == AnimatorControllerParameterType.Float)
                {
                    hasSpeedParam = true;
                }

                if (parameter.type == AnimatorControllerParameterType.Trigger)
                {
                    if (parameter.name == "SitDown") hasSitDownTrigger = true;
                    if (parameter.name == "StandUp") hasStandUpTrigger = true;
                    if (parameter.name == "StartEat") hasStartEatTrigger = true;
                    if (parameter.name == "StopEat") hasStopEatTrigger = true;
                }

                if (parameter.type == AnimatorControllerParameterType.Bool)
                {
                    if (parameter.name == "IsSitting") hasIsSittingBool = true;
                    if (parameter.name == "IsEating") hasIsEatingBool = true;
                }
            }
        }

        /// <summary>
        /// 确保动画事件转发器。
        /// </summary>
        private void EnsureAnimationEventRelay()
        {
            if (animator == null)
            {
                return;
            }

            // 动画事件打在 动画器 所在节点上，因此需要中继组件把回调转发到运行时控制器。
            var relay = animator.GetComponent<TavernCustomerAnimationEventRelay>();
            if (relay == null)
            {
                relay = animator.gameObject.AddComponent<TavernCustomerAnimationEventRelay>();
            }

            relay.Bind(this);
        }

        /// <summary>
        /// 处理准备代理用于生成相关逻辑。
        /// </summary>
        /// <param name="preferredPosition">坐标。</param>
        private void PrepareAgentForSpawn(Vector3 preferredPosition)
        {
            if (agent != null)
            {
                // 运行时统一兜底一些速度参数，避免导入自不同来源的顾客 预制体 手感不一致。
                agent.speed = 0.95f;
                defaultMoveSpeed = agent.speed;
                agent.acceleration = 3.2f;
                agent.angularSpeed = 360f;
                agent.baseOffset = GroundOffset;
                agent.radius = 0.18f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                agent.avoidancePriority = Random.Range(20, 80);
            }

            if (!TryResolveNavMeshPosition(preferredPosition, out var navMeshPosition))
            {
                transform.position = preferredPosition;
                return;
            }

            transform.position = navMeshPosition;
            TryEnableAgentOnNavMesh(navMeshPosition);
        }

        /// <summary>
        /// 移动To。
        /// </summary>
        /// <param name="worldPosition">坐标。</param>
        private void MoveTo(Vector3 worldPosition)
        {
            EnsureMovementReady();
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
            {
                return;
            }

            if (!TryResolveNavMeshPosition(worldPosition, out var navMeshPosition))
            {
                return;
            }

            if (ShouldSkipMoveTo(navMeshPosition))
            {
                return;
            }

            ApplyAgentDestination(navMeshPosition);
        }

        /// <summary>
        /// 强制设置寻路目标（入座用），忽略与当前目标过近的跳过逻辑。
        /// </summary>
        private void ForceMoveTo(Vector3 worldPosition)
        {
            EnsureMovementReady();
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
            {
                return;
            }

            if (!TryResolveNavMeshPosition(worldPosition, out var navMeshPosition))
            {
                return;
            }

            ApplyAgentDestination(navMeshPosition);
        }

        private void ApplyAgentDestination(Vector3 navMeshPosition)
        {
            currentDestination = navMeshPosition;
            stuckTimer = 0f;
            agent.isStopped = false;
            agent.ResetPath();
            agent.SetDestination(navMeshPosition);
        }

        private bool ShouldSkipMoveTo(Vector3 navMeshPosition)
        {
            var destinationDelta = navMeshPosition - currentDestination;
            destinationDelta.y = 0f;
            if (destinationDelta.sqrMagnitude > 0.0004f)
            {
                return false;
            }

            if (agent.hasPath && !agent.pathPending)
            {
                return true;
            }

            var positionDelta = transform.position - navMeshPosition;
            positionDelta.y = 0f;
            var stopDistance = agent.stoppingDistance + 0.05f;
            return positionDelta.sqrMagnitude <= stopDistance * stopDistance
                   && (agent.isStopped || agent.remainingDistance <= stopDistance);
        }

        private bool IsSameQueueDestination(Vector3 queuePosition)
        {
            if (!TryResolveNavMeshPosition(queuePosition, out var navMeshPosition))
            {
                return false;
            }

            var destinationDelta = navMeshPosition - currentDestination;
            destinationDelta.y = 0f;
            return destinationDelta.sqrMagnitude <= 0.0004f;
        }

        /// <summary>
        /// 角色是否已站在排队目标附近（用于 MoveTo 被跳过时的到达兜底）。
        /// </summary>
        private bool IsAlreadyAtQueueDestination(Vector3 queuePosition)
        {
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
            {
                return false;
            }

            if (!TryResolveNavMeshPosition(queuePosition, out var navMeshPosition))
            {
                return false;
            }

            var positionDelta = transform.position - navMeshPosition;
            positionDelta.y = 0f;
            var stopDistance = agent.stoppingDistance + 0.08f;
            return positionDelta.sqrMagnitude <= stopDistance * stopDistance;
        }

        /// <summary>
        /// 确保根节点 NavMeshAgent 已启用并落在 NavMesh 上，避免贵客等包装体瞬移到目标点。
        /// </summary>
        private void EnsureMovementReady()
        {
            DisableChildNavMeshAgents();
            if (agent == null)
            {
                agent = GetComponent<NavMeshAgent>() ?? GetComponentInChildren<NavMeshAgent>(true);
            }

            if (agent == null)
            {
                return;
            }

            if (!agent.enabled)
            {
                agent.enabled = true;
            }

            TryEnableAgentOnNavMesh(transform.position);
        }

        /// <summary>
        /// 仅保留根节点 NavMeshAgent，关闭子节点上的代理（贵客 FBX 常见嵌套代理导致瞬移）。
        /// </summary>
        private void DisableChildNavMeshAgents()
        {
            var rootAgent = GetComponent<NavMeshAgent>();
            var agents = GetComponentsInChildren<NavMeshAgent>(true);
            for (var index = 0; index < agents.Length; index++)
            {
                var navAgent = agents[index];
                if (navAgent == null || navAgent == rootAgent)
                {
                    continue;
                }

                navAgent.enabled = false;
            }
        }

        /// <summary>
        /// 更新动画器。
        /// </summary>
        private void UpdateAnimator()
        {
            if (!hasSpeedParam || animator == null || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
            {
                return;
            }

            animator.SetFloat(speedHash, agent.velocity.magnitude);
        }

        /// <summary>
        /// 处理卡住恢复相关逻辑。
        /// </summary>
        private void RecoverIfStuck()
        {
            if (!agent.hasPath || agent.pathPending || agent.remainingDistance <= agent.stoppingDistance + 0.05f)
            {
                stuckTimer = 0f;
                return;
            }

            if (agent.velocity.sqrMagnitude > StuckVelocityThreshold * StuckVelocityThreshold)
            {
                stuckTimer = 0f;
                return;
            }

            stuckTimer += Time.deltaTime;
            if (stuckTimer < RepathDelay)
            {
                return;
            }

            // 当顾客长时间几乎不移动时，重新寻路一次，缓解局部卡住的问题。
            stuckTimer = 0f;
            if (TryResolveSideStepPosition(out var sideStepPosition))
            {
                MoveTo(sideStepPosition);
                return;
            }

            MoveTo(currentDestination);
        }

        /// <summary>
        /// 为相向卡住的顾客尝试寻找一个侧移落点，先错身再继续去原目标。
        /// </summary>
        /// <param name="sideStepPosition">输出的侧移坐标。</param>
        /// <returns>找到可用侧移点时返回 true，否则返回 false。</returns>
        private bool TryResolveSideStepPosition(out Vector3 sideStepPosition)
        {
            sideStepPosition = Vector3.zero;
            var forward = currentDestination - transform.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            if (right.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            var candidates = new[]
            {
                transform.position + right * StuckSideStepDistance,
                transform.position - right * StuckSideStepDistance
            };

            for (var index = 0; index < candidates.Length; index++)
            {
                if (TryResolveNavMeshPosition(candidates[index], out sideStepPosition))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 响应到达排队点事件并同步朝向。
        /// </summary>
        private void OnReachQueue()
        {
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
                agent.Warp(currentDestination);
            }

            ApplyQueueRotation();
            isWaitingAtQueueSlot = true;
            // 保持 Queueing，避免落成 None 后补位/点单状态语义不清。
            state = CustomerState.Queueing;
            // 进店时已决定的前 2 名额：站定后再通知前台开始点单，不靠 Update 轮询。
            if (IsFrontCounterOrderCandidate)
            {
                owner?.NotifyCustomerReachedQueueSlotForFrontOrder(this);
            }
        }

        /// <summary>
        /// 出生/排队点若被误判为已到达，需先走出可见路程再允许入座。
        /// </summary>
        private bool ShouldDeferSeatArrival()
        {
            var minDistance = IsVip ? VipMinWalkToSeatDistance : MinWalkToSeatDistance;
            var minSeconds = IsVip ? VipMinWalkToSeatSeconds : MinWalkToSeatSeconds;
            var planarDelta = transform.position - tableAssignOrigin;
            planarDelta.y = 0f;
            return planarDelta.sqrMagnitude < minDistance * minDistance
                   && tableAssignElapsed < minSeconds;
        }

        /// <summary>
        /// Agent 路径结束只表示走到 NavMesh 可达点，不等于到达 SeatSlot。
        /// </summary>
        private bool CanCompleteSeatApproach()
        {
            if (tableAssignElapsed >= SeatApproachForceArrivalSeconds)
            {
                return true;
            }

            // 座位查询失败时不可直接判定完成，否则会在门口触发入座并瞬移到凳子。
            if (!TryGetAssignedSeatPosition(out var seatPosition))
            {
                return false;
            }

            var toSeat = transform.position - seatPosition;
            toSeat.y = 0f;
            return toSeat.sqrMagnitude <= SeatArrivalMaxPlanarDistance * SeatArrivalMaxPlanarDistance;
        }

        private void ResumeSeatApproachMovement()
        {
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.isStopped = false;
            }

            var destination = hasSeatApproachDestination ? seatApproachDestination : currentDestination;
            if (owner != null
                && owner.AllTables.TryGetValue(TableId, out var table)
                && table != null
                && table.TryGetSeatPoseByIndex(SeatIndex, out var seatPosition, out var lookAtPosition))
            {
                var awayFromTable = seatPosition - lookAtPosition;
                awayFromTable.y = 0f;
                if (awayFromTable.sqrMagnitude > 0.0001f)
                {
                    awayFromTable.Normalize();
                    destination = seatPosition + awayFromTable * 0.28f;
                }
                else
                {
                    destination = seatPosition;
                }

                seatApproachDestination = destination;
                hasSeatApproachDestination = true;
            }

            ForceMoveTo(destination);
        }

        private bool TryGetAssignedSeatPosition(out Vector3 seatPosition)
        {
            seatPosition = Vector3.zero;
            if (owner == null
                || !owner.AllTables.TryGetValue(TableId, out var table)
                || table == null)
            {
                return false;
            }

            return table.TryGetSeatPoseByIndex(SeatIndex, out seatPosition, out _);
        }

        /// <summary>
        /// 响应到达桌位事件并同步状态。
        /// </summary>
        private void OnReachTable()
        {
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = true;
                agent.enabled = false;
            }

            // 入座阶段先关闭寻路，避免导航代理持续修正位置导致坐姿偏移。
            TriggerSitDownAnimation();
            StartCoroutine(NotifySeatedDelayed());
        }

        /// <summary>
        /// 处理应用排队朝向相关逻辑。
        /// </summary>
        private void ApplyQueueRotation()
        {
            var forward = queueTargetRotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        /// <summary>
        /// 延迟通知桌位顾客已经入座。
        /// </summary>
        /// <returns>协程迭代器。</returns>
        private IEnumerator NotifySeatedDelayed()
        {
            yield return new WaitForSeconds(SitBlendDelay);
            SnapToSeatPose();
            if (hasIsSittingBool && animator != null)
            {
                animator.SetBool("IsSitting", true);
            }

            IsSeated = true;
            state = CustomerState.None;
            ShowDesiredProductBubble();
            owner?.TrackCustomerState(this, CustomerStateKeys.Seated);
            owner.NotifyCustomerSeated(this);
        }

        // 坐下 动画事件回调：在动作结束点再对齐一次座位姿态，减少动画漂移。
        public void OnSitDownComplete()
        {
            SnapToSeatPose();
            if (hasIsSittingBool && animator != null)
            {
                animator.SetBool("IsSitting", true);
            }
        }

        // 起身 动画事件回调：离桌前同步清理坐下状态。
        public void OnStandUpAnimationComplete()
        {
            if (hasIsSittingBool && animator != null)
            {
                animator.SetBool("IsSitting", false);
            }
        }

        /// <summary>
        /// 显示点单气泡。
        /// </summary>
        /// <param name="dishNames">名称。</param>
        public void ShowOrderBubbles(IReadOnlyList<string> dishNames)
        {
            if (dishNames == null || dishNames.Count == 0)
            {
                ClearOrderBubbles();
                return;
            }

            var contents = new List<OrderBubbleContent>(dishNames.Count);
            for (var index = 0; index < dishNames.Count; index++)
            {
                var dishName = dishNames[index];
                contents.Add(new OrderBubbleContent
                {
                    Text = dishName,
                    Icon = ResolveProductIconByName(dishName)
                });
            }

            ShowOrderBubbles(contents);
        }

        /// <summary>
        /// 显示点单气泡。
        /// </summary>
        /// <param name="products">菜品列表。</param>
        public void ShowOrderBubbles(IReadOnlyList<SO_Product> products)
        {
            if (products == null || products.Count == 0)
            {
                ClearOrderBubbles();
                return;
            }

            var contents = new List<OrderBubbleContent>(products.Count);
            for (var index = 0; index < products.Count; index++)
            {
                var product = products[index];
                if (product == null)
                {
                    continue;
                }

                contents.Add(new OrderBubbleContent
                {
                    Text = product.displayName,
                    Icon = product.icon
                });
            }

            ShowOrderBubbles(contents);
        }

        /// <summary>
        /// 显示点单气泡。
        /// </summary>
        /// <param name="contents">气泡内容。</param>
        private void ShowOrderBubbles(IReadOnlyList<OrderBubbleContent> contents)
        {
            ClearOrderBubbles();
            // 玩法调整：关闭顾客头顶 OrderBubble_0 类点菜/用餐气泡。
            return;
        }

        /// <summary>
        /// 根据顾客相对桌子中心在左侧还是右侧，返回点单气泡的水平偏移。
        /// </summary>
        /// <returns>世界坐标偏移。</returns>
        private Vector3 ResolveOrderBubbleSideOffset()
        {
            if (owner == null || !owner.AllTables.TryGetValue(TableId, out var table) || table == null)
            {
                return Vector3.zero;
            }

            // 用“顾客在桌子 right 方向上的投影”判断左右侧：
            // dot > 0 视为右侧，dot < 0 视为左侧。
            var tableCenter = table.transform.position;
            var toCustomer = transform.position - tableCenter;
            toCustomer.y = 0f;
            var tableRight = table.transform.right;
            tableRight.y = 0f;
            if (tableRight.sqrMagnitude <= 0.0001f)
            {
                return Vector3.zero;
            }

            tableRight.Normalize();
            var onRightSide = Vector3.Dot(toCustomer, tableRight) >= 0f;
            return onRightSide
                ? tableRight * OrderBubbleRightSideOffsetDistance
                : -tableRight * OrderBubbleLeftSideOffsetDistance;
        }

        /// <summary>
        /// 清理点单气泡。
        /// </summary>
        private void ClearOrderBubbles()
        {
            if (hideOrderBubblesCoroutine != null)
            {
                StopCoroutine(hideOrderBubblesCoroutine);
                hideOrderBubblesCoroutine = null;
            }

            for (var index = 0; index < activeOrderBubbles.Count; index++)
            {
                var bubble = activeOrderBubbles[index];
                if (bubble != null)
                {
                    Destroy(bubble);
                }
            }

            activeOrderBubbles.Clear();

            for (var index = transform.childCount - 1; index >= 0; index--)
            {
                var child = transform.GetChild(index);
                if (child != null && child.name.StartsWith("OrderBubble_"))
                {
                    Destroy(child.gameObject);
                }
            }
        }

        private void RestartOrderBubbleAutoHide()
        {
            if (hideOrderBubblesCoroutine != null)
            {
                StopCoroutine(hideOrderBubblesCoroutine);
            }

            hideOrderBubblesCoroutine = StartCoroutine(HideOrderBubblesDelayed());
        }

        private IEnumerator HideOrderBubblesDelayed()
        {
            yield return new WaitForSeconds(OrderBubbleDisplayDuration);
            hideOrderBubblesCoroutine = null;
            ClearOrderBubbles();
        }

        private void ShowDesiredProductBubble()
        {
            if (desiredProduct == null)
            {
                return;
            }

            ShowOrderBubbles(new[] { desiredProduct });
        }

        private static SO_Product ResolveRandomDesiredProduct()
        {
            var products = SO_Product.GetUnlockedForCurrentChefLevel();
            if (products == null || products.Count == 0)
            {
                return null;
            }

            var startIndex = Random.Range(0, products.Count);
            SO_Product fallbackProduct = null;
            for (var offset = 0; offset < products.Count; offset++)
            {
                var product = products[(startIndex + offset) % products.Count];
                if (product == null)
                {
                    continue;
                }

                fallbackProduct ??= product;
                if (product.icon != null)
                {
                    return product;
                }
            }

            return fallbackProduct;
        }

        private Sprite ResolveProductIconByName(string dishName)
        {
            if (string.IsNullOrWhiteSpace(dishName))
            {
                return null;
            }

            if (desiredProduct != null
                && !string.IsNullOrWhiteSpace(desiredProduct.displayName)
                && desiredProduct.displayName == dishName)
            {
                return desiredProduct.icon;
            }

            var products = SO_Product.GetAll();
            if (products == null)
            {
                return null;
            }

            for (var index = 0; index < products.Count; index++)
            {
                var product = products[index];
                if (product != null && product.displayName == dishName)
                {
                    return product.icon;
                }
            }

            return null;
        }

        private struct OrderBubbleContent
        {
            public string Text;
            public Sprite Icon;
        }

        private IEnumerator LeaveAfterStandUpRoutine()
        {
            TriggerStandUpAnimation();
            yield return new WaitForSeconds(StandBlendDelay);
            if (agent != null && !agent.enabled)
            {
                agent.enabled = true;
            }

            // 重新启用寻路后再离场，避免站起动作期间被寻路系统拖拽。
            MoveTo(exitPosition);
        }

        /// <summary>
        /// 触发顾客坐下动画。
        /// </summary>
        private void TriggerSitDownAnimation()
        {
            if (animator == null)
            {
                return;
            }

            if (hasIsSittingBool)
            {
                animator.SetBool("IsSitting", false);
            }

            if (hasSitDownTrigger)
            {
                animator.SetTrigger("SitDown");
            }
        }

        /// <summary>
        /// 触发顾客起身动画。
        /// </summary>
        private void TriggerStandUpAnimation()
        {
            if (animator == null)
            {
                return;
            }

            StopEatingAnimation();
            if (hasIsSittingBool)
            {
                animator.SetBool("IsSitting", false);
            }

            if (hasStandUpTrigger)
            {
                animator.SetTrigger("StandUp");
            }
        }

        /// <summary>
        /// 启动吃饭动画。
        /// </summary>
        private void StartEatingAnimation()
        {
            if (animator == null)
            {
                return;
            }

            if (hasIsEatingBool)
            {
                animator.SetBool("IsEating", true);
            }

            if (hasStartEatTrigger)
            {
                animator.SetTrigger("StartEat");
            }
        }

        /// <summary>
        /// 停止吃饭动画。
        /// </summary>
        private void StopEatingAnimation()
        {
            if (animator == null)
            {
                return;
            }

            if (hasIsEatingBool)
            {
                animator.SetBool("IsEating", false);
            }

            if (hasStopEatTrigger)
            {
                animator.SetTrigger("StopEat");
            }
        }

        /// <summary>
        /// 处理吸附到座位姿态相关逻辑。
        /// </summary>
        private void SnapToSeatPose()
        {
            if (owner == null || !owner.AllTables.TryGetValue(TableId, out var table))
            {
                return;
            }

            if (!table.TryGetSeatPoseByIndex(SeatIndex, out var seatPosition, out var lookAtPosition)
                && !table.TryGetNearestSeatPose(transform.position, out seatPosition, out lookAtPosition))
            {
                return;
            }

            var towardTable = lookAtPosition - seatPosition;
            towardTable.y = 0f;
            if (towardTable.sqrMagnitude > 0.0001f)
            {
                towardTable.Normalize();

                // 不把角色完全贴在 座位点 上，而是向桌面轻推一点，让屁股与凳子、更靠桌的姿态更自然。
                var snappedPosition = seatPosition + towardTable * SeatTowardTableOffset;
                snappedPosition += table.GetSeatSnapPlanarOffset(SeatIndex, seatPosition);
                snappedPosition.y = table.GetSeatedCustomerY();
                transform.position = snappedPosition;
                transform.rotation = Quaternion.LookRotation(towardTable, Vector3.up);
            }
            else
            {
                var snappedPosition = seatPosition + table.GetSeatSnapPlanarOffset(SeatIndex, seatPosition);
                snappedPosition.y = table.GetSeatedCustomerY();
                transform.position = snappedPosition;
            }
        }

        /// <summary>
        /// 顾客结束就餐后冒一句评价气泡。
        /// </summary>
        private void ShowDiningSpeech()
        {
            if (DiningSpeechTexts == null || DiningSpeechTexts.Length == 0)
            {
                return;
            }

            var randomText = DiningSpeechTexts[Random.Range(0, DiningSpeechTexts.Length)];
            if (string.IsNullOrWhiteSpace(randomText))
            {
                return;
            }

            ShowOrderBubbles(new[] { randomText });
        }

        /// <summary>
        /// 尝试处理在导航网格上启用代理。
        /// </summary>
        /// <param name="preferredPosition">坐标。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private bool TryEnableAgentOnNavMesh(Vector3 preferredPosition)
        {
            if (agent == null)
            {
                return false;
            }

            if (!TryResolveNavMeshPosition(preferredPosition, out var navMeshPosition))
            {
                return false;
            }

            if (!agent.enabled)
            {
                agent.enabled = true;
            }

            if (agent.isOnNavMesh)
            {
                return true;
            }

            return agent.Warp(navMeshPosition);
        }

        /// <summary>
        /// 尝试处理解析导航网格位置。
        /// </summary>
        /// <param name="preferredPosition">坐标。</param>
        /// <param name="navMeshPosition">坐标。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private static bool TryResolveNavMeshPosition(Vector3 preferredPosition, out Vector3 navMeshPosition)
        {
            if (NavMesh.SamplePosition(preferredPosition, out var hit, NavMeshSampleDistance, NavMesh.AllAreas))
            {
                navMeshPosition = hit.position;
                return true;
            }

            navMeshPosition = preferredPosition;
            return false;
        }
    }

    /// <summary>
    /// 负责酒楼顾客动画事件转发器相关的运行时逻辑。
    /// </summary>
    public sealed class TavernCustomerAnimationEventRelay : MonoBehaviour
    {
        private TavernCustomerRuntimeController owner;

        /// <summary>
        /// 处理绑定相关逻辑。
        /// </summary>
        /// <param name="runtimeController">持续时间。</param>
        public void Bind(TavernCustomerRuntimeController runtimeController)
        {
            owner = runtimeController;
        }

        /// <summary>
        /// 响应坐下完成事件并同步状态。
        /// </summary>
        public void OnSitDownComplete()
        {
            owner?.OnSitDownComplete();
        }

        /// <summary>
        /// 响应起身动画完成事件并同步状态。
        /// </summary>
        public void OnStandUpAnimationComplete()
        {
            owner?.OnStandUpAnimationComplete();
        }
    }
}
