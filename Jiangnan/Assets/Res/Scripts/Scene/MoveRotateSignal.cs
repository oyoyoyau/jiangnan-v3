using System;
using System.Collections;
using JN.Client;
using JN.Client.Manager;
using QFramework;
using UnityEngine;

namespace JN.Client.Scene
{
    public class MoveRotateSignal : MonoBehaviour
    {
        private const int TableEquipmentLookupId = 2;

        [Header("目标点")] public Transform checkpointA;
        public Transform checkpointB;

        [Header("移动设置")] public float speed = 2f;
        public float arriveDistance = 0.1f;

        [Header("旋转设置")] public float turnSpeed = 5f;
        public float yOffset = -90f;

        [Header("旋转停顿")] public float rotateWaitTime = 0.5f;

        public Action OnArrived;

        private bool movingToA = true;
        private bool movingToB;
        private bool finished;
        private bool hasRotated;

        // 缓存初始 Transform 与运行协程，便于桌位升级时把搬运动画从头重放。
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private bool hasInitialPose;
        private Coroutine rotateRoutine;
        private Transform carriedTableVisual;
        private int currentVisualLevel;

        [SerializeField] private int tableId;

        /// <summary>
        /// 由外部在启动搬运前写入当前桌位编号，确保运行时能读取到正确桌位等级。
        /// </summary>
        /// <param name="targetTableId">桌位编号。</param>
        public void ConfigureTableId(int targetTableId)
        {
            tableId = targetTableId;
            RefreshCarriedTableVisual();
        }

        /// <summary>
        /// 初始化组件引用和运行时状态。
        /// </summary>
        private void Awake()
        {
            OnArrived += HandleArrived;
            // 记录场景里手摆的起始位姿，后续 Reset 时直接还原即可。
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            hasInitialPose = true;
            CacheCarriedTableVisual();
            currentVisualLevel = 1;
        }

        /// <summary>
        /// 销毁时释放监听、协程和运行时缓存。
        /// </summary>
        private void OnDestroy()
        {
            OnArrived -= HandleArrived;
        }

        /// <summary>
        /// 把搬运状态机重置为起始状态，并将位姿还原到初始记录点，
        /// 用于桌位升级或重复触发搬运时重新播放整段动画。
        /// </summary>
        public void ResetMovement()
        {
            if (rotateRoutine != null)
            {
                StopCoroutine(rotateRoutine);
                rotateRoutine = null;
            }

            movingToA = true;
            movingToB = false;
            finished = false;
            hasRotated = false;

            if (hasInitialPose)
            {
                transform.SetPositionAndRotation(initialPosition, initialRotation);
            }

            RefreshCarriedTableVisual();
        }


        /// <summary>
        /// 逐帧处理输入、状态推进或动画刷新。
        /// </summary>
        private void Update()
        {
            if (finished)
            {
                return;
            }

            if (movingToA)
            {
                MoveToPoint(checkpointA, OnReachA, false);
            }
            else if (movingToB)
            {
                MoveToPoint(checkpointB, OnReachB, !hasRotated);
            }
        }

        /// <summary>
        /// 移动到目标点。
        /// </summary>
        /// <param name="target">目标对象。</param>
        /// <param name="onReach">参数值。</param>
        /// <param name="rotate">参数值。</param>
        private void MoveToPoint(Transform target, Action onReach, bool rotate)
        {
            if (target == null)
            {
                return;
            }

            var targetPos = new Vector3(target.position.x, transform.position.y, target.position.z);
            transform.position = Vector3.MoveTowards(transform.position, targetPos, speed * Time.deltaTime);

            if (rotate)
            {
                var dir = targetPos - transform.position;
                dir.y = 0;

                if (dir.sqrMagnitude > 0.001f)
                {
                    var targetRot = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, yOffset, 0f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
                }
            }

            if (Vector3.Distance(transform.position, targetPos) <= arriveDistance)
            {
                onReach?.Invoke();
            }
        }

        /// <summary>
        /// 响应到达 A 点事件并同步状态。
        /// </summary>
        private void OnReachA()
        {
            movingToA = false;
            hasRotated = true;
            rotateRoutine = StartCoroutine(RotateThenMoveB());
        }

        /// <summary>
        /// 旋转后继续移动到 B 点。
        /// </summary>
        /// <returns>协程迭代器。</returns>
        private IEnumerator RotateThenMoveB()
        {
            yield return new WaitForSeconds(rotateWaitTime);
            movingToB = true;
            rotateRoutine = null;
        }

        /// <summary>
        /// 响应到达 B 点事件并同步状态。
        /// </summary>
        private void OnReachB()
        {
            movingToB = false;
            finished = true;
            OnArrived?.Invoke();
        }

        /// <summary>
        /// 处理到达。
        /// </summary>
        private void HandleArrived()
        {
            DataManager.Instance.UnlockTable(tableId);
            Signals.Get<ArrivedTableSignal>().Dispatch(tableId);
            Signals.Get<TavernRuntimeChangedSignal>().Dispatch();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 缓存载具上当前承载的桌子模型节点。
        /// </summary>
        private void CacheCarriedTableVisual()
        {
            carriedTableVisual = null;
            for (var index = 0; index < transform.childCount; index++)
            {
                var child = transform.GetChild(index);
                if (child == null)
                {
                    continue;
                }

                // 搬运工节点都带 Animator，桌子模型本身不带，用它来区分当前承载物。
                if (child.GetComponentInChildren<Animator>(true) != null)
                {
                    continue;
                }

                carriedTableVisual = child;
                break;
            }
        }

        /// <summary>
        /// 按当前桌位等级同步载具上的桌子模型，避免升级后搬运时仍显示 Lv1。
        /// </summary>
        private void RefreshCarriedTableVisual()
        {
            var tableData = DataManager.Instance != null ? DataManager.Instance.GetTableData(tableId) : null;
            var targetLevel = Mathf.Max(1, tableData != null ? tableData.level : 1);
            if (targetLevel == currentVisualLevel && carriedTableVisual != null)
            {
                return;
            }

            var levelPrefab = LoadTableLevelPrefab(targetLevel);
            if (levelPrefab == null)
            {
                return;
            }

            if (carriedTableVisual == null)
            {
                CacheCarriedTableVisual();
            }

            var anchor = transform;
            var siblingIndex = 0;
            var localPosition = Vector3.zero;
            var localRotation = Quaternion.identity;
            var localScale = Vector3.one;

            if (carriedTableVisual != null)
            {
                anchor = carriedTableVisual.parent != null ? carriedTableVisual.parent : transform;
                siblingIndex = carriedTableVisual.GetSiblingIndex();
                localPosition = carriedTableVisual.localPosition;
                localRotation = carriedTableVisual.localRotation;
                localScale = carriedTableVisual.localScale;
                Destroy(carriedTableVisual.gameObject);
            }

            var instance = Instantiate(levelPrefab, anchor, false);
            instance.name = levelPrefab.name;
            instance.transform.SetSiblingIndex(siblingIndex);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = localScale;

            carriedTableVisual = instance.transform;
            currentVisualLevel = targetLevel;
        }

        /// <summary>
        /// 通过桌子设备配置读取指定等级的场景预制体。
        /// </summary>
        /// <param name="level">桌子等级。</param>
        /// <returns>对应等级的桌子预制体；找不到时返回 null。</returns>
        private static GameObject LoadTableLevelPrefab(int level)
        {
            var equipment = SO_Equipment.GetById(TableEquipmentLookupId);
            if (equipment == null)
            {
                return null;
            }

            var levelConfig = equipment.GetLevelConfig(level);
            return levelConfig != null ? levelConfig.scenePrefab : null;
        }
    }
}
