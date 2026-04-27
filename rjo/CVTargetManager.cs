using System;
using System.Collections.Generic;
using UnityEngine;

namespace CVatGPT
{
    /// <summary>
    /// Manages runtime tracked targets, primary/secondary slots, gyro fallback, and smoothing.
    /// Fully C#; no NativeCVWrapper dependency.
    /// </summary>
    public class CVTargetManager : MonoBehaviour
    {
        [Header("Target Settings")]
        public int minPointsToUnlock = 8;
        public int maxPointsExpected = 100;
        public float outlineFidelity = 0.9f;

        [Header("Gyro / Dead Reckoning")]
        public GameObject gyroTrackingGO;
        public float gyroFallbackDuration = 2f;
        [Range(0f, 1f)] public float smoothingLerp = 0.6f;

        [Header("Primary / Secondary Targets")]
        public CVTrackedTarget primaryTarget;
        public CVTrackedTarget secondaryTarget;

        [Header("Debug / Status")]
        public bool verbose = true;

        [Header("Fallback Options")]
        public bool allowGyroFallback = false;   // default OFF

        private float gyroTimer = 0f;
        private bool gyroActive = false;
        private readonly List<CVTrackedTarget> potentialTargets = new();

        void Awake()
        {
            gyroTrackingGO?.SetActive(false);

            if (verbose)
                Debug.Log("[CVTargetManager] Awake: gyroTrackingGO disabled, allowGyroFallback=" + allowGyroFallback);
        }

        void Update()
        {
            if (verbose)
            {
                string primaryName = primaryTarget != null ? primaryTarget.name : "null";
                string secondaryName = secondaryTarget != null ? secondaryTarget.name : "null";
                Debug.Log($"[CVTargetManager] Update: potentialTargets={potentialTargets.Count}, primary={primaryName}, secondary={secondaryName}");
            }

            UpdateTrackedTargets();
            HandleUnlocking();
            HandleGyroFallback();
        }

        /// <summary>
        /// Update all potential targets via CVTrackingManager
        /// </summary>
        void UpdateTrackedTargets()
        {
            foreach (var target in potentialTargets)
            {
                if (verbose)
                    Debug.Log($"[CVTargetManager] Querying pose for target '{target.name}'");

                if (CVTrackingManager.TryGetTrackedPose(target.name, out CVPose pose) && pose.IsValid)
                {
                    target.lastPose = pose;
                    target.pointsDetected = pose.Keypoints != null ? pose.Keypoints.Count : 0;
                    target.isVisible = true;
                    target.lastSeenTime = Time.time;

                    if (verbose)
                        Debug.Log($"[CVTargetManager] Pose OK for {target.name}: points={target.pointsDetected}, time={target.lastSeenTime}");
                }
                else
                {
                    if (target.isVisible && verbose)
                        Debug.Log($"[CVTargetManager] LOST {target.name} (no valid pose)");

                    target.isVisible = false;
                    target.pointsDetected = 0;

                    if (verbose)
                        Debug.Log($"[CVTargetManager] No pose for {target.name} this frame");
                }
            }
        }

        void HandleUnlocking()
        {
            foreach (var target in potentialTargets)
            {
                if (!target.isUnlocked &&
                    target.isVisible &&
                    target.pointsDetected >= minPointsToUnlock &&
                    target.pointsDetected <= maxPointsExpected)
                {
                    target.isUnlocked = true;

                    if (primaryTarget == null)
                    {
                        primaryTarget = target;
                        if (verbose)
                            Debug.Log($"[CVTargetManager] PRIMARY target unlocked: {target.name}");
                    }
                    else if (secondaryTarget == null)
                    {
                        secondaryTarget = target;
                        if (verbose)
                            Debug.Log($"[CVTargetManager] SECONDARY target unlocked: {target.name}");
                    }
                }
                else if (verbose && target.isVisible)
                {
                    Debug.Log($"[CVTargetManager] Visible but not unlocked: {target.name}, points={target.pointsDetected}");
                }
            }
        }

        /// <summary>
        /// Gyro fallback is now OPTIONAL. If disabled, we never enter fallback mode.
        /// </summary>
        void HandleGyroFallback()
        {
            // Hard-disable gyro fallback unless explicitly enabled
            if (!allowGyroFallback)
            {
                if (gyroActive && verbose)
                    Debug.Log("[CVTargetManager] Gyro fallback disabled; forcing OFF");

                gyroActive = false;
                gyroTimer = 0f;
                gyroTrackingGO?.SetActive(false);
                return;
            }

            bool primaryLost =
                primaryTarget == null ||
                !primaryTarget.isVisible ||
                primaryTarget.pointsDetected < minPointsToUnlock * outlineFidelity;

            if (primaryLost)
            {
                gyroActive = true;
                gyroTimer += Time.deltaTime;
                gyroTrackingGO?.SetActive(true);

                if (verbose)
                    Debug.Log("[CVTargetManager] Gyro fallback active");

                if (gyroTimer >= gyroFallbackDuration)
                {
                    primaryTarget = null;
                    secondaryTarget = null;
                    gyroTimer = 0f;

                    if (verbose)
                        Debug.Log("[CVTargetManager] Gyro timeout, targets cleared");
                }
            }
            else
            {
                if (gyroActive && verbose)
                    Debug.Log("[CVTargetManager] Primary recovered; gyro fallback OFF");

                gyroActive = false;
                gyroTimer = 0f;
                gyroTrackingGO?.SetActive(false);
            }
        }

        /// <summary>
        /// Registers a new target provided dynamically by the DynamicImageLoader.
        /// </summary>
        public void RegisterDynamicTarget(CVTrackedTarget target)
        {
            if (!potentialTargets.Exists(t => t.name == target.name))
            {
                potentialTargets.Add(target);
                if (verbose)
                    Debug.Log($"[CVTargetManager] Registered dynamic target: {target.name}. Total now: {potentialTargets.Count}");
            }
            else if (verbose)
            {
                Debug.Log($"[CVTargetManager] Dynamic target already registered: {target.name}");
            }
        }

        /// <summary>
        /// Unregisters a target by name.
        /// </summary>
        public void UnregisterTarget(string name)
        {
            potentialTargets.RemoveAll(t => t.name == name);

            if (primaryTarget?.name == name) primaryTarget = null;
            if (secondaryTarget?.name == name) secondaryTarget = null;

            if (verbose)
                Debug.Log($"[CVTargetManager] Unregistered target: {name}");
        }
    }

    [Serializable]
    public class CVTrackedTarget
    {
        public string name;
        public bool isUnlocked;
        public bool isVisible;
        public int pointsDetected;
        public CVPose lastPose;
        public float lastSeenTime;

        public CVTrackedTarget(string name)
        {
            this.name = name;
            lastPose = CVPose.Identity;
        }
    }
}
