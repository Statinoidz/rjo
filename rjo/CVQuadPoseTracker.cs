using UnityEngine;
using System.Collections.Generic;

public class CVQuadPoseTracker
{
    public struct TargetData
    {
        public string name;
        public bool unlocked;
        public Vector3[] corners; // quad corners in world space
        public float confidence;  // 0-1
    }

    private CVGyroDeadReckoner gyroDeadReckoner;

    private TargetData primaryTarget;
    private TargetData secondaryTarget;

    // Tunables
    public int minPointsToUnlock = 12;
    public int maxPointsExpected = 32;
    public bool useHighContrastOutline = true;
    public float secondaryPromotionConfidence = 0.8f;

    public CVQuadPoseTracker(CVGyroDeadReckoner gyro)
    {
        gyroDeadReckoner = gyro;
    }

    // --------------------------
    // Main update loop
    // --------------------------
    public void UpdateTarget(Texture2D cameraFrame, float deltaTime)
    {
        // 1. Attempt to detect target corners
        DetectCorners(ref primaryTarget, cameraFrame);
        DetectCorners(ref secondaryTarget, cameraFrame);

        // 2. Unlock / lock logic
        if (!primaryTarget.unlocked && primaryTarget.confidence >= 1f)
            primaryTarget.unlocked = true;

        if (!secondaryTarget.unlocked && secondaryTarget.confidence >= secondaryPromotionConfidence)
            secondaryTarget.unlocked = true;

        // 3. Gyro fallback if primary lost
        if (primaryTarget.unlocked && primaryTarget.confidence < 0.1f)
        {
            bool gyroValid = gyroDeadReckoner.Update(deltaTime);
            if (!gyroValid)
            {
                Debug.Log("[CVQuadPoseTracker] Gyro fallback failed; tracking lost");
            }
        }
        else
        {
            // Reset gyro if target regained
            if (primaryTarget.unlocked)
                gyroDeadReckoner.ResetFromVision(GetPrimaryPosition(), GetPrimaryRotation());
        }
    }

    // --------------------------
    // Corner detection
    // --------------------------
    void DetectCorners(ref TargetData t, Texture2D frame)
    {
        if (frame == null) return;

        // For now, pseudo-detection logic (replace with actual CV code)
        int detectedPoints = Random.Range(0, maxPointsExpected);

        t.confidence = Mathf.Clamp01((float)detectedPoints / maxPointsExpected);

        // If using high-contrast outline, simulate quad corners
        if (useHighContrastOutline && t.confidence >= 0.5f)
        {
            t.corners = new Vector3[4]
            {
                new Vector3(-0.5f,0f, -0.5f),
                new Vector3(0.5f,0f,-0.5f),
                new Vector3(0.5f,0f,0.5f),
                new Vector3(-0.5f,0f,0.5f)
            };
        }
    }

    // --------------------------
    // Public getters
    // --------------------------
    public Vector3 GetPrimaryPosition()
    {
        if (primaryTarget.corners != null && primaryTarget.corners.Length == 4)
            return (primaryTarget.corners[0] + primaryTarget.corners[1] +
                    primaryTarget.corners[2] + primaryTarget.corners[3]) / 4f;
        return Vector3.zero;
    }

    public Quaternion GetPrimaryRotation()
    {
        if (primaryTarget.corners != null && primaryTarget.corners.Length == 4)
        {
            Vector3 forward = (primaryTarget.corners[1] - primaryTarget.corners[0] +
                               primaryTarget.corners[2] - primaryTarget.corners[3]) / 2f;
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
        return Quaternion.identity;
    }

    public TargetData GetPrimaryTarget() => primaryTarget;
    public TargetData GetSecondaryTarget() => secondaryTarget;
}
