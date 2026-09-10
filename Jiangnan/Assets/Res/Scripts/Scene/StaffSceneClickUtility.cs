using UnityEngine;

namespace JN.Client.Scene
{
    /// <summary>
    /// 为场景内员工模型补齐点击射线所需的碰撞体。
    /// </summary>
    internal static class StaffSceneClickUtility
    {
        private static readonly Vector3 DefaultColliderCenter = new(0f, 1f, 0f);
        private const float DefaultColliderRadius = 0.38f;
        private const float DefaultColliderHeight = 1.85f;

        public static void EnsureClickCollider(GameObject staffRoot)
        {
            if (staffRoot == null)
            {
                return;
            }

            var capsule = staffRoot.GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                capsule = staffRoot.AddComponent<CapsuleCollider>();
                capsule.center = DefaultColliderCenter;
                capsule.radius = DefaultColliderRadius;
                capsule.height = DefaultColliderHeight;
                capsule.direction = 1;
            }

            capsule.enabled = true;
            capsule.isTrigger = false;
        }

        public static int ResolveStaffId(Collider hitCollider)
        {
            if (hitCollider == null)
            {
                return 0;
            }

            var waiter = hitCollider.GetComponentInParent<WaiterCharacter>();
            if (waiter != null && waiter.StaffId > 0)
            {
                return waiter.StaffId;
            }

            var chef = hitCollider.GetComponentInParent<ChefCharacter>();
            if (chef != null && chef.StaffId > 0)
            {
                return chef.StaffId;
            }

            return 0;
        }
    }
}
