using System.Collections;
using System.Collections.Generic;
using JN.Client;
using JN.Client.Config;
using JN.Client.Manager;
using JN.Client.Model;
using JN.Client.UI;
using UnityEngine;

namespace JN.Client.Scene
{
    public partial class TavernSceneManager
    {
        private const float WaiterAttractIntervalSeconds = 2.5f;
        private const int WaiterAttractMinTableFillsDefault = 3;
        /// <summary>至少两桌空桌时小二才自发拉客。</summary>
        private const int WaiterAttractMinEmptyTables = 2;
        /// <summary>连续拉客失败超过此次数则放弃本轮，回归工作。</summary>
        private const int WaiterAttractMaxConsecutiveFailedWaves = 3;
        private const string WaiterAttractVoluntaryNotice = "入座率低，员工自发拉客";
        private const string WaiterAttractPointName = "LaPoint";
        private const string WaiterAttractBubblePrefabPath = "Assets/Res/Resources/UI/Guides/WaiterAttractBubble.prefab";
        private const string WaiterAttractBubbleFallbackPrefabPath = "Assets/Res/Resources/UI/Guides/CounterRewardBubble.prefab";
        private const string WaiterAttractIconPath = "Assets/Res/Resources/Textures/UI/TechTree/lanke1.png";
        /// <summary>拉客头顶气泡比任务进度条再高一些，避免与角色头部重叠。</summary>
        private const float WaiterAttractBubbleHeadOffset = 1.75f;

        private readonly HashSet<GameObject> attractingWaiters = new();
        private readonly Dictionary<GameObject, WaiterAttractSession> waiterAttractSessions = new();
        private readonly Dictionary<GameObject, GameObject> waiterAttractBubbleRoots = new();

        private sealed class WaiterAttractSession
        {
            public int AttractedCustomerCount;
            /// <summary>本轮已成功拉客次数（每成功 spawn 一次 +1，满 minTableFills 后视空桌决定是否续拉）。</summary>
            public int AttractedTableFillCount;
            public int ConsecutiveFailedSpawnWaves;
            /// <summary>强制演示拉客，不受空桌/排队等常规门槛限制。</summary>
            public bool IsUnlockIntro;
        }

        private bool pendingUnlockWaiterAttract;
        private readonly Queue<int> pendingUnlockableTableAttractQueue = new();

        private bool IsWaiterAttractFeatureActive()
        {
            return DataManager.Instance != null
                   && !DataManager.Instance.IsVisitingOtherTavern
                   && DataManager.Instance.IsVisitCustomerEnabled()
                   && IsBusinessActive;
        }

        /// <summary>
        /// 软/硬打烊阶段：停止接新拉客，但不打断已在拉客的小二。
        /// </summary>
        private bool IsWaiterAttractIntakeClosed()
        {
            return softClosingStarted || isClosingBusiness;
        }

        private bool CanStartNewWaiterAttract(bool isUnlockIntro)
        {
            return !IsWaiterAttractIntakeClosed()
                   && (isUnlockIntro || IsWaiterAttractFeatureActive());
        }

        private int CountIdleUnlockedTables()
        {
            if (DataManager.Instance == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var tablePair in AllTables)
            {
                var tableData = DataManager.Instance.GetTableData(tablePair.Key);
                if (tableData == null
                    || !tableData.isUnlocked
                    || pendingUpgradeTableIds.Contains(tablePair.Key)
                    || (TavernTableRuntimeState)tableData.runtimeState != TavernTableRuntimeState.Idle
                    || IsTableBlockedForNewSeating(tablePair.Key))
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private int CountEligibleIdleWaiters(bool excludeAttracting)
        {
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            var count = 0;
            for (var index = 0; index < waiters.Length; index++)
            {
                var waiter = waiters[index];
                if (!IsWaiterEligibleForAttractAssignment(waiter, excludeAttracting))
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private bool IsWaiterEligibleForAttractAssignment(GameObject waiter, bool excludeAttracting)
        {
            if (waiter == null
                || busyWaiters.Contains(waiter)
                || staffVisualsBeingAnimated.Contains(waiter)
                || IsWaiterNapping(waiter))
            {
                return false;
            }

            if (excludeAttracting && (attractingWaiters.Contains(waiter) || IsWaiterInAttractFlow(waiter)))
            {
                return false;
            }

            if (waiterTaskRoutines.ContainsKey(waiter)
                && waiterContexts.TryGetValue(waiter, out var context)
                && context != null
                && context.CurrentStateKey != WaiterStateKeys.Idle
                && context.CurrentStateKey != WaiterStateKeys.ReturningHome
                && context.CurrentStateKey != WaiterStateKeys.MoveToAttractPoint
                && context.CurrentStateKey != WaiterStateKeys.Attracting)
            {
                return false;
            }

            return true;
        }

        private bool IsWaiterInAttractFlow(GameObject waiter)
        {
            if (waiter == null || !waiterContexts.TryGetValue(waiter, out var context) || context == null)
            {
                return false;
            }

            return context.CurrentStateKey == WaiterStateKeys.MoveToAttractPoint
                   || context.CurrentStateKey == WaiterStateKeys.Attracting;
        }

        private int ResolveDesiredAttractCount(int emptyTables)
        {
            if (emptyTables < WaiterAttractMinEmptyTables)
            {
                return 0;
            }

            return emptyTables <= 2 ? 1 : 2;
        }

        private int ResolveMaxAttractSlots(int emptyTables)
        {
            var desired = ResolveDesiredAttractCount(emptyTables);
            if (desired <= 0)
            {
                return attractingWaiters.Count;
            }

            var eligibleIdle = CountEligibleIdleWaiters(excludeAttracting: false);
            // 至少留 1 名小二在店内接派单；仅 1 人在岗时不新派拉客。
            var attractCap = Mathf.Max(0, eligibleIdle - 1);
            if (attractCap <= 0)
            {
                return attractingWaiters.Count;
            }

            return Mathf.Min(
                desired,
                TbConfigRuntime.GetWaiterAttractMaxWaiters(2),
                attractCap);
        }

        private void EnsureIdleWaiterPosture()
        {
            // 有待清扫桌时禁止遣回默认站位，否则点结账后小二会先回家再出来清桌。
            if (HasUnassignedCleaningTables())
            {
                return;
            }

            if (IsWaiterAttractIntakeClosed())
            {
                ReturnNonAttractingIdleWaitersHome();
                return;
            }

            TryConsumePendingUnlockWaiterAttract();
            TryConsumePendingUnlockableTableAttract();

            if (!IsWaiterAttractFeatureActive())
            {
                ClearAllWaiterAttracting();
                EnsureAllWaitersReturnedHome();
                return;
            }

            if (queuedCustomers.Count >= GetEffectiveMaxQueueSize() && !HasUnlockIntroAttractingWaiter())
            {
                if (!HasNonIntroIncompleteAttractBatch())
                {
                    ClearAllWaiterAttracting();
                    EnsureAllWaitersReturnedHome();
                    return;
                }
            }

            StopNonIntroWaitersPastAttractCommitment();

            var emptyTables = CountIdleUnlockedTables();
            var desiredAttract = ResolveDesiredAttractCount(emptyTables);
            var maxAttractSlots = ResolveMaxAttractSlots(emptyTables);

            if (emptyTables >= WaiterAttractMinEmptyTables && attractingWaiters.Count > maxAttractSlots)
            {
                TrimAttractingWaitersTo(maxAttractSlots);
            }

            if (emptyTables >= WaiterAttractMinEmptyTables && desiredAttract > 0)
            {
                AssignAttractingWaitersUntil(maxAttractSlots);
            }

            ReturnNonAttractingIdleWaitersHome();
        }

        /// <summary>满座或已有排队：不再拉客。</summary>
        private bool ShouldStopWaiterAttractForCapacity()
        {
            return CountIdleUnlockedTables() <= 0 || queuedCustomers.Count > 0;
        }

        /// <summary>空桌不足 2：不再缺客，不应继续拉客。</summary>
        private bool ShouldStopWaiterAttractForLowDemand()
        {
            return CountIdleUnlockedTables() < WaiterAttractMinEmptyTables;
        }

        private bool IsWaiterAttractBatchIncomplete(WaiterAttractSession session)
        {
            return session != null
                   && session.AttractedTableFillCount < GetWaiterAttractMinTableFills();
        }

        private bool HasNonIntroIncompleteAttractBatch()
        {
            foreach (var waiter in attractingWaiters)
            {
                if (waiter == null)
                {
                    continue;
                }

                if (!waiterAttractSessions.TryGetValue(waiter, out var session)
                    || session == null
                    || session.IsUnlockIntro)
                {
                    continue;
                }

                if (IsWaiterAttractBatchIncomplete(session))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 按每位小二的拉客进度决定是否结束；未满三拨不会因空桌变少被整体清掉。
        /// </summary>
        private void StopNonIntroWaitersPastAttractCommitment()
        {
            var snapshot = new List<GameObject>(attractingWaiters);
            for (var index = 0; index < snapshot.Count; index++)
            {
                var waiter = snapshot[index];
                if (waiter == null)
                {
                    continue;
                }

                if (!waiterAttractSessions.TryGetValue(waiter, out var session)
                    || session == null
                    || session.IsUnlockIntro)
                {
                    continue;
                }

                if (ShouldWaiterVoluntarilyStopAttracting(waiter))
                {
                    StopWaiterAttract(waiter);
                }
            }
        }

        private bool HasWaiterAttractFailedTooManyTimes(WaiterAttractSession session)
        {
            return session != null
                   && session.ConsecutiveFailedSpawnWaves >= WaiterAttractMaxConsecutiveFailedWaves;
        }

        private int GetWaiterAttractMinTableFills()
        {
            return TbConfigRuntime.GetWaiterAttractMinTableFills(WaiterAttractMinTableFillsDefault);
        }

        /// <summary>
        /// 每成功拉一次客后检查：满一批且仍缺客则重置计数续拉，否则本轮结束。
        /// </summary>
        private void TryRollWaiterAttractBatchAfterSpawn(GameObject waiter, WaiterAttractSession session)
        {
            if (waiter == null || session == null || session.IsUnlockIntro)
            {
                return;
            }

            var minTableFills = GetWaiterAttractMinTableFills();
            if (session.AttractedTableFillCount < minTableFills)
            {
                return;
            }

            if (CountIdleUnlockedTables() >= WaiterAttractMinEmptyTables)
            {
                session.AttractedTableFillCount = 0;
                session.AttractedCustomerCount = 0;
                session.ConsecutiveFailedSpawnWaves = 0;
                RefreshWaiterAttractBubble(waiter, session);
                return;
            }

            // 满一批但空桌已不足 2：下轮 voluntary/force stop 会结束拉客。
        }

        private void ClearNonIntroWaiterAttracting()
        {
            var snapshot = new List<GameObject>(attractingWaiters);
            for (var index = 0; index < snapshot.Count; index++)
            {
                var waiter = snapshot[index];
                if (waiterAttractSessions.TryGetValue(waiter, out var session)
                    && session != null
                    && session.IsUnlockIntro)
                {
                    continue;
                }

                StopWaiterAttract(waiter);
            }
        }

        private void AssignAttractingWaitersUntil(int targetCount)
        {
            if (targetCount <= 0)
            {
                return;
            }

            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            for (var index = waiters.Length - 1; index >= 0 && attractingWaiters.Count < targetCount; index--)
            {
                var waiter = waiters[index];
                if (waiter == null || attractingWaiters.Contains(waiter) || !IsWaiterEligibleForAttractAssignment(waiter, excludeAttracting: true))
                {
                    continue;
                }

                BeginWaiterAttract(waiter);
            }
        }

        private void BeginWaiterAttract(GameObject waiter, bool isUnlockIntro = false)
        {
            if (waiter == null || attractingWaiters.Contains(waiter))
            {
                return;
            }

            if (!CanStartNewWaiterAttract(isUnlockIntro))
            {
                return;
            }

            StopWaiterHomeReturn(waiter);
            EnsureWaiterAnimationReceiver(waiter);
            var isFirstAttract = attractingWaiters.Count == 0;
            var session = GetOrCreateAttractSession(waiter);
            session.AttractedCustomerCount = 0;
            session.AttractedTableFillCount = 0;
            session.ConsecutiveFailedSpawnWaves = 0;
            session.IsUnlockIntro = isUnlockIntro;
            attractingWaiters.Add(waiter);
            if (isFirstAttract)
            {
                HudOverlayService.ShowFloatingWarning(WaiterAttractVoluntaryNotice);
            }
            SetWaiterServiceState(waiter, WaiterServiceState.Attracting);
            ((IWaiterRuntimeHost)this).EnsureWaiterAttractBubble(waiter);
            waiterTaskDispatchService.StartAttractCustomers(this, waiter);
        }

        /// <summary>
        /// 拉客科技解锁后：无视空桌/排队等条件，立即尝试启动一次拉客演示。
        /// </summary>
        public void NotifyVisitCustomerTechUnlocked()
        {
            pendingUnlockWaiterAttract = true;
            TryConsumePendingUnlockWaiterAttract();
            TryConsumePendingUnlockableTableAttract();
        }

        /// <summary>
        /// 桌位建造入口首次变为可解锁时，触发一次强制拉客（需已研究拉客科技且营业中）。
        /// </summary>
        public void NotifyUnlockableTableAvailable(int tableId)
        {
            if (tableId <= 0)
            {
                return;
            }

            foreach (var pendingTableId in pendingUnlockableTableAttractQueue)
            {
                if (pendingTableId == tableId)
                {
                    return;
                }
            }

            pendingUnlockableTableAttractQueue.Enqueue(tableId);
            TryConsumePendingUnlockableTableAttract();
        }

        private void TryConsumePendingUnlockableTableAttract()
        {
            while (pendingUnlockableTableAttractQueue.Count > 0)
            {
                if (!IsWaiterAttractFeatureActive())
                {
                    return;
                }

                if (!TryForceBeginUnlockWaiterAttract())
                {
                    return;
                }

                pendingUnlockableTableAttractQueue.Dequeue();
            }
        }

        private void TryConsumePendingUnlockWaiterAttract()
        {
            if (!pendingUnlockWaiterAttract)
            {
                return;
            }

            if (!IsWaiterAttractFeatureActive())
            {
                return;
            }

            if (TryForceBeginUnlockWaiterAttract())
            {
                pendingUnlockWaiterAttract = false;
            }
        }

        private bool TryForceBeginUnlockWaiterAttract()
        {
            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            for (var index = waiters.Length - 1; index >= 0; index--)
            {
                var waiter = waiters[index];
                if (waiter == null
                    || attractingWaiters.Contains(waiter)
                    || !IsWaiterEligibleForAttractAssignment(waiter, excludeAttracting: true))
                {
                    continue;
                }

                BeginWaiterAttract(waiter, isUnlockIntro: true);
                return true;
            }

            return false;
        }

        private bool HasUnlockIntroAttractingWaiter()
        {
            foreach (var waiter in attractingWaiters)
            {
                if (waiter == null)
                {
                    continue;
                }

                if (waiterAttractSessions.TryGetValue(waiter, out var session)
                    && session != null
                    && session.IsUnlockIntro)
                {
                    return true;
                }
            }

            return false;
        }

        private void TrimAttractingWaitersTo(int targetCount)
        {
            if (attractingWaiters.Count <= targetCount)
            {
                return;
            }

            var removeList = new List<GameObject>(attractingWaiters.Count);
            foreach (var waiter in attractingWaiters)
            {
                if (attractingWaiters.Count - removeList.Count <= targetCount)
                {
                    break;
                }

                if (ShouldWaiterVoluntarilyStopAttracting(waiter))
                {
                    removeList.Add(waiter);
                }
            }

            for (var index = removeList.Count - 1; index >= 0 && attractingWaiters.Count > targetCount; index--)
            {
                StopWaiterAttract(removeList[index]);
            }

            if (attractingWaiters.Count <= targetCount)
            {
                return;
            }

            var overflow = new List<GameObject>(attractingWaiters);
            overflow.Sort((left, right) => ResolveWaiterHomeIndex(left).CompareTo(ResolveWaiterHomeIndex(right)));
            for (var index = 0; index < overflow.Count && attractingWaiters.Count > targetCount; index++)
            {
                if (!ShouldWaiterVoluntarilyStopAttracting(overflow[index]))
                {
                    continue;
                }

                StopWaiterAttract(overflow[index]);
            }
        }

        private void ReturnNonAttractingIdleWaitersHome()
        {
            if (HasUnassignedCleaningTables())
            {
                return;
            }

            var waiters = GetGuideStaffVisuals(GuideWaiterVisualKey);
            for (var index = 0; index < waiters.Length; index++)
            {
                var waiter = waiters[index];
                if (waiter == null
                    || busyWaiters.Contains(waiter)
                    || attractingWaiters.Contains(waiter)
                    || IsWaiterInAttractFlow(waiter)
                    || waiterTaskRoutines.ContainsKey(waiter)
                    || staffVisualsBeingAnimated.Contains(waiter)
                    || IsWaiterNapping(waiter)
                    || waitersSuppressHomeReturn.Contains(waiter))
                {
                    continue;
                }

                if (waiterContexts.TryGetValue(waiter, out var context)
                    && context != null
                    && context.CurrentStateKey == WaiterStateKeys.ReturningHome)
                {
                    continue;
                }

                var homeIndex = ResolveWaiterHomeIndex(waiter);
                if (IsWaiterNearHome(waiter, homeIndex))
                {
                    continue;
                }

                EnsureWaiterAnimationReceiver(waiter);
                waiterTaskDispatchService.StartReturnHome(this, waiter);
            }
        }

        private void ClearAllWaiterAttracting()
        {
            if (attractingWaiters.Count == 0)
            {
                return;
            }

            var snapshot = new List<GameObject>(attractingWaiters);
            for (var index = 0; index < snapshot.Count; index++)
            {
                StopWaiterAttract(snapshot[index]);
            }
        }

        private WaiterAttractSession GetOrCreateAttractSession(GameObject waiter)
        {
            if (!waiterAttractSessions.TryGetValue(waiter, out var session) || session == null)
            {
                session = new WaiterAttractSession();
                waiterAttractSessions[waiter] = session;
            }

            return session;
        }

        bool IWaiterRuntimeHost.IsWaiterAttracting(GameObject waiter)
        {
            return waiter != null && attractingWaiters.Contains(waiter);
        }

        /// <summary>
        /// 自发拉客承诺期：未满 minTableFills 且未连续失败达上限前，不因排队/满座/空桌变少而强制停拉。
        /// </summary>
        private bool IsWaiterInAttractCommitmentPhase(WaiterAttractSession session)
        {
            return session != null
                   && !session.IsUnlockIntro
                   && IsWaiterAttractBatchIncomplete(session)
                   && !HasWaiterAttractFailedTooManyTimes(session);
        }

        bool IWaiterRuntimeHost.ShouldWaiterForceStopAttracting(GameObject waiter)
        {
            if (waiter == null || !attractingWaiters.Contains(waiter))
            {
                return true;
            }

            if (!IsWaiterAttractFeatureActive())
            {
                // 软打烊后 IsBusinessActive 为 false，但不强制打断已在拉客的小二。
                return !IsWaiterAttractIntakeClosed();
            }

            var session = GetOrCreateAttractSession(waiter);

            // 承诺期：只认连续 spawn 失败达上限；排队满/满座/空桌不足均不提前打断。
            if (IsWaiterInAttractCommitmentPhase(session))
            {
                return false;
            }

            if (!session.IsUnlockIntro && IsWaiterAttractBatchIncomplete(session))
            {
                return true;
            }

            if (!session.IsUnlockIntro && queuedCustomers.Count >= GetEffectiveMaxQueueSize())
            {
                return true;
            }

            if (!session.IsUnlockIntro
                && (ShouldStopWaiterAttractForCapacity() || ShouldStopWaiterAttractForLowDemand()))
            {
                return true;
            }

            if (busyWaiters.Contains(waiter))
            {
                return true;
            }

            return false;
        }

        bool IWaiterRuntimeHost.ShouldWaiterVoluntarilyStopAttracting(GameObject waiter)
        {
            return ShouldWaiterVoluntarilyStopAttracting(waiter);
        }

        private bool ShouldWaiterVoluntarilyStopAttracting(GameObject waiter)
        {
            if (waiter == null || !attractingWaiters.Contains(waiter))
            {
                return true;
            }

            var session = GetOrCreateAttractSession(waiter);
            if (session.IsUnlockIntro)
            {
                return session.AttractedTableFillCount >= GetWaiterAttractMinTableFills();
            }

            if (HasWaiterAttractFailedTooManyTimes(session))
            {
                return true;
            }

            // 未满三拨：必须继续拉，直到满三拨或连续失败。
            if (IsWaiterAttractBatchIncomplete(session))
            {
                return false;
            }

            // 已满三拨且仍缺客：spawn 回调已重置计数，继续下一批。
            if (CountIdleUnlockedTables() >= WaiterAttractMinEmptyTables)
            {
                return false;
            }

            // 已满三拨且不再缺客。
            return true;
        }

        /// <summary>
        /// 承诺期内：不被店内派工打断；承诺完成或连续失败后派工优先。
        /// </summary>
        private bool IsWaiterAttractLockedForWork(GameObject waiter)
        {
            if (waiter == null)
            {
                return false;
            }

            if (!attractingWaiters.Contains(waiter) && !IsWaiterInAttractFlow(waiter))
            {
                return false;
            }

            var session = GetOrCreateAttractSession(waiter);
            if (session.IsUnlockIntro)
            {
                return false;
            }

            return IsWaiterInAttractCommitmentPhase(session);
        }

        /// <summary>
        /// 排队已满导致暂时无法刷客；承诺期内不计入连续失败。
        /// </summary>
        private bool IsAttractSpawnTemporarilyBlockedByQueue()
        {
            return queuedCustomers.Count >= GetEffectiveMaxQueueSize();
        }

        private bool TryResolveWaiterAttractPosition(GameObject waiter, out Vector3 position)
        {
            position = Vector3.zero;
            if (customerEntryPoint == null)
            {
                return false;
            }

            Vector3 basePosition;
            if (waiterAttractPoint != null)
            {
                basePosition = TryGetNavMeshPosition(waiterAttractPoint.position, out var attractNavMeshPosition)
                    ? attractNavMeshPosition
                    : waiterAttractPoint.position;
            }
            else if (queuePointAnchors.Count > 0 && queuePointAnchors[0] != null)
            {
                var queueAnchor = queuePointAnchors[0];
                basePosition = TryGetNavMeshPosition(queueAnchor.position, out var navMeshPosition)
                    ? navMeshPosition
                    : queueAnchor.position;
            }
            else
            {
                basePosition = GetQueuePosition(0);
            }

            var homeIndex = waiter != null ? ResolveWaiterHomeIndex(waiter) : 0;
            var right = customerEntryPoint.right.sqrMagnitude > 0.1f
                ? customerEntryPoint.right.normalized
                : Vector3.right;
            var laneOffset = right * ((homeIndex % 2 == 0 ? -1f : 1f) * Mathf.Max(spawnLaneSpacing, 0.25f));
            var candidate = basePosition + laneOffset;
            if (TryGetNavMeshPosition(candidate, out position))
            {
                return true;
            }

            position = basePosition;
            return true;
        }

        IEnumerator IWaiterRuntimeHost.MoveWaiterToAttractPoint(GameObject waiter)
        {
            if (waiter == null || customerEntryPoint == null)
            {
                yield break;
            }

            var targetPosition = TryResolveWaiterAttractPosition(waiter, out var attractPosition)
                ? attractPosition
                : customerEntryPoint.position;

            yield return MoveCharacterAlongNavMesh(
                waiter.transform,
                targetPosition,
                GetEffectiveWaiterMoveSpeed(waiter),
                true);

            if (waiter == null)
            {
                yield break;
            }

            var facingTransform = waiterAttractPoint != null ? waiterAttractPoint : customerEntryPoint;
            if (facingTransform != null && facingTransform.forward.sqrMagnitude > 0.01f)
            {
                waiter.transform.rotation = Quaternion.LookRotation(-facingTransform.forward, Vector3.up);
            }

            SetAnimatorSpeed(waiter.GetComponentInChildren<Animator>(true), 0f);
        }

        bool IWaiterRuntimeHost.TrySpawnAttractCustomers(out int spawnedCustomerCount)
        {
            spawnedCustomerCount = 0;
            var beforeCount = activeCustomers.Count + queuedCustomers.Count;
            if (!SpawnCustomerIfPossible(allowVipSpawn: true, vipChanceMultiplier: vipAttractSpawnChanceMultiplier))
            {
                return false;
            }

            var afterCount = activeCustomers.Count + queuedCustomers.Count;
            spawnedCustomerCount = Mathf.Max(0, afterCount - beforeCount);
            return spawnedCustomerCount > 0;
        }

        void IWaiterRuntimeHost.RecordWaiterAttractSpawn(GameObject waiter, int spawnedCustomerCount)
        {
            if (waiter == null || spawnedCustomerCount <= 0)
            {
                return;
            }

            var session = GetOrCreateAttractSession(waiter);
            session.ConsecutiveFailedSpawnWaves = 0;
            session.AttractedCustomerCount += spawnedCustomerCount;
            session.AttractedTableFillCount += 1;
            RefreshWaiterAttractBubble(waiter, session);
            TryRollWaiterAttractBatchAfterSpawn(waiter, session);
        }

        float IWaiterRuntimeHost.GetWaiterAttractIntervalSeconds()
        {
            return TbConfigRuntime.GetWaiterAttractInterval(WaiterAttractIntervalSeconds);
        }

        void IWaiterRuntimeHost.PerformWaiterAttractWave(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            var host = (IWaiterRuntimeHost)this;
            if (!host.TrySpawnAttractCustomers(out var spawnedCount))
            {
                var session = GetOrCreateAttractSession(waiter);
                if (IsAttractSpawnTemporarilyBlockedByQueue()
                    && IsWaiterInAttractCommitmentPhase(session))
                {
                    return;
                }

                RecordWaiterAttractSpawnFailure(waiter);
                return;
            }

            host.RecordWaiterAttractSpawn(waiter, spawnedCount);
        }

        private void RecordWaiterAttractSpawnFailure(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            var session = GetOrCreateAttractSession(waiter);
            session.ConsecutiveFailedSpawnWaves++;
        }

        void IWaiterRuntimeHost.EnsureWaiterAttractBubble(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            if (!waiterAttractBubbleRoots.TryGetValue(waiter, out var bubbleRoot) || bubbleRoot == null)
            {
                var prefab = GameplayResourceStore.LoadAsset<GameObject>(WaiterAttractBubblePrefabPath)
                             ?? GameplayResourceStore.LoadAsset<GameObject>(WaiterAttractBubbleFallbackPrefabPath);
                if (prefab == null)
                {
                    return;
                }

                bubbleRoot = Instantiate(prefab, waiter.transform);
                bubbleRoot.name = "WaiterAttractBubble";
                waiterAttractBubbleRoots[waiter] = bubbleRoot;

                var billboard = bubbleRoot.GetComponent<Billboard>();
                if (billboard != null)
                {
                    billboard.SceneCamera = SceneCamera != null ? SceneCamera : Camera.main;
                }

                var view = bubbleRoot.GetComponent<WaiterAttractBubbleView>();
                if (view == null)
                {
                    view = bubbleRoot.AddComponent<WaiterAttractBubbleView>();
                }

                var icon = GameplayResourceStore.LoadAsset<Sprite>(WaiterAttractIconPath);
                view.SetIcon(icon);
            }

            bubbleRoot.transform.localPosition = new Vector3(0f, WaiterAttractBubbleHeadOffset, 0f);
            bubbleRoot.transform.localRotation = Quaternion.identity;
            RefreshWaiterAttractBubble(waiter, GetOrCreateAttractSession(waiter));
        }

        void IWaiterRuntimeHost.StopWaiterAttract(GameObject waiter)
        {
            StopWaiterAttract(waiter);
        }

        private void StopWaiterAttract(GameObject waiter)
        {
            if (waiter == null)
            {
                return;
            }

            attractingWaiters.Remove(waiter);
            waiterAttractSessions.Remove(waiter);
            StopTrackedWaiterRoutine(waiter);

            if (waiterContexts.TryGetValue(waiter, out var context)
                && context != null
                && (context.CurrentStateKey == WaiterStateKeys.MoveToAttractPoint
                    || context.CurrentStateKey == WaiterStateKeys.Attracting))
            {
                context.SetPassiveState(new WaiterIdleState());
            }

            if (waiterAttractBubbleRoots.TryGetValue(waiter, out var bubbleRoot))
            {
                if (bubbleRoot != null)
                {
                    Destroy(bubbleRoot);
                }

                waiterAttractBubbleRoots.Remove(waiter);
            }

            SetWaiterServiceState(waiter, WaiterServiceState.Idle);
        }

        private void RefreshWaiterAttractBubble(GameObject waiter, WaiterAttractSession session)
        {
            if (waiter == null || session == null)
            {
                return;
            }

            if (!waiterAttractBubbleRoots.TryGetValue(waiter, out var bubbleRoot) || bubbleRoot == null)
            {
                return;
            }

            var view = bubbleRoot.GetComponent<WaiterAttractBubbleView>();
            if (view == null)
            {
                return;
            }

            view.SetCustomerCount(session.AttractedCustomerCount);
        }
    }
}
