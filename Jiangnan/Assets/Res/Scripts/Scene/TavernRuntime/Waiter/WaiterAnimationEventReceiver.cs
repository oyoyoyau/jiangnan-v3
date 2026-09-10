using UnityEngine;

namespace JN.Client.Scene
{
    /// <summary>
    /// 接收服务员清扫动画中的事件，避免动画事件找不到接收器时报错。
    /// </summary>
    public sealed class WaiterAnimationEventReceiver : MonoBehaviour
    {
        /// <summary>
        /// 清扫动画尝试显示抹布时的事件入口，当前没有独立抹布表现所以留空。
        /// </summary>
        public void ShowCloth()
        {
        }

        /// <summary>
        /// 清扫动画尝试隐藏抹布时的事件入口，当前没有独立抹布表现所以留空。
        /// </summary>
        public void HideCloth()
        {
        }
    }
}
