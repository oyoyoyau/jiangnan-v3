using System.Collections.Generic;
using cfg;
using JN.Client;
using UnityEngine;

namespace JN.Client.Scene
{
    public partial class TavernSceneManager
    {
        private const string TechUnlockEffectRootPath = "Assets/Res/Resources/Effect/";
        private const float TechUnlockEffectFallbackLifetime = 3f;
        private static readonly Vector3 TechUnlockFootEffectRotation = new(90f, 0f, 0f);
        private const float TechUnlockFootEffectScale = 0.4f;

        private static readonly Dictionary<string, GameObject> TechUnlockEffectPrefabCache = new();

        /// <summary>
        /// 科技解锁时，在对应职位员工脚底播放配表 effName 特效，播完后销毁。
        /// </summary>
        public void PlayTechUnlockStaffFootEffect(TavernTech tech)
        {
            if (tech == null || string.IsNullOrWhiteSpace(tech.EffName) || !tech.StaffPosition.HasValue)
            {
                return;
            }

            var prefab = LoadTechUnlockEffectPrefab(tech.EffName.Trim());
            if (prefab == null)
            {
                return;
            }

            var staffTargets = ResolveTechUnlockStaffTargets(tech.StaffPosition.Value);
            if (staffTargets.Count == 0)
            {
                return;
            }

            var effName = tech.EffName.Trim();
            for (var index = 0; index < staffTargets.Count; index++)
            {
                var staffTransform = staffTargets[index];
                if (staffTransform == null)
                {
                    continue;
                }

                PlayTechUnlockFootEffectAtStaff(staffTransform, prefab, effName);
            }
        }

        private List<Transform> ResolveTechUnlockStaffTargets(StaffPosition position)
        {
            var targets = new List<Transform>();
            var visualKey = ResolveStaffVisualKey(position);
            if (string.IsNullOrEmpty(visualKey))
            {
                return targets;
            }

            var visuals = GetGuideStaffVisuals(visualKey);
            for (var index = 0; index < visuals.Length; index++)
            {
                var staff = visuals[index];
                if (staff != null && staff.activeInHierarchy)
                {
                    targets.Add(staff.transform);
                }
            }

            if (targets.Count == 0
                && guideStaffVisuals.TryGetValue(visualKey, out var primaryStaff)
                && primaryStaff != null
                && primaryStaff.activeInHierarchy)
            {
                targets.Add(primaryStaff.transform);
            }

            if (targets.Count == 0)
            {
                AppendTechUnlockStaffMarkerTarget(position, targets);
            }

            return targets;
        }

        private void AppendTechUnlockStaffMarkerTarget(StaffPosition position, List<Transform> targets)
        {
            Transform marker = null;
            switch (position)
            {
                case StaffPosition.Shopkeeper:
                    marker = FindSceneTransformByName(GuideShopkeeperMarkerName)
                             ?? FindSceneTransformByName("WaiterF1");
                    break;
                case StaffPosition.Chef:
                    marker = FindSceneTransformByName(GuideChefMarkerName)
                             ?? FindSceneTransformByName("Chef3");
                    break;
                case StaffPosition.Waiter:
                    marker = FindSceneTransformByName(GuideWaiterMarkerName)
                             ?? FindSceneTransformByName("WaiterF1_1");
                    break;
            }

            if (marker != null)
            {
                targets.Add(marker);
            }
        }

        private static string ResolveStaffVisualKey(StaffPosition position)
        {
            return position switch
            {
                StaffPosition.Shopkeeper => GuideShopkeeperVisualKey,
                StaffPosition.Chef => GuideChefVisualKey,
                StaffPosition.Waiter => GuideWaiterVisualKey,
                _ => null
            };
        }

        private static GameObject LoadTechUnlockEffectPrefab(string effName)
        {
            if (string.IsNullOrWhiteSpace(effName))
            {
                return null;
            }

            if (TechUnlockEffectPrefabCache.TryGetValue(effName, out var cached) && cached != null)
            {
                return cached;
            }

            var path = $"{TechUnlockEffectRootPath}{effName}.prefab";
            var prefab = GameplayResourceStore.LoadAsset<GameObject>(path);
            if (prefab != null)
            {
                TechUnlockEffectPrefabCache[effName] = prefab;
            }

            return prefab;
        }

        private static void PlayTechUnlockFootEffectAtStaff(Transform staffTransform, GameObject prefab, string effName)
        {
            if (staffTransform == null || prefab == null)
            {
                return;
            }

            var effect = Instantiate(
                prefab,
                ResolveStaffFootWorldPosition(staffTransform),
                Quaternion.Euler(TechUnlockFootEffectRotation));
            if (effect == null)
            {
                return;
            }

            effect.name = $"{effName}_TechUnlockRuntime";
            effect.transform.localScale = Vector3.one * TechUnlockFootEffectScale;
            effect.SetActive(true);

            foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                particle.gameObject.SetActive(true);
                particle.Clear(true);
                particle.Play(true);
            }

            Destroy(effect, ResolveEffectLifetime(effect, TechUnlockEffectFallbackLifetime));
        }

        private static Vector3 ResolveStaffFootWorldPosition(Transform staffTransform)
        {
            var renderers = staffTransform.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var index = 1; index < renderers.Length; index++)
                {
                    bounds.Encapsulate(renderers[index].bounds);
                }

                return new Vector3(staffTransform.position.x, bounds.min.y, staffTransform.position.z);
            }

            return staffTransform.position;
        }

        private static float ResolveEffectLifetime(GameObject effect, float fallbackLifetime)
        {
            if (effect == null)
            {
                return fallbackLifetime;
            }

            var maxLifetime = 0f;
            foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particle.main;
                var startLifetime = main.startLifetime.mode switch
                {
                    ParticleSystemCurveMode.Constant => main.startLifetime.constant,
                    ParticleSystemCurveMode.TwoConstants => main.startLifetime.constantMax,
                    _ => main.startLifetime.constantMax
                };
                maxLifetime = Mathf.Max(maxLifetime, main.duration + startLifetime);
            }

            return maxLifetime > 0f ? maxLifetime : fallbackLifetime;
        }
    }
}
