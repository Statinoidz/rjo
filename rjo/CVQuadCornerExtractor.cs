using UnityEngine;
using System.Collections.Generic;

public static class CVQuadCornerExtractor
{
    public static bool TryExtractQuad(
        List<Vector2> points,
        float expectedAspect,
        out Vector2[] quad,
        float aspectTolerance = 0.25f)
    {
        quad = null;
        if (points == null || points.Count < 10) return false;

        // Compute bounds
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (var p in points)
        {
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }

        float w = maxX - minX;
        float h = maxY - minY;
        if (h <= 0.0001f) return false;

        float aspect = w / h;
        if (Mathf.Abs(aspect - expectedAspect) > aspectTolerance)
            return false;

        quad = new Vector2[4];
        quad[0] = new Vector2(minX, minY);
        quad[1] = new Vector2(maxX, minY);
        quad[2] = new Vector2(maxX, maxY);
        quad[3] = new Vector2(minX, maxY);

        return true;
    }
}
