using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Contour extraction for Sihouetter.
/// Produces ordered, normalized contours suitable for world-space usage.
/// </summary>
public partial class Sihouetter
{
    // =========================================================
    // ===================== CONTOUR STATE =====================
    // =========================================================

    protected readonly List<List<Vector2>> contours = new();
    protected bool contourDirty = true;

    protected int maskWidth;
    protected int maskHeight;

    // =========================================================
    // ===================== INITIALIZATION ====================
    // =========================================================

    protected void InitContour()
    {
        contourDirty = true;
    }

    protected void ResetContour()
    {
        contours.Clear();
        contourDirty = true;
    }

    // =========================================================
    // ===================== CONTOUR UPDATE ===================
    // =========================================================

    protected void UpdateContour(Texture2D source)
    {
        Texture2D mask = GetMaskTexture(source);
        if (!mask)
            return;

        maskWidth = mask.width;
        maskHeight = mask.height;

        contours.Clear();

        Color32[] pixels = mask.GetPixels32();
        bool[] visited = new bool[pixels.Length];

        for (int y = 1; y < maskHeight - 1; y++)
        {
            for (int x = 1; x < maskWidth - 1; x++)
            {
                int idx = y * maskWidth + x;

                if (visited[idx])
                    continue;

                if (pixels[idx].r == 0)
                    continue;

                List<Vector2> contour = TraceOrderedContour(x, y, pixels, visited);

                if (contour.Count > 4)
                    contours.Add(contour);
            }
        }

        contourDirty = false;
    }

    // =========================================================
    // ===================== ORDERED TRACE ====================
    // =========================================================

    protected List<Vector2> TraceOrderedContour(
        int startX,
        int startY,
        Color32[] pixels,
        bool[] visited
    )
    {
        List<Vector2> contour = new();
        Stack<Vector2Int> stack = new();
        stack.Push(new Vector2Int(startX, startY));

        while (stack.Count > 0)
        {
            Vector2Int p = stack.Pop();
            int x = p.x;
            int y = p.y;

            int idx = y * maskWidth + x;
            if (visited[idx])
                continue;

            visited[idx] = true;

            if (pixels[idx].r == 0)
                continue;

            if (IsBoundaryPixel(x, y, pixels))
            {
                contour.Add(new Vector2(
                    (float)x / (maskWidth - 1),
                    (float)y / (maskHeight - 1)
                ));
            }

            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0)
                        continue;

                    int nx = x + ox;
                    int ny = y + oy;

                    if (nx <= 0 || ny <= 0 || nx >= maskWidth - 1 || ny >= maskHeight - 1)
                        continue;

                    int nidx = ny * maskWidth + nx;
                    if (!visited[nidx] && pixels[nidx].r != 0)
                        stack.Push(new Vector2Int(nx, ny));
                }
            }
        }

        return contour;
    }

    // =========================================================
    // ===================== BOUNDARY CHECK ===================
    // =========================================================

    protected bool IsBoundaryPixel(int x, int y, Color32[] pixels)
    {
        int idx = y * maskWidth + x;

        if (pixels[idx - 1].r == 0) return true;
        if (pixels[idx + 1].r == 0) return true;
        if (pixels[idx - maskWidth].r == 0) return true;
        if (pixels[idx + maskWidth].r == 0) return true;

        return false;
    }

    // =========================================================
    // ===================== ACCESSORS =========================
    // =========================================================

    protected List<List<Vector2>> GetContours(Texture2D source)
    {
        if (contourDirty)
            UpdateContour(source);

        return contours;
    }

    // =========================================================
    // ============ REQUIRED BY SihouetterPolygon ==============
    // =========================================================

    protected List<Vector2Int> GetContour(Texture2D source)
    {
        if (contourDirty)
            UpdateContour(source);

        if (contours.Count == 0)
            return null;

        // Pick the largest contour (most stable)
        List<Vector2> best = contours[0];
        for (int i = 1; i < contours.Count; i++)
        {
            if (contours[i].Count > best.Count)
                best = contours[i];
        }

        List<Vector2Int> result = new(best.Count);
        for (int i = 0; i < best.Count; i++)
        {
            result.Add(new Vector2Int(
                Mathf.RoundToInt(best[i].x * (maskWidth - 1)),
                Mathf.RoundToInt(best[i].y * (maskHeight - 1))
            ));
        }

        return result;
    }
}
