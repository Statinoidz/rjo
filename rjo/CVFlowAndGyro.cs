using System.Collections.Generic;
using UnityEngine;

namespace CVatGPT
{
    /// <summary>
    /// Gyro + temporal pose stabilizer.
    /// Optical flow is handled by CVTrackingManager.
    /// </summary>
    public static class CVFlowAndGyro
    {
        private static readonly Dictionary<string, CVPose> lastPose = new();
        private static readonly Dictionary<string, float> confidence = new();

        /// <summary>
        /// Apply gyro rotation and temporal smoothing.
        /// Translation is handled elsewhere.
        /// </summary>
        public static void Update(
            string name,
            Vector3 gyro,
            float dt,
            ref CVPose pose
        )
        {
            // -------------------- TEMPORAL SMOOTHING --------------------
            if (lastPose.TryGetValue(name, out CVPose prev))
            {
                pose = CVPoseUtils.NlerpPose(prev, pose, 0.6f);
            }
            else
            {
                pose = pose.IsValid ? pose : CVPose.Identity;
            }

            // -------------------- GYRO ROTATION --------------------
            if (gyro != Vector3.zero && dt > 0f)
            {
                Quaternion dq = CVPoseUtils.DeltaQuatFromAngVel(gyro, dt);
                Quaternion q = pose.Rotation * dq;

                pose.qx = q.x;
                pose.qy = q.y;
                pose.qz = q.z;
                pose.qw = q.w;
            }

            // -------------------- CONFIDENCE --------------------
            float prevConf = confidence.TryGetValue(name, out float c) ? c : 0f;
            confidence[name] = Mathf.Clamp01(prevConf * 0.95f + 0.05f);

            lastPose[name] = pose;
        }

        public static float GetConfidence(string name)
        {
            return confidence.TryGetValue(name, out float c) ? c : 0f;
        }

        public static void Reset()
        {
            lastPose.Clear();
            confidence.Clear();
        }
    }
}
