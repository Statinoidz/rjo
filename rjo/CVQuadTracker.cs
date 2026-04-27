using UnityEngine;

/// <summary>
/// Stabilizes quad detection across frames using
/// centroid + normalized shape comparison.
/// 
/// Designed to work with silhouette-derived quads
/// and distance-based scale changes.
/// </summary>
public class CVQuadTracker
{
    [Header("Stability Thresholds")]
    [Tooltip("Max normalized corner drift (scale-independent)")]
    public float maxNormalizedCornerDrift = 0.08f;

    [Tooltip("Max centroid movement allowed (normalized)")]
    public float maxCentroidShift = 0.12f;

    Vector2[] lastQuad;
    float lastScale;

    // =========================================================
    // ===================== PUBLIC API ========================
    // =========================================================

    public bool Update(Vector2[] newQuad, out Vector2[] stabilized)
    {
        stabilized = null;

        if (newQuad == null || newQuad.Length != 4)
            return false;

        if (lastQuad == null)
        {
            lastQuad = CopyQuad(newQuad);
            lastScale = EstimateScale(newQuad);
            stabilized = newQuad;
            return true;
        }

        Vector2 lastCenter = ComputeCentroid(lastQuad);
        Vector2 newCenter = ComputeCentroid(newQuad);

        float centroidShift =
            Vector2.Distance(lastCenter, newCenter) /
            Mathf.Max(lastScale, 0.0001f);

        if (centroidShift > maxCentroidShift)
            return false;

        float newScale = EstimateScale(newQuad);
        float scale = Mathf.Max(lastScale, newScale, 0.0001f);

        for (int i = 0; i < 4; i++)
        {
            float drift =
                Vector2.Distance(lastQuad[i], newQuad[i]) / scale;

            if (drift > maxNormalizedCornerDrift)
                return false;
        }

        lastQuad = CopyQuad(newQuad);
        lastScale = newScale;
        stabilized = newQuad;
        return true;
    }

    public void Reset()
    {
        lastQuad = null;
        lastScale = 0f;
    }

    // =========================================================
    // ===================== INTERNALS =========================
    // =========================================================

    Vector2 ComputeCentroid(Vector2[] quad)
    {
        Vector2 sum = Vector2.zero;
        for (int i = 0; i < 4; i++)
            sum += quad[i];
        return sum * 0.25f;
    }

    float EstimateScale(Vector2[] quad)
    {
        Vector2 c = ComputeCentroid(quad);
        float total = 0f;

        for (int i = 0; i < 4; i++)
            total += Vector2.Distance(c, quad[i]);

        return total * 0.25f;
    }

    Vector2[] CopyQuad(Vector2[] src)
    {
        Vector2[] copy = new Vector2[4];
        for (int i = 0; i < 4; i++)
            copy[i] = src[i];
        return copy;
    }
}
