using System.Collections;
using UnityEngine;

namespace JN.Client.Scene
{
    public class NPCPatrol : MonoBehaviour
    {
        [Header("巡逻点设置")] public Transform[] points; // 巡逻点数组
        public float speed = 2f; // 移动速度
        public float arriveDistance = 0.1f; // 到点判定距离

        [Header("旋转设置")] public float turnSpeed = 5f; // 平滑转向速度

        [Header("停顿设置")] public bool waitAtPoint = true; // 到点是否停顿
        public float waitTime = 1f; // 停顿时间（秒）

        [Header("动画设置")] public Animator animator; // 非玩家角色动画器
        public string speedParam = "Speed"; // 动画器浮点参数名
        public float walkAnimSpeed = 0.65f; // 移动时传给动画器的稳定走路速度

        private int index; // 当前巡逻点索引
        private int direction = 1; // 巡逻方向：1=正向，-1=反向
        private bool isWaiting;
        private bool hasSpeedParam;

        /// <summary>
        /// 初始化时缓存动画参数，避免运行时反复访问不存在的 Animator 参数。
        /// </summary>
        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            hasSpeedParam = HasFloatParameter(animator, speedParam);
        }

        /// <summary>
        /// 逐帧处理输入、状态推进或动画刷新。
        /// </summary>
        private void Update()
        {
            if (points == null || points.Length == 0 || isWaiting)
            {
                SetMoveAnimationSpeed(0f);
                return;
            }

            Transform target = points[index];
            Vector3 targetPos = new Vector3(target.position.x, transform.position.y, target.position.z);
            Vector3 oldPos = transform.position;

            transform.position = Vector3.MoveTowards(transform.position, targetPos, speed * Time.deltaTime);

            Vector3 dir = targetPos - transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }

            Vector3 delta = transform.position - oldPos;
            SetMoveAnimationSpeed(delta.sqrMagnitude > 0.000001f ? walkAnimSpeed : 0f);

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
            SetMoveAnimationSpeed(0f);
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

        /// <summary>
        /// 安全设置巡逻移动动画速度。
        /// </summary>
        /// <param name="value">动画速度参数值。</param>
        private void SetMoveAnimationSpeed(float value)
        {
            if (animator != null && hasSpeedParam)
            {
                animator.SetFloat(speedParam, value);
            }
        }

        /// <summary>
        /// 判断动画器是否包含指定 Float 参数。
        /// </summary>
        /// <param name="targetAnimator">动画器。</param>
        /// <param name="parameterName">参数名。</param>
        /// <returns>存在对应 Float 参数时返回 true。</returns>
        private static bool HasFloatParameter(Animator targetAnimator, string parameterName)
        {
            if (targetAnimator == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            foreach (var parameter in targetAnimator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Float && parameter.name == parameterName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
