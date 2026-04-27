using UnityEngine;
using System.Collections.Generic;

namespace CVatGPT
{
    /// <summary>
    /// Autonomously spawns and maintains one prefab instance per CV image target.
    /// </summary>
    public class CVImageTargetInstanceManager : MonoBehaviour
    {
        [Header("Prefab")]
        public GameObject imageTargetPrefab;

        [Header("Control")]
        public bool enableInstances = true;
        public bool hideWhenNotTracked = true;

        private readonly Dictionary<string, GameObject> _instances = new();
        private readonly Dictionary<string, Vector2> _sizes = new();

        void Update()
        {
            if (!enableInstances || imageTargetPrefab == null)
                return;

            foreach (string name in CVTrackingManager.TrackedNames)
            {
                if (!CVTrackingManager.TryGetTrackedPose(name, out CVPose pose) || !pose.IsValid)
                {
                    if (hideWhenNotTracked &&
                        _instances.TryGetValue(name, out var inst) &&
                        inst != null)
                        inst.SetActive(false);

                    continue;
                }

                if (!_instances.TryGetValue(name, out GameObject go) || go == null)
                {
                    go = Instantiate(imageTargetPrefab, Vector3.zero, Quaternion.identity, transform);
                    go.name = $"CVImageTarget_{name}";
                    _instances[name] = go;

                    var def = CVTargetRegistry.Get(name);
                    if (def != null && def.referenceImage != null)
                    {
                        var renderer = go.GetComponentInChildren<Renderer>();
                        if (renderer != null && renderer.material != null)
                            renderer.material.mainTexture = def.referenceImage;
                    }

                    if (_sizes.TryGetValue(name, out Vector2 size))
                    {
                        go.transform.localScale = new Vector3(size.x, size.y, 1f);
                    }
                }

                go.SetActive(true);
                go.transform.position = new Vector3(pose.px, pose.py, pose.pz);
                go.transform.rotation = pose.Rotation;
            }
        }

        public void RegisterPhysicalSize(string targetName, float widthMeters, float heightMeters)
        {
            _sizes[targetName] = new Vector2(widthMeters, heightMeters);

            if (_instances.TryGetValue(targetName, out GameObject go) && go != null)
            {
                go.transform.localScale = new Vector3(widthMeters, heightMeters, 1f);
            }
        }
    }
}
