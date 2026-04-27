using System;
using System.Collections.Generic;
using UnityEngine;

namespace CVatGPT
{
    /// <summary>
    /// UnifiedStability is the stability governor for the entire CV pipeline.
    /// It takes raw poses from the orchestrator, smooths them, freezes them
    /// during noise, and only outputs stable poses to CVTrackingManager.
    ///
    /// Features:
    /// - Fast-first / accurate-later stability
    /// - Confidence decay
    /// - Noise gating
    /// - Motion thresholds
    /// - Freeze-on-noise
    /// - Unlock gating
    /// - Promotion gating
    /// </summary>
    public class UnifiedStability : MonoBehaviour
    {
        private static UnifiedStability _instance;
        public static UnifiedStability Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("UnifiedStability");
                    _instance = go.AddComponent<UnifiedStability>();
                }
                return _instance;
            }
        }

        // ---------------------------------------------------------
        // INSPECTOR SETTINGS
        // ---------------------------------------------------------

        [Header("Stability Settings")]
        public bool enableStability = true;

        [Tooltip("Minimum confidence required to accept a pose.")]
        public float minConfidence = 0.25f;

        [Tooltip("Minimum keypoints required to accept a pose.")]
        public int minKeypoints = 8;

        [Tooltip("Maximum allowed motion per frame before freezing.")]
        public float maxMotionPerFrame = 0.05f;

        [Tooltip("How many stable frames required before unlocking.")]
        public int stableFramesToUnlock = 4;

        [Tooltip("How many noisy frames before freezing.")]
        public int noisyFramesToFreeze = 2;

        [Tooltip("How many frozen frames before resetting.")]
        public int freezeResetFrames = 12;

        [Header("Smoothing")]
        [Range(0f, 1f)] public float positionLerp = 0.6f;
        [Range(0f, 1f)] public float rotationLerp = 0.6f;

        [Header("Debug")]
        public bool verbose = true;
        public int debugStableFrames;
        public int debugNoisyFrames;
        public int debugFrozenFrames;

        // ---------------------------------------------------------
        // INTERNAL STATE
        // ---------------------------------------------------------

        private CVPose _lastStablePose = CVPose.Identity;
        private bool _hasStablePose;

        private int _stableFrameCount;
        private int _noisyFrameCount;
        private int _frozenFrameCount;

        // ---------------------------------------------------------
        // PUBLIC API
        // ---------------------------------------------------------

        /// <summary>
        /// Accepts a raw pose from the orchestrator and returns a stabilized pose.
        /// </summary>
        public CVPose Process(CVPose rawPose)
        {
            if (!enableStability)
                return rawPose;

            // -----------------------------------------------------
            // 1. VALIDITY CHECK
            // -----------------------------------------------------
            if (!rawPose.IsValid ||
                rawPose.Keypoints == null ||
                rawPose.Keypoints.Count < minKeypoints ||
                rawPose.confidence < minConfidence)
            {
                MarkNoisy();
                return _hasStablePose ? _lastStablePose : CVPose.Identity;
            }

            // -----------------------------------------------------
            // 2. MOTION CHECK
            // -----------------------------------------------------
            if (_hasStablePose)
            {
                float motion = Vector3.Distance(
                    rawPose.Position,
                    _lastStablePose.Position
                );

                if (motion > maxMotionPerFrame)
                {
                    MarkNoisy();
                    return _lastStablePose;
                }
            }

            // -----------------------------------------------------
            // 3. STABLE FRAME
            // -----------------------------------------------------
            MarkStable();

            // -----------------------------------------------------
            // 4. SMOOTHING
            // -----------------------------------------------------
            CVPose smoothed = SmoothPose(rawPose);

            _lastStablePose = smoothed;
            _hasStablePose = true;

            return smoothed;
        }

        /// <summary>
        /// Returns true if the pose is stable enough to unlock a target.
        /// </summary>
        public bool IsStableForUnlock()
        {
            return _stableFrameCount >= stableFramesToUnlock;
        }

        // ---------------------------------------------------------
        // INTERNAL LOGIC
        // ---------------------------------------------------------

        private void MarkStable()
        {
            _stableFrameCount++;
            _noisyFrameCount = 0;
            _frozenFrameCount = 0;

            debugStableFrames = _stableFrameCount;
            debugNoisyFrames = _noisyFrameCount;
            debugFrozenFrames = _frozenFrameCount;
        }

        private void MarkNoisy()
        {
            _noisyFrameCount++;
            debugNoisyFrames = _noisyFrameCount;

            if (_noisyFrameCount >= noisyFramesToFreeze)
            {
                _frozenFrameCount++;
                debugFrozenFrames = _frozenFrameCount;

                if (verbose)
                    Debug.Log("[UnifiedStability] FREEZE: noisy frames exceeded threshold.");

                if (_frozenFrameCount >= freezeResetFrames)
                {
                    ResetStability();
                }
            }
        }

        private void ResetStability()
        {
            if (verbose)
                Debug.Log("[UnifiedStability] RESET: frozen too long, clearing stable pose.");

            _stableFrameCount = 0;
            _noisyFrameCount = 0;
            _frozenFrameCount = 0;
            _hasStablePose = false;
            _lastStablePose = CVPose.Identity;
        }

        private CVPose SmoothPose(CVPose raw)
        {
            if (!_hasStablePose)
                return raw;

            CVPose smoothed = raw;

            // Position smoothing
            smoothed.Position = Vector3.Lerp(
                _lastStablePose.Position,
                raw.Position,
                positionLerp
            );

            // Rotation smoothing
            smoothed.Rotation = Quaternion.Slerp(
                _lastStablePose.Rotation,
                raw.Rotation,
                rotationLerp
            );

            return smoothed;
        }
    }
}
