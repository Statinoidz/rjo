using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Controls when an image target is "unlocked" based on
/// silhouette outline stability across frames and distances.
/// 
/// This replaces raw keypoint-count logic with geometry consistency.
/// </summary>
public class CVFeatureUnlocker
{
    [Header("Unlock Thresholds")]
    public int minOutlinePoints = 32;
    public int requiredStableFrames = 5;
    public float minConfidence = 0.85f;

    [Header("Outline Stability")]
    [Tooltip("Max average pixel drift allowed between outlines")]
    public float maxAverageDrift = 2.5f;

    int stableCount = 0;

    // Cached outline from previous frame (already marching-squares reduced)
    List<Vector2> lastOutline;

    // =========================================================
    // ===================== PUBLIC API ========================
    // =========================================================

    /// <summary>
    /// Attempt to unlock a target using its silhouette outline.
    /// Outline should already be generated via marching squares
    /// at an appropriate resolution for the current distance.
    /// </summary>
    public bool TryUnlock(
        List<Vector2> outline,
        float confidence)
    {
        if (outline == null || outline.Count < minOutlinePoints)
        {
            Reset();
            return false;
        }

        if (confidence < minConfidence)
        {
            Reset();
            return false;
        }

        if (lastOutline != null)
        {
            float drift = ComputeAverageDrift(lastOutline, outline);

            if (drift > maxAverageDrift)
            {
                Reset();
                lastOutline = CopyOutline(outline);
                return false;
            }
        }

        stableCount++;
        lastOutline = CopyOutline(outline);

        return stableCount >= requiredStableFrames;
    }

    public void Reset()
    {
        stableCount = 0;
        lastOutline = null;
    }

    // =========================================================
    // ===================== INTERNALS =========================
    // =========================================================

    float ComputeAverageDrift(
        List<Vector2> a,
        List<Vector2> b)
    {
        int count = Mathf.Min(a.Count, b.Count);
        if (count == 0) return float.MaxValue;

        float total = 0f;

        for (int i = 0; i < count; i++)
        {
            total += Vector2.Distance(a[i], b[i]);
        }

        return total / count;
    }

    List<Vector2> CopyOutline(List<Vector2> src)
    {
        List<Vector2> copy = new(src.Count);
        for (int i = 0; i < src.Count; i++)
            copy.Add(src[i]);
        return copy;
    }
}
