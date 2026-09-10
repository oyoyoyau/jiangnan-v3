using UnityEngine;

namespace JN.Client.Scene
{
    public class Billboard : MonoBehaviour
    {
        public Camera SceneCamera;

        /// <summary>
        /// 初始化组件引用和运行时状态。
        /// </summary>
        private void Awake()
        {
            if (SceneCamera == null)
            {
                Debug.LogWarning($"{name} 未找到 SceneCamera");
            }
        }

        /// <summary>
        /// 在帧末同步跟随 界面 和场景表现位置。
        /// </summary>
        private void LateUpdate()
        {
            if (SceneCamera == null) return;

            Transform camTransform = SceneCamera.transform;
            transform.LookAt(
                transform.position + camTransform.rotation * Vector3.forward,
                camTransform.rotation * Vector3.up
            );
        }
    }
}
