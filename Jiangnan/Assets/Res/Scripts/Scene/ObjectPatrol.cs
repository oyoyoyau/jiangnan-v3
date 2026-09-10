using System.Collections;
using UnityEngine;

namespace JN.Client.Scene
{
    public class ObjectPatrol : MonoBehaviour
    {
        [Header("巡逻点设置")] public Transform[] points; // 巡逻点数组
        public float speed = 2f; // 移动速度
        public float arriveDistance = 0.1f; // 到点距离判定

        [Header("旋转设置")] public float turnSpeed = 5f; // 平滑转向速度
        public float yOffset = -90f; // 模型正面偏移角度（绕Y轴）

        [Header("停顿设置")] public bool waitAtPoint = true; // 到点是否停顿
        public float waitTime = 1f; // 停顿时间（秒）

        private int index; // 当前巡逻点索引
        private int direction = 1; // 巡逻方向：1 = 正向，-1 = 反向
        private bool isWaiting;

        /// <summary>
        /// 逐帧处理输入、状态推进或动画刷新。
        /// </summary>
        private void Update()
        {
            if (points == null || points.Length == 0 || isWaiting) return;

            Transform target = points[index];
            Vector3 targetPos = new Vector3(target.position.x, transform.position.y, target.position.z);

            transform.position = Vector3.MoveTowards(transform.position, targetPos, speed * Time.deltaTime);

            Vector3 dir = targetPos - transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                targetRot *= Quaternion.Euler(0f, yOffset, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }

            if (Vector3.Distance(transform.position, targetPos) <= arriveDistance)
            {
                if (waitAtPoint)
                    StartCoroutine(WaitNextPoint());
                else
                    NextPoint();
            }
        }

        /// <summary>
        /// 等待后切换到下一个巡逻点。
        /// </summary>
        /// <returns>协程迭代器。</returns>
        private IEnumerator WaitNextPoint()
        {
            isWaiting = true;
            yield return new WaitForSeconds(waitTime);
            NextPoint();
            isWaiting = false;
        }

        /// <summary>
        /// 切换并移动到下一个巡逻点。
        /// </summary>
        private void NextPoint()
        {
            if (index == points.Length - 1)
                direction = -1;
            else if (index == 0)
                direction = 1;

            index += direction;
        }
    }
}
