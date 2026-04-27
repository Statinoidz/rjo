using System.Collections.Generic;
using UnityEngine;

namespace CVatGPT
{
    public static class CVTrackingManager
    {
        public static readonly List<string> TrackedNames = new();

        public static CVPose PrimaryPose { get; set; } = CVPose.Identity;
        public static int FrameIndex { get; private set; }
        public static float Timestamp { get; private set; }

        private static readonly Dictionary<string, CVPose> poses = new();
        private static readonly Dictionary<string, bool> anchorFlags = new();

        private static byte[] prevGray;

        public static CVPose Primary => PrimaryPose;

        // How many synthetic keypoints to generate per tracked target
        private const int SYNTH_KEYPOINT_COUNT = 16;

        // =========================================================
        // FRAME ENTRY (CPU Grayscale – Pi Safe)
        // =========================================================

        public static void ProcessFrame(byte[] currGray, int width, int height)
        {
            if (currGray == null || currGray.Length == 0)
                return;

            FrameIndex++;
            Timestamp += Time.deltaTime;
            TrackedNames.Clear();

            // --- Compute flow ONCE ---
            Vector2 avgFlow = Vector2.zero;
            bool hasFlow = false;

            if (prevGray != null)
            {
                hasFlow = CVFlowTracker.Update(
                    currGray,
                    prevGray,
                    width,
                    height,
                    out avgFlow,
                    out _
                );
            }

            // Tunable constants
            const float FLOW_POS_SCALE = 0.0015f;
            const float FLOW_ROT_SCALE = 0.0008f; // radians per pixel

            foreach (string name in CVTargetDatabase.AllTargets())
            {
                // Start from previous pose or identity
                CVPose pose = poses.TryGetValue(name, out var existing) ? existing : CVPose.Identity;

                // --- ROTATION FROM FLOW (YAW) ---
                if (hasFlow)
                {
                    float yawDeltaRad = -avgFlow.x * FLOW_ROT_SCALE; // flip sign if backwards
                    float yawDeltaDeg = yawDeltaRad * Mathf.Rad2Deg;
                    Quaternion dq = Quaternion.AngleAxis(yawDeltaDeg, Vector3.up);
                    Quaternion q = pose.Rotation * dq;
                    pose.Rotation = q;
                }

                // --- TRANSLATION FROM FLOW ---
                if (hasFlow)
                {
                    pose.px += avgFlow.x * FLOW_POS_SCALE;
                    pose.py -= avgFlow.y * FLOW_POS_SCALE;
                }

                // --- MARK POSE AS CONFIDENT / VALID ---
                pose.confidence = 1.0f;

                // --- SYNTHESIZE KEYPOINTS ---
                List<CVKeypoint> keys = pose.Keypoints;
                keys.Clear();

                int cols = Mathf.CeilToInt(Mathf.Sqrt(SYNTH_KEYPOINT_COUNT));
                int rows = cols;

                for (int r = 0; r < rows && keys.Count < SYNTH_KEYPOINT_COUNT; r++)
                {
                    for (int c = 0; c < cols && keys.Count < SYNTH_KEYPOINT_COUNT; c++)
                    {
                        float u = (c + 0.5f) / cols;
                        float v = (r + 0.5f) / rows;

                        CVKeypoint kp = new CVKeypoint
                        {
                            imagePosition = new Vector2(u * width, v * height),
                            worldPosition = Vector3.zero,
                            response = 1.0f,
                            matched = true,
                            quality = CVKeypointQuality.High
                        };

                        keys.Add(kp);
                    }
                }

                pose.Keypoints = keys;

                poses[name] = pose;
                TrackedNames.Add(name);
            }

            PrimaryPose =
                TrackedNames.Count > 0
                    ? poses[TrackedNames[0]]
                    : CVPose.Identity;

            prevGray = currGray;
        }

        // =========================================================
        // QUERY API
        // =========================================================

        public static bool TryGetPose(string targetName, out CVPose pose)
            => poses.TryGetValue(targetName, out pose);

        public static bool TryGetTrackedPose(string targetName, out CVPose pose)
            => poses.TryGetValue(targetName, out pose);

        // =========================================================
        // TARGET / ANCHOR CONTROL
        // =========================================================

        public static void RegisterTarget(string targetName)
        {
            if (!poses.ContainsKey(targetName))
                poses[targetName] = CVPose.Identity;

            if (!anchorFlags.ContainsKey(targetName))
                anchorFlags[targetName] = false;
        }

        public static void SetTargetAnchor(string targetName, bool anchored)
        {
            if (string.IsNullOrEmpty(targetName))
                return;

            anchorFlags[targetName] = anchored;
        }

        public static bool IsTargetAnchored(string targetName)
            => anchorFlags.TryGetValue(targetName, out bool a) && a;

        public static void Reset()
        {
            TrackedNames.Clear();
            poses.Clear();
            anchorFlags.Clear();
            prevGray = null;

            PrimaryPose = CVPose.Identity;
            FrameIndex = 0;
            Timestamp = 0f;
        }
    }
}
