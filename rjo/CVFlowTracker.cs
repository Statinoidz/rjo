using UnityEngine;
using System.Collections.Generic;

public static class CVFlowTracker
{
    // =========================================================
    // CONFIGURABLE GRID RESOLUTION
    // =========================================================
    public static int GridX { get; set; } = 4;   // default 4×4
    public static int GridY { get; set; } = 4;

    private static int SEARCH_RADIUS = 1;
    private static int PATCH_RADIUS = 0;
    private static int MIN_VALID_POINTS = 4;

    private static readonly List<Vector2> points = new();

    private static byte[] prev;
    private static byte[] curr;

    private static int w, h;
    private static bool hasPrev;

    private static Vector2[] lastFlow;
    private static bool hasFlow;

    // =========================================================
    public static void Reset()
    {
        points.Clear();
        prev = null;
        curr = null;
        lastFlow = null;
        hasPrev = false;
        hasFlow = false;
        w = h = 0;
    }

    public static bool Update(
        byte[] currGray,
        byte[] prevGray,
        int width,
        int height,
        out Vector2 avgFlow,
        out Vector2[] flowGrid
    )
    {
        avgFlow = Vector2.zero;
        flowGrid = null;

        if (currGray == null || prevGray == null)
            return false;

        if (!hasPrev || w != width || h != height)
        {
            Initialize(width, height);
            prev = prevGray;
            hasPrev = true;
            return false;
        }

        w = width;
        h = height;
        curr = currGray;

        Vector2[] grid = new Vector2[GridX * GridY];
        int[] gridCount = new int[grid.Length];

        int valid = 0;
        Vector2 total = Vector2.zero;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 p = points[i];
            if (!InBounds(p)) continue;

            Vector2 delta = EstimateMotion(p);
            if (delta == Vector2.zero) continue;

            int gx = Mathf.Clamp((int)(p.x / w * GridX), 0, GridX - 1);
            int gy = Mathf.Clamp((int)(p.y / h * GridY), 0, GridY - 1);
            int gi = gy * GridX + gx;

            grid[gi] += delta;
            gridCount[gi]++;

            total += delta;
            valid++;

            points[i] = p + delta;
        }

        if (valid < MIN_VALID_POINTS)
        {
            Initialize(width, height);
            prev = curr;
            return false;
        }

        for (int i = 0; i < grid.Length; i++)
            if (gridCount[i] > 0)
                grid[i] /= gridCount[i];

        avgFlow = total / valid;

        lastFlow = grid;
        hasFlow = true;

        prev = curr;
        flowGrid = grid;

        return true;
    }

    public static bool TryGetLastFlow(
        out Vector2[] flow,
        out int gridWidth,
        out int gridHeight
    )
    {
        flow = null;
        gridWidth = GridX;
        gridHeight = GridY;

        if (!hasFlow || lastFlow == null)
            return false;

        flow = lastFlow;
        return true;
    }

    // =========================================================
    private static void Initialize(int width, int height)
    {
        w = width;
        h = height;
        points.Clear();

        int sx = w / GridX;
        int sy = h / GridY;

        for (int y = sy / 2; y < h; y += sy)
            for (int x = sx / 2; x < w; x += sx)
                points.Add(new Vector2(x, y));
    }

    private static Vector2 EstimateMotion(Vector2 p)
    {
        int cx = Mathf.RoundToInt(p.x);
        int cy = Mathf.RoundToInt(p.y);

        float best = float.MaxValue;
        Vector2 bestDelta = Vector2.zero;

        for (int dy = -SEARCH_RADIUS; dy <= SEARCH_RADIUS; dy++)
        for (int dx = -SEARCH_RADIUS; dx <= SEARCH_RADIUS; dx++)
        {
            float err = PatchError(cx, cy, dx, dy);
            if (err < best)
            {
                best = err;
                bestDelta = new Vector2(dx, dy);
            }
        }

        return bestDelta;
    }

    private static float PatchError(int x, int y, int dx, int dy)
    {
        float sum = 0f;

        for (int py = -PATCH_RADIUS; py <= PATCH_RADIUS; py++)
        for (int px = -PATCH_RADIUS; px <= PATCH_RADIUS; px++)
        {
            float a = Sample(prev, x + px, y + py);
            float b = Sample(curr, x + px + dx, y + py + dy);
            float d = a - b;
            sum += d * d;
        }

        return sum;
    }

    private static float Sample(byte[] buf, int x, int y)
    {
        if (buf == null || x < 0 || y < 0 || x >= w || y >= h)
            return 0f;

        return buf[y * w + x] / 255f;
    }

    private static bool InBounds(Vector2 p)
    {
        return p.x >= 2 && p.y >= 2 && p.x < w - 2 && p.y < h - 2;
    }
}
