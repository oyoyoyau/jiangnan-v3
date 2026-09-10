using System;
using System.Collections;
using JN.Client.Manager;
using JN.Client.UI;
using QFramework;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace JN.Client.Scene
{
    public partial class TavernSceneManager
    {
        #region Guide Delivery

        /// <summary>
        /// 尝试从门口播放购买物件的搬运表现。
        /// 目标优先用建筑本体，缺失时回退到建造底座，保证前台/灶台也能播搬运。
        /// </summary>
        /// <param name="target">目标对象。</param>
        /// <param name="carrierPrefab">参数值。</param>
        /// <param name="on到达">参数值。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private bool TryPlayGuideDeliveryEffect(Transform target, GameObject carrierPrefab, Action onArrived)
        {
            if (target == null)
            {
                return false;
            }

            if (carrierPrefab == null)
            {
                return false;
            }

            var spawnPoint = objectMovePoint != null
                ? objectMovePoint
                : FindSceneTransformByName("ObjectMovePoint")
                  ?? FindSceneTransformByName("PeopleStartPoint")
                  ?? FindSceneTransformByName("TableMoveCheckPoint");
            var spawnPos = spawnPoint != null ? spawnPoint.position : target.position + target.right * 2.2f;
            var spawnRot = spawnPoint != null ? spawnPoint.rotation : target.rotation;
            var isSceneCarrier = carrierPrefab.scene.IsValid();
            // 场景预置搬运物正在播放时复制一份，允许多次购买并行搬运。
            var reuseSceneCarrier = isSceneCarrier && !carrierPrefab.activeInHierarchy;
            var carrier = reuseSceneCarrier
                ? carrierPrefab
                : Instantiate(carrierPrefab, spawnPos, spawnRot);
            PrepareGuideCarrierForManualDelivery(carrier);
            carrier.transform.SetPositionAndRotation(spawnPos, spawnRot);
            carrier.SetActive(true);
            FacilityBuildVisualUtility.ApplyBuiltState(carrier);
            StartCoroutine(GuideDeliveryRoutine(
                carrier,
                target.position,
                ResolveGuideDeliveryEffectPosition(target),
                onArrived,
                !reuseSceneCarrier));
            return true;
        }

        /// <summary>
        /// 尝试播放引导搬运：目标缺失时用备用落点。
        /// </summary>
        private bool TryPlayGuideDeliveryEffect(Transform target, Transform fallbackTarget, GameObject carrierPrefab, Action onArrived)
        {
            return TryPlayGuideDeliveryEffect(target != null ? target : fallbackTarget, carrierPrefab, onArrived);
        }

        /// <summary>
        /// 驱动搬运物件以世界坐标直线移动到目标点。
        /// 表现与落点是否在 NavMesh 上无关（前台/灶台等常不在可行走网格上）。
        /// </summary>
        /// <param name="carrier">参数值。</param>
        /// <param name="targetPosition">目标对象。</param>
        /// <param name="on到达">参数值。</param>
        /// <param name="destroyOnArrive">参数值。</param>
        /// <returns>协程迭代器。</returns>
        private IEnumerator GuideDeliveryRoutine(GameObject carrier, Vector3 targetPosition, Vector3 effectPosition, Action onArrived, bool destroyOnArrive)
        {
            if (carrier == null)
            {
                onArrived?.Invoke();
                yield break;
            }

            var carrierTransform = carrier.transform;
            var agent = carrier.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.enabled = false;
            }

            // 仅做视觉搬运：门口 → 建筑落点，不采样、不贴合 NavMesh。
            var corners = new[] { carrierTransform.position, targetPosition };
            var animators = carrier.GetComponentsInChildren<Animator>(true);
            var moveSpeed = 1.05f;
            var arriveDistance = 0.08f;
            var maxWaitTime = 15f;
            var waitTime = 0f;

            for (var cornerIndex = 1; cornerIndex < corners.Length; cornerIndex++)
            {
                var corner = corners[cornerIndex];
                while (carrier != null && Vector3.Distance(carrierTransform.position, corner) > arriveDistance)
                {
                    waitTime += Time.deltaTime;
                    if (waitTime >= maxWaitTime)
                    {
                        carrierTransform.position = targetPosition;
                        UpdateGuideCarrierAnimators(animators, 0f);
                        FinalizeGuideDelivery(carrier, effectPosition, onArrived, destroyOnArrive);
                        yield break;
                    }

                    var currentPosition = carrierTransform.position;
                    var nextPosition = Vector3.MoveTowards(currentPosition, corner, moveSpeed * Time.deltaTime);
                    var delta = nextPosition - currentPosition;
                    carrierTransform.position = nextPosition;
                    if (delta.sqrMagnitude > 0.000001f)
                    {
                        var flatDelta = new Vector3(delta.x, 0f, delta.z);
                        if (flatDelta.sqrMagnitude > 0.000001f)
                        {
                            var lookRotation = Quaternion.LookRotation(flatDelta.normalized, Vector3.up);
                            carrierTransform.rotation = Quaternion.RotateTowards(
                                carrierTransform.rotation, lookRotation, 540f * Time.deltaTime);
                        }
                    }

                    UpdateGuideCarrierAnimators(animators, delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f));
                    yield return null;
                }
            }

            UpdateGuideCarrierAnimators(animators, 0f);
            carrierTransform.position = targetPosition;
            FinalizeGuideDelivery(carrier, effectPosition, onArrived, destroyOnArrive);
        }

        /// <summary>
        /// 根据目标建筑包围盒计算建造完成特效播放位置。
        /// </summary>
        /// <param name="target">目标对象。</param>
        /// <returns>返回方法执行后的结果。</returns>
        private static Vector3 ResolveGuideDeliveryEffectPosition(Transform target)
        {
            if (target == null)
            {
                return Vector3.zero;
            }

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return target.position + Vector3.up * 0.8f;
            }

            var hasBounds = false;
            var bounds = new Bounds(target.position, Vector3.zero);
            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            return hasBounds
                ? new Vector3(bounds.center.x, bounds.max.y + 0.25f, bounds.center.z)
                : target.position + Vector3.up * 0.8f;
        }

        /// <summary>
        /// 完成搬运后回收表现并执行到达回调。
        /// </summary>
        /// <param name="carrier">参数值。</param>
        /// <param name="effectPosition">坐标。</param>
        /// <param name="on到达">参数值。</param>
        /// <param name="destroyOnArrive">参数值。</param>
        private static void FinalizeGuideDelivery(GameObject carrier, Vector3 effectPosition, Action onArrived, bool destroyOnArrive)
        {
            PlayGuideBuildingSuccessEffect(effectPosition);

            if (carrier != null)
            {
                if (destroyOnArrive)
                {
                    Destroy(carrier);
                }
                else
                {
                    carrier.SetActive(false);
                }
            }

            onArrived?.Invoke();
        }

        /// <summary>
        /// 在目标家具包围盒顶部播放建造完成特效（与一楼搬运到位相同）。
        /// </summary>
        public static void PlayGuideBuildingSuccessEffectAt(Transform target, bool playAudio = true)
        {
            if (target == null)
            {
                return;
            }

            PlayGuideBuildingSuccessEffect(ResolveGuideDeliveryEffectPosition(target), playAudio);
        }

        /// <summary>
        /// 在指定世界坐标播放建造完成特效（UIEffect_BuildingSuccess）。
        /// 家具搬运到位、开局酒楼落成共用。
        /// </summary>
        /// <param name="worldPosition">世界坐标。</param>
        /// <param name="playAudio">是否播放落成音效；卸客等场景可关。</param>
        /// <param name="effectParent">可选 UI 父节点；空则用酒楼 canvas，再没有则直接放世界坐标。</param>
        public static void PlayGuideBuildingSuccessEffect(
            Vector3 worldPosition,
            bool playAudio = true,
            Transform effectParent = null)
        {
            if (playAudio)
            {
                GameAudioManager.PlayBuildPutDown();
            }

            var effectPrefab = LoadGuideBuildingSuccessEffectPrefab();
            if (effectPrefab == null)
            {
                return;
            }

            effectParent ??= Instance != null ? Instance.canvasParent : null;
            if (effectParent == null)
            {
                var hud = UIKit.GetPanel<TavernWorldRuntimeHudPanelController>();
                effectParent = hud != null ? hud.transform : null;
            }
            var effect = effectParent != null
                ? Instantiate(effectPrefab, effectParent)
                : Instantiate(effectPrefab, worldPosition, Quaternion.identity);
            if (effect == null)
            {
                return;
            }

            effect.name = "UIEffect_BuildingSuccess_Runtime";
            effect.transform.localScale = Vector3.one;
            effect.transform.localRotation = Quaternion.identity;
            if (effectParent != null)
            {
                var effectRect = effect.transform as RectTransform;
            var sceneCamera = Instance != null && Instance.SceneCamera != null
                ? Instance.SceneCamera
                : TileManager.Instance != null && TileManager.Instance.SceneCamera != null
                    ? TileManager.Instance.SceneCamera
                    : Camera.main;
                var screenPosition = sceneCamera != null ? sceneCamera.WorldToScreenPoint(worldPosition) : worldPosition;
                if (effectRect != null)
                {
                    effectRect.position = screenPosition;
                    effectRect.anchoredPosition3D = new Vector3(effectRect.anchoredPosition3D.x, effectRect.anchoredPosition3D.y, -50f);
                }
                else
                {
                    effect.transform.position = screenPosition;
                }
            }
            else
            {
                effect.transform.position = worldPosition;
            }

            effect.SetActive(true);

            foreach (var child in effect.GetComponentsInChildren<Transform>(true))
            {
                if (child.localScale == Vector3.zero)
                {
                    child.localScale = Vector3.one;
                }
            }

            foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                particle.gameObject.SetActive(true);
                particle.Clear(true);
                particle.Play(true);
            }

            Destroy(effect, 3f);
        }

        /// <summary>
        /// 查找场景预置搬运物或加载搬运 预制体。
        /// </summary>
        /// <param name="assetPath">资源路径。</param>
        /// <param name="sceneObjectName">名称。</param>
        /// <returns>返回匹配到的对象引用。</returns>
        private GameObject ResolveGuideCarrier(string assetPath, string sceneObjectName)
        {
            var sceneCarrier = FindChildGameObjectByName(objectMovePoint, sceneObjectName)
                               ?? FindSceneGameObjectByName(sceneObjectName);
            if (sceneCarrier != null)
            {
                return sceneCarrier;
            }

            return LoadGuideCarrierPrefab(assetPath);
        }

        /// <summary>
        /// 隐藏场景中预放的搬运表现物。
        /// </summary>
        /// <param name="carrierName">名称。</param>
        private void HideGuideSceneCarrier(string carrierName)
        {
            var carrier = FindChildGameObjectByName(objectMovePoint, carrierName)
                          ?? FindSceneGameObjectByName(carrierName);
            if (carrier == null || !carrier.scene.IsValid())
            {
                return;
            }

            PrepareGuideCarrierForManualDelivery(carrier);
            carrier.SetActive(false);
        }

        /// <summary>
        /// 关闭搬运表现上的自动移动和阻挡组件。
        /// </summary>
        /// <param name="carrier">参数值。</param>
        private static void PrepareGuideCarrierForManualDelivery(GameObject carrier)
        {
            if (carrier == null)
            {
                return;
            }

            PrepareMovePrefabForManualMovement(carrier);
            foreach (var obstacle in carrier.GetComponentsInChildren<NavMeshObstacle>(true))
            {
                obstacle.enabled = false;
            }

            foreach (var moveSignal in carrier.GetComponentsInChildren<MoveRotateSignal>(true))
            {
                moveSignal.enabled = false;
            }
        }

        /// <summary>
        /// 根据搬运速度同步搬运工动画参数。
        /// </summary>
        /// <param name="animators">参数值。</param>
        /// <param name="speed">参数值。</param>
        private static void UpdateGuideCarrierAnimators(Animator[] animators, float speed)
        {
            if (animators == null)
            {
                return;
            }

            for (var index = 0; index < animators.Length; index++)
            {
                var animator = animators[index];
                if (animator == null)
                {
                    continue;
                }

                if (HasAnimatorParameter(animator, "Speed", AnimatorControllerParameterType.Float))
                {
                    animator.SetFloat("Speed", speed);
                }

                if (HasAnimatorParameter(animator, "Move", AnimatorControllerParameterType.Bool))
                {
                    animator.SetBool("Move", speed > 0.05f);
                }

                if (HasAnimatorParameter(animator, "Walk", AnimatorControllerParameterType.Bool))
                {
                    animator.SetBool("Walk", speed > 0.05f);
                }
            }
        }

        /// <summary>
        /// 判断 动画器 是否包含指定参数。
        /// </summary>
        /// <param name="animator">参数值。</param>
        /// <param name="parameterName">名称。</param>
        /// <param name="parameterType">参数值。</param>
        /// <returns>满足条件时返回 真，否则返回 假。</returns>
        private static bool HasAnimatorParameter(Animator animator, string parameterName, AnimatorControllerParameterType parameterType)
        {
            if (animator == null || string.IsNullOrEmpty(parameterName))
            {
                return false;
            }

            var parameters = animator.parameters;
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                if (parameter.name == parameterName && parameter.type == parameterType)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 按资源路径加载并缓存搬运 预制体。
        /// </summary>
        /// <param name="assetPath">资源路径。</param>
        /// <returns>返回匹配到的对象引用。</returns>
        private static GameObject LoadGuideCarrierPrefab(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            if (GuideCarrierPrefabCache.TryGetValue(assetPath, out var cachedPrefab) && cachedPrefab != null)
            {
                return cachedPrefab;
            }

            var prefab = GameplayResourceStore.LoadAsset<GameObject>(assetPath);
            GuideCarrierPrefabCache[assetPath] = prefab;
            return prefab;
        }

        /// <summary>
        /// 加载并缓存建筑完成特效预制体。
        /// </summary>
        /// <returns>返回匹配到的对象引用。</returns>
        private static GameObject LoadGuideBuildingSuccessEffectPrefab()
        {
            if (guideBuildingSuccessEffectPrefab != null)
            {
                return guideBuildingSuccessEffectPrefab;
            }

            guideBuildingSuccessEffectPrefab = GameplayResourceStore.LoadAsset<GameObject>(GuideBuildingSuccessEffectPrefabPath);
            return guideBuildingSuccessEffectPrefab;
        }

        /// <summary>
        /// 关闭搬运预制体内部的导航代理。
        /// </summary>
        /// <param name="tableMovePrefab">桌位对象。</param>
        private static void PrepareMovePrefabForManualMovement(GameObject tableMovePrefab)
        {
            foreach (var navMeshAgent in tableMovePrefab.GetComponentsInChildren<NavMeshAgent>(true))
            {
                navMeshAgent.enabled = false;
            }
        }

        #endregion
    }

    /// <summary>
    /// 可建造设施的 LoutiCustom 材质状态：半透预览 / 建造完成。
    /// 不改 Shader，通过材质实例写入 _Alpha / _OutlineWidth。
    /// </summary>
    public static class FacilityBuildVisualUtility
    {
        public const float PreviewAlpha = 0.5f;
        /// <summary>未解锁/未建造预览：不描边（材质默认常为 0.03，运行时强制清 0）。</summary>
        public const float PreviewOutlineWidth = 0f;
        public const float BuiltAlpha = 1f;
        public const float BuiltOutlineWidth = 0f;

        private static readonly int AlphaPropertyId = Shader.PropertyToID("_Alpha");
        private static readonly int OutlineWidthPropertyId = Shader.PropertyToID("_OutlineWidth");

        /// <summary>
        /// 未建造预览：半透明、无描边。
        /// </summary>
        /// <param name="includeChildren">为 false 时只改 root 自身 Renderer（如轿子不改轿夫子节点）。</param>
        public static void ApplyPreviewState(GameObject root, bool includeChildren = true)
        {
            ApplyState(root, PreviewAlpha, PreviewOutlineWidth, includeChildren);
        }

        /// <summary>
        /// 建造完成（含搬运途中的设施）：不透明、无描边。
        /// </summary>
        /// <param name="includeChildren">为 false 时只改 root 自身 Renderer。</param>
        public static void ApplyBuiltState(GameObject root, bool includeChildren = true)
        {
            ApplyState(root, BuiltAlpha, BuiltOutlineWidth, includeChildren);
        }

        private static void ApplyState(GameObject root, float alpha, float outlineWidth, bool includeChildren)
        {
            if (root == null)
            {
                return;
            }

            var renderers = includeChildren
                ? root.GetComponentsInChildren<Renderer>(true)
                : root.GetComponents<Renderer>();
            for (var i = 0; i < renderers.Length; i++)
            {
                ApplyState(renderers[i], alpha, outlineWidth);
            }
        }

        private static void ApplyState(Renderer renderer, float alpha, float outlineWidth)
        {
            if (renderer == null)
            {
                return;
            }

            var sharedMaterials = renderer.sharedMaterials;
            if (sharedMaterials == null || sharedMaterials.Length == 0)
            {
                return;
            }

            var hasTargetProperty = false;
            for (var i = 0; i < sharedMaterials.Length; i++)
            {
                if (sharedMaterials[i] != null && sharedMaterials[i].HasProperty(AlphaPropertyId))
                {
                    hasTargetProperty = true;
                    break;
                }
            }

            if (!hasTargetProperty)
            {
                return;
            }

            var materials = renderer.materials;
            for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                var material = materials[materialIndex];
                if (material == null || !material.HasProperty(AlphaPropertyId))
                {
                    continue;
                }

                material.SetFloat(AlphaPropertyId, alpha);
                if (material.HasProperty(OutlineWidthPropertyId))
                {
                    material.SetFloat(OutlineWidthPropertyId, outlineWidth);
                }
            }
        }
    }
}
