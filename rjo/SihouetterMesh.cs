using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Mesh generation from silhouette polygon.
/// Converts polygon loops into a triangulated Unity Mesh.
/// World-space correct via CV RawImage mapping.
/// </summary>
public partial class Sihouetter
{
    // =========================================================
    // ===================== MESH STATE ========================
    // =========================================================

    protected Mesh silhouetteMesh;
    protected bool meshDirty = true;

    [Header("Mesh Settings")]
    [SerializeField] protected bool flipWinding = false;
    [SerializeField] protected float zDepth = 0f;

    [Header("World Space Mapping")]
    [Tooltip("RawImage that represents the CV frame (screen or world space)")]
    [SerializeField] protected RawImage cvRawImage;

    // =========================================================
    // ===================== INITIALIZATION ====================
    // =========================================================

    protected void InitMesh()
    {
        if (silhouetteMesh == null)
        {
            silhouetteMesh = new Mesh
            {
                name = "SihouetterMesh"
            };
            silhouetteMesh.MarkDynamic();
        }

        meshDirty = true;
    }

    protected void ResetMesh()
    {
        if (silhouetteMesh != null)
            silhouetteMesh.Clear();

        meshDirty = true;
    }

    // =========================================================
    // ===================== MESH UPDATE ======================
    // =========================================================

    protected void UpdateMesh(Texture2D source)
    {
        if (cvRawImage == null)
            return;

        List<Vector2> poly = GetPolygon(source);
        if (poly == null || poly.Count < 3)
            return;

        int[] indices = Triangulate(poly);
        if (indices == null || indices.Length < 3)
            return;

        RectTransform rt = cvRawImage.rectTransform;
        Rect rect = rt.rect;

        Vector3[] vertices = new Vector3[poly.Count];
        Vector2[] uvs = new Vector2[poly.Count];

        float texW = source.width;
        float texH = source.height;

        for (int i = 0; i < poly.Count; i++)
        {
            // Pixel -> UV
            float u = poly[i].x / texW;
            float v = poly[i].y / texH;

            // UV -> local rect space (centered)
            float localX = (u - 0.5f) * rect.width;
            float localY = (v - 0.5f) * rect.height;

            vertices[i] = new Vector3(localX, localY, zDepth);
            uvs[i] = new Vector2(u, v);
        }

        silhouetteMesh.Clear();
        silhouetteMesh.vertices = vertices;
        silhouetteMesh.triangles = indices;
        silhouetteMesh.uv = uvs;

        silhouetteMesh.RecalculateNormals();
        silhouetteMesh.RecalculateBounds();

        meshDirty = false;
    }

    // =========================================================
    // ===================== ACCESSOR ==========================
    // =========================================================

    protected Mesh GetMesh(Texture2D source)
    {
        if (meshDirty)
            UpdateMesh(source);

        return silhouetteMesh;
    }

    // =========================================================
    // ===================== TRIANGULATION ====================
    // =========================================================

    protected int[] Triangulate(List<Vector2> poly)
    {
        List<int> indices = new();
        List<int> verts = new();

        for (int i = 0; i < poly.Count; i++)
            verts.Add(i);

        int guard = 0;

        while (verts.Count > 2 && guard++ < 10000)
        {
            bool earFound = false;

            for (int i = 0; i < verts.Count; i++)
            {
                int prev = verts[(i - 1 + verts.Count) % verts.Count];
                int curr = verts[i];
                int next = verts[(i + 1) % verts.Count];

                if (!IsConvex(poly[prev], poly[curr], poly[next]))
                    continue;

                bool containsPoint = false;
                for (int j = 0; j < verts.Count; j++)
                {
                    int v = verts[j];
                    if (v == prev || v == curr || v == next)
                        continue;

                    if (PointInTriangle(
                        poly[v],
                        poly[prev],
                        poly[curr],
                        poly[next]
                    ))
                    {
                        containsPoint = true;
                        break;
                    }
                }

                if (containsPoint)
                    continue;

                if (flipWinding)
                {
                    indices.Add(prev);
                    indices.Add(curr);
                    indices.Add(next);
                }
                else
                {
                    indices.Add(prev);
                    indices.Add(next);
                    indices.Add(curr);
                }

                verts.RemoveAt(i);
                earFound = true;
                break;
            }

            if (!earFound)
                break;
        }

        return indices.ToArray();
    }

    protected bool IsConvex(Vector2 a, Vector2 b, Vector2 c)
    {
        return Vector2.SignedAngle(b - a, c - b) < 0f;
    }

    protected bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float area = TriangleArea(a, b, c);
        float a1 = TriangleArea(p, b, c);
        float a2 = TriangleArea(a, p, c);
        float a3 = TriangleArea(a, b, p);

        return Mathf.Abs(area - (a1 + a2 + a3)) < 0.01f;
    }

    protected float TriangleArea(Vector2 a, Vector2 b, Vector2 c)
    {
        return Mathf.Abs(
            (a.x * (b.y - c.y) +
             b.x * (c.y - a.y) +
             c.x * (a.y - b.y)) * 0.5f
        );
    }
}
