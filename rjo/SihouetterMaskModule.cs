// SihouetterMaskModule.cs
// Partial Sihouetter class: Mask building, morphology, and contour tracing.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CVatGPT
{
    public partial class Sihouetter : MonoBehaviour
    {
        // Build binary mask (0/1) at maskW x maskH.
        // srcTexture can be Texture or RenderTexture.
        public static byte[] BuildBinaryMask(
            Texture srcTexture,
            Texture2D backgroundRef,
            int maskW,
            int maskH,
            bool useDynamicBackground,
            bool enableChroma,
            Color chromaColor,
            float chromaTolerance,
            float diffThreshold,
            int morphErode,
            int morphDilate)
        {
            if (srcTexture == null) return null;

            Texture2D liveScaled = DownscaleTextureTo(srcTexture, maskW, maskH);
            if (liveScaled == null) return null;
            Texture2D bgScaled = null;
            if (useDynamicBackground && backgroundRef != null)
                bgScaled = DownscaleTextureTo(backgroundRef, maskW, maskH);

            byte[] mask = new byte[maskW * maskH];
            Color32[] livePixels = liveScaled.GetPixels32();
            Color32[] bgPixels = bgScaled != null ? bgScaled.GetPixels32() : null;

            for (int y = 0; y < maskH; y++)
            {
                for (int x = 0; x < maskW; x++)
                {
                    int idx = y * maskW + x;
                    Color32 lc = livePixels[idx];
                    bool isForeground = false;

                    if (useDynamicBackground && bgPixels != null)
                    {
                        Color32 bc = bgPixels[idx];
                        float diff = ColorDistance(lc, bc);
                        if (diff >= diffThreshold) isForeground = true;
                    }

                    if (!isForeground && enableChroma)
                    {
                        float chromaDiff = ColorDistance(lc, chromaColor);
                        if (chromaDiff > chromaTolerance)
                        {
                            isForeground = true; // non-chroma likely foreground
                        }
                    }

                    mask[idx] = (byte)(isForeground ? 1 : 0);
                }
            }

            if (morphErode > 0) for (int i = 0; i < morphErode; i++) mask = MorphErode(mask, maskW, maskH);
            if (morphDilate > 0) for (int i = 0; i < morphDilate; i++) mask = MorphDilate(mask, maskW, maskH);

            UnityEngine.Object.DestroyImmediate(liveScaled);
            if (bgScaled != null) UnityEngine.Object.DestroyImmediate(bgScaled);
            return mask;
        }

        public static Texture2D DownscaleTextureTo(Texture src, int w, int h)
        {
            if (src == null) return null;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(false, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        private static float ColorDistance(Color32 a, Color b)
        {
            float dr = ((float)a.r / 255f) - b.r;
            float dg = ((float)a.g / 255f) - b.g;
            float db = ((float)a.b / 255f) - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static float ColorDistance(Color32 a, Color32 b)
        {
            float dr = ((float)a.r - b.r) / 255f;
            float dg = ((float)a.g - b.g) / 255f;
            float db = ((float)a.b - b.b) / 255f;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static byte[] MorphErode(byte[] mask, int w, int h)
        {
            byte[] dst = new byte[mask.Length];
            Array.Copy(mask, dst, mask.Length);
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    bool keep = true;
                    for (int oy = -1; oy <= 1 && keep; oy++)
                        for (int ox = -1; ox <= 1 && keep; ox++)
                            if (mask[(y + oy) * w + (x + ox)] == 0) keep = false;
                    dst[i] = (byte)(keep ? 1 : 0);
                }
            return dst;
        }

        private static byte[] MorphDilate(byte[] mask, int w, int h)
        {
            byte[] dst = new byte[mask.Length];
            Array.Copy(mask, dst, mask.Length);
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    bool set = false;
                    for (int oy = -1; oy <= 1 && !set; oy++)
                        for (int ox = -1; ox <= 1 && !set; ox++)
                            if (mask[(y + oy) * w + (x + ox)] == 1) set = true;
                    dst[i] = (byte)(set ? 1 : 0);
                }
            return dst;
        }

        // Trace largest component's border (marching squares / border follow)
        public static List<Vector2> TraceLargestContour(byte[] mask, int w, int h)
        {
            if (mask == null) return null;
            int[] comp = new int[mask.Length];
            int compId = 0;
            int[] compCount = new int[mask.Length / 4 + 4];
            int[] q = new int[mask.Length];
            for (int i = 0; i < mask.Length; i++) comp[i] = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    if (mask[idx] == 0 || comp[idx] != 0) continue;
                    compId++;
                    int qh = 0, qt = 0;
                    q[qt++] = idx;
                    comp[idx] = compId;
                    int count = 0;
                    while (qh < qt)
                    {
                        int cur = q[qh++];
                        count++;
                        int cy = cur / w;
                        int cx = cur % w;
                        if (cx > 0) { int ni = cur - 1; if (mask[ni] == 1 && comp[ni] == 0) { comp[ni] = compId; q[qt++] = ni; } }
                        if (cx < w - 1) { int ni = cur + 1; if (mask[ni] == 1 && comp[ni] == 0) { comp[ni] = compId; q[qt++] = ni; } }
                        if (cy > 0) { int ni = cur - w; if (mask[ni] == 1 && comp[ni] == 0) { comp[ni] = compId; q[qt++] = ni; } }
                        if (cy < h - 1) { int ni = cur + w; if (mask[ni] == 1 && comp[ni] == 0) { comp[ni] = compId; q[qt++] = ni; } }
                    }
                    compCount[compId] = count;
                }

            if (compId == 0) return null;
            int best = 1; int bestCount = compCount[1];
            for (int i = 2; i <= compId; i++) if (compCount[i] > bestCount) { bestCount = compCount[i]; best = i; }

            byte[] compMask = new byte[mask.Length];
            for (int i = 0; i < mask.Length; i++) compMask[i] = (comp[i] == best) ? (byte)1 : (byte)0;

            List<Vector2> polygon = MarchingSquaresBorder(compMask, w, h);
            if (polygon == null || polygon.Count < 3) return null;

            var simplified = RamerDouglasPeucker(polygon, 1.0f);
            return simplified;
        }

        private static List<Vector2> MarchingSquaresBorder(byte[] mask, int w, int h)
        {
            int sx = -1, sy = -1;
            for (int y = 0; y < h && sx < 0; y++)
            {
                for (int x = 0; x < w; x++)
                    if (mask[y * w + x] == 1) { sx = x; sy = y; break; }
            }
            if (sx < 0) return null;

            List<Vector2> points = new List<Vector2>();
            int cx = sx, cy = sy;
            int dir = 0;
            int safety = 0;
            int[] DirX = { 1, 1, 0, -1, -1, -1, 0, 1 };
            int[] DirY = { 0, 1, 1, 1, 0, -1, -1, -1 };

            do
            {
                bool found = false;
                for (int i = 0; i < 8; i++)
                {
                    int nd = (dir + 7 + i) % 8;
                    int nx = cx + DirX[nd];
                    int ny = cy + DirY[nd];
                    if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                    {
                        if (mask[ny * w + nx] == 1)
                        {
                            cx = nx; cy = ny; dir = nd;
                            points.Add(new Vector2(cx, cy));
                            found = true;
                            break;
                        }
                    }
                }
                if (!found) break;
                safety++;
                if (safety > mask.Length * 4) break;
            } while (!(cx == sx && cy == sy && points.Count > 1));

            var cleaned = CleanPolyline(points);
            if (cleaned.Count >= 3 && !IsCCW(cleaned.ToArray())) cleaned.Reverse();
            return cleaned;
        }

        private static List<Vector2> CleanPolyline(List<Vector2> pts)
        {
            if (pts == null) return null;
            List<Vector2> outPts = new List<Vector2>();
            Vector2 last = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in pts)
            {
                if (Vector2.SqrMagnitude(p - last) > 0.25f)
                {
                    outPts.Add(p);
                    last = p;
                }
            }
            return outPts;
        }

        public static List<Vector2> RamerDouglasPeucker(List<Vector2> points, float epsilon)
        {
            if (points == null || points.Count < 3) return new List<Vector2>(points ?? new List<Vector2>());
            bool[] marked = new bool[points.Count];
            marked[0] = marked[points.Count - 1] = true;
            RDPRecurse(points, 0, points.Count - 1, epsilon, marked);
            List<Vector2> result = new List<Vector2>();
            for (int i = 0; i < points.Count; i++) if (marked[i]) result.Add(points[i]);
            return result;
        }

        private static void RDPRecurse(List<Vector2> pts, int a, int b, float eps, bool[] marked)
        {
            if (b <= a + 1) return;
            float maxDist = -1f;
            int index = a;
            Vector2 A = pts[a], B = pts[b];
            for (int i = a + 1; i < b; i++)
            {
                float d = PerpDistance(pts[i], A, B);
                if (d > maxDist) { index = i; maxDist = d; }
            }
            if (maxDist > eps)
            {
                marked[index] = true;
                RDPRecurse(pts, a, index, eps, marked);
                RDPRecurse(pts, index, b, eps, marked);
            }
        }

        private static float PerpDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            float dx = b.x - a.x, dy = b.y - a.y;
            if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f))
                return Vector2.Distance(p, a);
            float t = ((p.x - a.x) * dx + (p.y - a.y) * dy) / (dx * dx + dy * dy);
            Vector2 proj = new Vector2(a.x + t * dx, a.y + t * dy);
            return Vector2.Distance(p, proj);
        }

        private static bool IsCCW(Vector2[] poly)
        {
            float s = 0f;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % poly.Length];
                s += (b.x - a.x) * (b.y + a.y);
            }
            return s < 0f;
        }

        public static float GetVoxelStability(Vector3 voxelPosition, float voxelSize)
        {
            // hook into real pipeline later if needed; keep signature valid
            return 0.5f;
        }
    }
}
