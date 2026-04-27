using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Polygon extraction and simplification from silhouette contours.
/// Produces ordered, simplified 2D polygon loops.
/// </summary>
public partial class Sihouetter
{
    // =========================================================
    // ===================== POLYGON STATE =====================
    // =========================================================

    protected readonly List<Vector2> polygon = new();
    protected bool polygonDirty = true;

    [Header("Polygon Settings")]
    [SerializeField] protected float simplifyEpsilon = 2.5f;
    [SerializeField] protected int minPolygonPoints = 8;

    // =========================================================
    // ===================== INITIALIZATION ====================
    // =========================================================

    protected void InitPolygon()
    {
        polygonDirty = true;
    }

    protected void ResetPolygon()
    {
        polygon.Clear();
        polygonDirty = true;
    }

    // =========================================================
    // ===================== POLYGON UPDATE ===================
    // =========================================================

    protected void UpdatePolygon(Texture2D source)
    {
        polygon.Clear();

        // IMPLEMENTED in SihouetterContour.cs
        List<Vector2Int> contour = GetContour(source);

        if (contour == null || contour.Count < minPolygonPoints)
        {
            polygonDirty = false;
            return;
        }

        // Convert pixels → float space
        List<Vector2> raw = new(contour.Count);
        for (int i = 0; i < contour.Count; i++)
        {
            raw.Add(new Vector2(contour[i].x, contour[i].y));
        }

        // Order contour points
        List<Vector2> ordered = OrderContour(raw);

        // Simplify polygon
        List<Vector2> simplified =
            DouglasPeucker(ordered, simplifyEpsilon);

        if (simplified.Count >= minPolygonPoints)
            polygon.AddRange(simplified);

        polygonDirty = false;
    }

    // =========================================================
    // ===================== ACCESSOR ==========================
    // =========================================================

    protected List<Vector2> GetPolygon(Texture2D source)
    {
        if (polygonDirty)
            UpdatePolygon(source);

        return polygon;
    }

    // =========================================================
    // ===================== CONTOUR ORDER =====================
    // =========================================================

    protected List<Vector2> OrderContour(List<Vector2> points)
    {
        if (points == null || points.Count == 0)
            return points;

        Vector2 center = Vector2.zero;
        for (int i = 0; i < points.Count; i++)
            center += points[i];
        center /= points.Count;

        points.Sort((a, b) =>
        {
            float angleA = Mathf.Atan2(a.y - center.y, a.x - center.x);
            float angleB = Mathf.Atan2(b.y - center.y, b.x - center.x);
            return angleA.CompareTo(angleB);
        });

        return points;
    }

    // =========================================================
    // ===================== SIMPLIFICATION ===================
    // =========================================================

    protected List<Vector2> DouglasPeucker(
        List<Vector2> points,
        float epsilon
    )
    {
        if (points == null || points.Count < 3)
            return points;

        bool[] keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;

        SimplifySection(points, keep, 0, points.Count - 1, epsilon);

        List<Vector2> result = new();
        for (int i = 0; i < points.Count; i++)
            if (keep[i])
                result.Add(points[i]);

        return result;
    }

    protected void SimplifySection(
        List<Vector2> pts,
        bool[] keep,
        int start,
        int end,
        float epsilon
    )
    {
        if (end <= start + 1)
            return;

        float maxDist = 0f;
        int index = -1;

        Vector2 a = pts[start];
        Vector2 b = pts[end];

        for (int i = start + 1; i < end; i++)
        {
            float dist = PerpendicularDistance(pts[i], a, b);
            if (dist > maxDist)
            {
                maxDist = dist;
                index = i;
            }
        }

        if (index >= 0 && maxDist > epsilon)
        {
            keep[index] = true;
            SimplifySection(pts, keep, start, index, epsilon);
            SimplifySection(pts, keep, index, end, epsilon);
        }
    }

    protected float PerpendicularDistance(
        Vector2 p,
        Vector2 a,
        Vector2 b
    )
    {
        if (a == b)
            return Vector2.Distance(p, a);

        float t =
            Vector2.Dot(p - a, b - a) /
            (b - a).sqrMagnitude;

        t = Mathf.Clamp01(t);
        Vector2 proj = a + t * (b - a);
        return Vector2.Distance(p, proj);
    }
}
