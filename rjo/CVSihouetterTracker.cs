using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Tracks a silhouette of a target based on quad corners and video input.
/// Maintains stability maps and allows confidence decay.
/// Integrates with gyro fallback for when visual tracking fails.
/// Now includes silhouette-edge + fallback optical flow for walking movement.
/// </summary>
public class CVSihouetterTracker
{
    public struct SilhouetteData
    {
        public Vector2[] outlinePixels;  // edge pixels in camera space (0-1 UV)
        public float stability;          // 0-1
        public float confidence;         // 0-1
        public bool lostTracking;        // visual tracking lost
    }

    private CVQuadPoseTracker quadTracker;
    private CVGyroDeadReckoner gyroDeadReckoner;

    // Runtime tweakables
    public int minOutlinePoints = 8;
    public int maxOutlinePoints = 64;
    public float confidenceDecayRate = 0.3f;
    public float stabilitySmoothing = 0.5f;
    public bool assumeCameraFlat = true;

    private SilhouetteData primarySilhouette;
    private SilhouetteData secondarySilhouette;

    // --- Optical Flow State ---
    private Texture2D prevFrame;   // snapshot of previous frame
    private Vector2 rawFlow;
    private Vector2 stableFlow;
    private float walkingSpeed;     // meters per second
    private float targetSpeed;      // smoothed target
    private float turnSuppression;  // 0–1

    public CVSihouetterTracker(CVQuadPoseTracker quad, CVGyroDeadReckoner gyro)
    {
        quadTracker = quad;
        gyroDeadReckoner = gyro;
    }

    /// <summary>
    /// Update the silhouette tracker each frame.
    /// </summary>
    public void UpdateSilhouette(Texture2D cameraFrame, float deltaTime)
    {
        // 0. No frame? Decay and bail.
        if (cameraFrame == null)
        {
            DecaySilhouette(ref primarySilhouette, deltaTime);
            DecaySilhouette(ref secondarySilhouette, deltaTime);
            return;
        }

        // 1. Quad tracking only if quadTracker exists
        if (quadTracker != null)
        {
            var primary = quadTracker.GetPrimaryTarget();
            if (primary.unlocked && primary.corners != null)
                TrackOutline(ref primarySilhouette, primary, cameraFrame);
            else
                DecaySilhouette(ref primarySilhouette, deltaTime);

            var secondary = quadTracker.GetSecondaryTarget();
            if (secondary.unlocked && secondary.corners != null)
                TrackOutline(ref secondarySilhouette, secondary, cameraFrame);
            else
                DecaySilhouette(ref secondarySilhouette, deltaTime);
        }
        else
        {
            // No quad tracker → silhouettes decay naturally
            DecaySilhouette(ref primarySilhouette, deltaTime);
            DecaySilhouette(ref secondarySilhouette, deltaTime);
        }

        // 2. Gyro fallback only if gyro exists
        if (gyroDeadReckoner != null)
        {
            if (primarySilhouette.confidence < 0.1f)
                primarySilhouette.lostTracking = !gyroDeadReckoner.Update(deltaTime);
            else
                primarySilhouette.lostTracking = false;
        }

        // 3. Ensure we have a previous-frame snapshot
        if (prevFrame == null ||
            prevFrame.width != cameraFrame.width ||
            prevFrame.height != cameraFrame.height)
        {
            if (prevFrame != null)
                Object.Destroy(prevFrame);

            prevFrame = new Texture2D(cameraFrame.width, cameraFrame.height, TextureFormat.RGB24, false);
            prevFrame.SetPixels(cameraFrame.GetPixels());
            prevFrame.Apply();
            return; // need one frame of history before flow
        }

        // 4. Optical flow ALWAYS runs if we have a previous snapshot
        bool haveEdges = primarySilhouette.outlinePixels != null &&
                         primarySilhouette.outlinePixels.Length > 0;

        if (haveEdges)
            rawFlow = ComputeEdgeFlow(prevFrame, cameraFrame, primarySilhouette.outlinePixels);
        else
            rawFlow = ComputeFallbackFlow(prevFrame, cameraFrame);

        // Temporal smoothing (reject noise)
        stableFlow = Vector2.Lerp(stableFlow, rawFlow, 0.12f);

        // Jostle suppression
        if (Mathf.Abs(rawFlow.x) > Mathf.Abs(stableFlow.x) * 4f)
            stableFlow = Vector2.Lerp(stableFlow, Vector2.zero, 0.5f);

        // Turning suppression (rotation ≠ walking)
        float turnAmount = Mathf.Abs(rawFlow.x) - Mathf.Abs(stableFlow.x);
        turnSuppression = Mathf.Clamp01(turnSuppression + turnAmount * 0.1f);

        // Flow → target walking speed (m/s)
        float flowX = stableFlow.x * (1f - turnSuppression);

        // Allow backward, but heavily damped
        if (flowX < 0f)
            flowX *= 0.25f;

        targetSpeed = Mathf.Clamp(flowX * 3.0f, -0.5f, 1.3f);
        walkingSpeed = Mathf.Lerp(walkingSpeed, targetSpeed, 0.1f);

        // 5. Update previous-frame snapshot for next tick
        prevFrame.SetPixels(cameraFrame.GetPixels());
        prevFrame.Apply();
    }

    /// <summary>
    /// Detect outline pixels and calculate stability
    /// </summary>
    private void TrackOutline(ref SilhouetteData sil, CVQuadPoseTracker.TargetData target, Texture2D frame)
    {
        if (frame == null) return;

        int numPoints = Random.Range(minOutlinePoints, maxOutlinePoints); // placeholder for CV logic
        sil.outlinePixels = new Vector2[numPoints];
        for (int i = 0; i < numPoints; i++)
        {
            // placeholder: random points within quad
            float u = Random.value;
            float v = Random.value;
            sil.outlinePixels[i] = new Vector2(u, v);
        }

        // Simulate stability calculation
        float newStability = Mathf.Clamp01(numPoints / (float)maxOutlinePoints);
        sil.stability = Mathf.Lerp(sil.stability, newStability, stabilitySmoothing);
        sil.confidence = sil.stability;

        if (assumeCameraFlat)
        {
            // flatten rotation if needed
        }
    }

    /// <summary>
    /// Confidence decay if target not detected
    /// </summary>
    private void DecaySilhouette(ref SilhouetteData sil, float deltaTime)
    {
        sil.confidence = Mathf.Max(0f, sil.confidence - confidenceDecayRate * deltaTime);
        sil.stability = Mathf.Lerp(sil.stability, 0f, deltaTime * stabilitySmoothing);
        if (sil.confidence <= 0f) sil.outlinePixels = null;
    }

    /// <summary>
    /// 1D Lucas–Kanade-style optical flow on silhouette edge pixels.
    /// </summary>
    private Vector2 ComputeEdgeFlow(Texture2D prev, Texture2D curr, Vector2[] edges)
    {
        if (edges == null || edges.Length == 0) return Vector2.zero;

        float num = 0f;
        float den = 0f;

        foreach (var p in edges)
        {
            int x = Mathf.Clamp((int)(p.x * curr.width), 1, curr.width - 2);
            int y = Mathf.Clamp((int)(p.y * curr.height), 1, curr.height - 2);

            float c = curr.GetPixel(x, y).grayscale;
            float cL = curr.GetPixel(x - 1, y).grayscale;
            float cR = curr.GetPixel(x + 1, y).grayscale;
            float prevC = prev.GetPixel(x, y).grayscale;

            float Ix = 0.5f * (cR - cL);   // spatial gradient
            float It = c - prevC;          // temporal gradient

            num += -It * Ix;
            den += Ix * Ix;
        }

        if (den < 1e-6f) return Vector2.zero;
        float vx = num / den;
        return new Vector2(vx, 0f);
    }

    /// <summary>
    /// Fallback full-frame optical flow (Pi Zero safe).
    /// </summary>
    private Vector2 ComputeFallbackFlow(Texture2D prev, Texture2D curr)
    {
        float num = 0f;
        float den = 0f;

        int w = curr.width;
        int h = curr.height;

        // Sparse grid sampling (fast)
        for (int y = 4; y < h; y += 8)
        {
            for (int x = 4; x < w; x += 8)
            {
                float c = curr.GetPixel(x, y).grayscale;
                float cL = curr.GetPixel(x - 1, y).grayscale;
                float cR = curr.GetPixel(x + 1, y).grayscale;
                float prevC = prev.GetPixel(x, y).grayscale;

                float Ix = 0.5f * (cR - cL);
                float It = c - prevC;

                num += -It * Ix;
                den += Ix * Ix;
            }
        }

        if (den < 1e-6f) return Vector2.zero;
        float vx = num / den;
        return new Vector2(vx, 0f);
    }

    /// <summary>
    /// Walking delta for SilhouetteWalkDriver.
    /// </summary>
    public bool TryGetWalkingDelta(out Vector2 delta)
    {
        delta = new Vector2(walkingSpeed, 0f);
        return Mathf.Abs(walkingSpeed) > 0.0001f;
    }

    public SilhouetteData GetPrimarySilhouette() => primarySilhouette;
    public SilhouetteData GetSecondarySilhouette() => secondarySilhouette;
}
