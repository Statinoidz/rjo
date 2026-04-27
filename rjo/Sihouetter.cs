using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace CVatGPT
{
    [ExecuteAlways]
    public partial class Sihouetter : MonoBehaviour
    {
        // ============================================================
        // EXTERNAL REFERENCES (ALL MANUAL DRAG & DROP)
        // ============================================================

        [Header("Video / CV Pipeline")]
        public AAAVideo videoSource;
        public RawImage cvRawImage;
        public RawImage scaleReference;

        [Header("Terrain / Vertex Controllers")]
        public RuntimeTerrainMeshGenerator terrainMesh;
        public GameObject vertexPrefabReference;
        public GameObject terrainEditingGate;

        [Header("Cameras")]
        public Camera silhouetteCamera;
        public Camera worldCamera;

        [Header("Control Gates")]
        public GameObject sihouetterGate;

        [Header("Save / Load Gate")]
        public GameObject saveLoadGate;

        // ============================================================
        // MASK / SILHOUETTE SETTINGS
        // ============================================================

        [Header("Mask Resolution")]
        public int maskWidth = 256;
        public int maskHeight = 256;

        [Header("Background / Chroma")]
        public Texture2D backgroundReference;
        public bool useDynamicBackground = false;

        public bool enableChroma = false;
        public Color chromaColor = Color.green;
        [Range(0f, 1f)] public float chromaTolerance = 0.2f;

        [Header("Foreground Detection")]
        [Range(0f, 1f)] public float diffThreshold = 0.15f;
        [Range(0, 8)] public int morphErode = 1;
        [Range(0, 8)] public int morphDilate = 2;

        [Header("Contour Simplification")]
        [Range(0.1f, 5f)] public float rdpEpsilon = 1.0f;

        // ============================================================
        // VOLUME / VERTEX PUSHBACK SETTINGS
        // ============================================================

        [Header("Silhouette Volume")]
        public float maxDistanceFromCamera = 5f;
        public float pushBackNear = 1f;
        public float pushBackFar = 2f;

        [Header("Stamping")]
        public bool enableStamping = true;
        public string hashFileName = "StatiHash.txt";
        public string asciiFileName = "StatiAscii.txt";

        [Header("Debug")]
        public bool draw2DContourGizmos = true;
        public bool draw3DSilhouette = true;
        public float gizmoDepth = 5f;

        // ============================================================
        // INTERNAL STATE
        // ============================================================

        public Texture2D currFrameGray;

        private byte[] currMask;
        private byte[] prevMask;

        public List<List<Vector2>> polygons = new List<List<Vector2>>();
        private bool contourDirty = true;

        private Mesh silhouetteMesh;

        private GameObject[] vertexControllers;
        private readonly Dictionary<GameObject, Vector3> originalVertexPositions = new Dictionary<GameObject, Vector3>();
        private readonly HashSet<GameObject> pushedVertices = new HashSet<GameObject>();

        private bool wasEditingLastFrame = false;
        private bool hasEverEnteredEditing = false;

        private bool wasSaveLoadGateLastFrame = false;

        private const string blobFileName = "StatiBlob.txt";

        // ============================================================
        // UNITY LIFECYCLE
        // ============================================================

        private void OnEnable()
        {
            if (worldCamera == null)
                worldCamera = Camera.main;
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            if (sihouetterGate != null && !sihouetterGate.activeInHierarchy)
                return;

            bool editingNow = terrainEditingGate != null && terrainEditingGate.activeInHierarchy;

            // Editing just turned on
            if (editingNow && !wasEditingLastFrame)
            {
                if (!hasEverEnteredEditing)
                    hasEverEnteredEditing = true;
            }

            // While Editing! is ON: run CV + volume + hash stamping
            if (editingNow)
            {
                EnsureVertexControllers();
                UpdateMaskAndContour();
                ApplySilhouetteVolumeToVertices();

                if (enableStamping)
                {
                    WriteHashStampToFile();   // small, frequent, overwrite
                    WriteBlobStampToFile();   // tiny XXYY blob stamp
                }
            }

            // Save/Load gate: controls hash load and big ASCII dump
            if (saveLoadGate != null)
            {
                bool saveLoadNow = saveLoadGate.activeInHierarchy;

                // OFF -> ON : load hash file and move vertices
                if (saveLoadNow && !wasSaveLoadGateLastFrame)
                {
                    if (enableStamping)
                        TryLoadFromHashFile();
                }

                // ON -> OFF : write big ASCII dump
                if (!saveLoadNow && wasSaveLoadGateLastFrame)
                {
                    if (enableStamping)
                        WriteAsciiStampToFile();
                }

                wasSaveLoadGateLastFrame = saveLoadNow;
            }

            wasEditingLastFrame = editingNow;
        }

        // ============================================================
        // PUBLIC ACCESSORS
        // ============================================================

        public bool HasValidFrame()
        {
            return currFrameGray != null &&
                   currFrameGray.width > 0 &&
                   currFrameGray.height > 0;
        }

        public int ProcessingWidth => currFrameGray != null ? currFrameGray.width : 0;
        public int ProcessingHeight => currFrameGray != null ? currFrameGray.height : 0;

        public Mesh GetSilhouetteMesh()
        {
            return silhouetteMesh;
        }

        // Centroid of largest polygon in pixel space
        public bool TryGetBlobCentroid(out Vector2 centroid)
        {
            centroid = Vector2.zero;

            if (polygons == null || polygons.Count == 0)
                return false;

            List<Vector2> best = null;
            float bestArea = 0f;

            foreach (var poly in polygons)
            {
                if (poly == null || poly.Count < 3)
                    continue;

                float area = 0f;
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                {
                    Vector2 pi = poly[i];
                    Vector2 pj = poly[j];
                    area += (pj.x * pi.y - pi.x * pj.y);
                }
                area = Mathf.Abs(area) * 0.5f;

                if (area > bestArea)
                {
                    bestArea = area;
                    best = poly;
                }
            }

            if (best == null || best.Count < 3)
                return false;

            float sx = 0f, sy = 0f;
            for (int i = 0; i < best.Count; i++)
            {
                sx += best[i].x;
                sy += best[i].y;
            }

            centroid = new Vector2(sx / best.Count, sy / best.Count);
            return true;
        }

        // Centroid in viewport space [0..1]
        public bool TryGetBlobCentroidViewport(out Vector2 vp)
        {
            vp = Vector2.zero;

            if (!TryGetBlobCentroid(out var pix))
                return false;

            if (maskWidth <= 1 || maskHeight <= 1)
                return false;

            float u = pix.x / (maskWidth - 1);
            float v = pix.y / (maskHeight - 1);
            vp = new Vector2(u, v);
            return true;
        }

        // Centroid as a world point at a given depth from the silhouette camera
        public bool TryGetBlobWorldPoint(float depth, out Vector3 world)
        {
            world = Vector3.zero;

            if (silhouetteCamera == null)
                return false;

            if (!TryGetBlobCentroidViewport(out var vp))
                return false;

            Vector3 vp3 = new Vector3(vp.x, vp.y, depth);
            world = silhouetteCamera.ViewportToWorldPoint(vp3);
            return true;
        }

        // ============================================================
        // CORE: MASK + CONTOUR
        // ============================================================

        private void UpdateMaskAndContour()
        {
            if (videoSource == null)
                return;

            Texture src = videoSource.GetCvTexture();
            if (src == null)
                return;

            prevMask = currMask;

            currMask = BuildBinaryMask(
                src,
                backgroundReference,
                maskWidth,
                maskHeight,
                useDynamicBackground,
                enableChroma,
                chromaColor,
                chromaTolerance,
                diffThreshold,
                morphErode,
                morphDilate
            );

            if (currMask == null)
                return;

            if (currFrameGray == null ||
                currFrameGray.width != maskWidth ||
                currFrameGray.height != maskHeight)
            {
                if (currFrameGray != null)
                    Destroy(currFrameGray);

                currFrameGray = new Texture2D(maskWidth, maskHeight, TextureFormat.R8, false);
            }

            Color32[] grayPixels = new Color32[maskWidth * maskHeight];
            for (int i = 0; i < grayPixels.Length; i++)
            {
                byte v = (byte)(currMask[i] * 255);
                grayPixels[i] = new Color32(v, v, v, 255);
            }
            currFrameGray.SetPixels32(grayPixels);
            currFrameGray.Apply(false, false);

            contourDirty = true;
            UpdateContourFromMask();
        }

        private void UpdateContourFromMask()
        {
            if (!contourDirty || currMask == null)
                return;

            polygons.Clear();

            List<Vector2> largest = TraceLargestContour(currMask, maskWidth, maskHeight);
            if (largest != null && largest.Count >= 3)
                polygons.Add(largest);

            contourDirty = false;
        }

        // ============================================================
        // VERTEX CONTROLLERS / VOLUME LOGIC
        // ============================================================

        private void EnsureVertexControllers()
        {
            if (terrainMesh == null)
                return;

            vertexControllers = terrainMesh.GetVertexControllers();
            if (vertexControllers == null)
                return;

            foreach (var v in vertexControllers)
            {
                if (v == null) continue;
                if (!originalVertexPositions.ContainsKey(v))
                    originalVertexPositions[v] = v.transform.position;
            }
        }

        private void ApplySilhouetteVolumeToVertices()
        {
            if (silhouetteCamera == null)
                return;

            if (vertexControllers == null || vertexControllers.Length == 0)
                return;

            if (polygons == null || polygons.Count == 0)
                return;

            if (currMask == null || currMask.Length == 0)
                return;

            int texW = maskWidth;
            int texH = maskHeight;

            if (cvRawImage == null)
                return;

            Vector3 camPos = silhouetteCamera.transform.position;

            List<GameObject> candidates = new List<GameObject>();
            List<float> distances = new List<float>();

            for (int i = 0; i < vertexControllers.Length; i++)
            {
                GameObject controller = vertexControllers[i];
                if (controller == null)
                    continue;

                if (vertexPrefabReference != null)
                {
                    // soft filter; keep loose
                    if (!controller.name.StartsWith("Vertex"))
                    {
                        // allowed
                    }
                }

                Vector3 worldPos = controller.transform.position;
                Vector3 vp = silhouetteCamera.WorldToViewportPoint(worldPos);

                if (vp.z <= 0f) continue;
                if (vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;

                float dist = Vector3.Distance(camPos, worldPos);
                if (dist > maxDistanceFromCamera) continue;

                candidates.Add(controller);
                distances.Add(dist);
            }

            if (candidates.Count == 0)
                return;

            List<int> indices = new List<int>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++) indices.Add(i);
            indices.Sort((a, b) => distances[a].CompareTo(distances[b]));

            float minDist = distances[indices[0]];
            float maxDist = distances[indices[indices.Count - 1]];
            float distRange = Mathf.Max(0.0001f, maxDist - minDist);

            for (int k = 0; k < indices.Count; k++)
            {
                int idx = indices[k];
                GameObject controller = candidates[idx];
                float dist = distances[idx];

                Vector3 worldPos = controller.transform.position;
                Vector3 vp = silhouetteCamera.WorldToViewportPoint(worldPos);

                float px = vp.x * texW;
                float py = vp.y * texH;
                Vector2 pTex = new Vector2(px, py);

                bool isForeground = false;

                foreach (var poly in polygons)
                {
                    if (poly == null || poly.Count < 3)
                        continue;

                    if (PointInPolygon2D(poly, pTex))
                    {
                        isForeground = true;
                        break;
                    }
                }

                if (isForeground)
                {
                    if (originalVertexPositions.TryGetValue(controller, out var orig))
                        controller.transform.position = orig;
                    pushedVertices.Remove(controller);
                }
                else
                {
                    float t = (dist - minDist) / distRange;
                    float push = Mathf.Lerp(pushBackNear, pushBackFar, t);

                    Vector3 dir = (worldPos - camPos).normalized;
                    Vector3 newPos = worldPos + dir * push;

                    controller.transform.position = newPos;
                    pushedVertices.Add(controller);
                }
            }

            if (terrainMesh != null)
                terrainMesh.RequestRuntimeRedetection();
        }

        // ============================================================
        // STAMPING: HASH (SMALL, FREQUENT, RECOVERY)
        // ============================================================

        private void WriteHashStampToFile()
        {
            if (vertexControllers == null || vertexControllers.Length == 0)
                return;

            string path = Path.Combine(Application.persistentDataPath, hashFileName);

            using (var ms = new MemoryStream())
            using (var sw = new StreamWriter(ms, Encoding.UTF8))
            {
                sw.WriteLine("# STATIHASH v1");
                sw.WriteLine("# Compact recovery plate");
                sw.WriteLine("# Format: vID_xX_yY_zZ or VID_xX_yY_zZ");
                sw.WriteLine();

                for (int i = 0; i < vertexControllers.Length; i++)
                {
                    GameObject v = vertexControllers[i];
                    if (v == null)
                        continue;

                    bool isPushed = pushedVertices.Contains(v);
                    char prefix = isPushed ? 'v' : 'V';

                    Vector3 curr = v.transform.position;
                    string id = i.ToString("D4");

                    sw.Write(prefix);
                    sw.Write(id);
                    sw.Write("_x");
                    sw.Write(curr.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
                    sw.Write("_y");
                    sw.Write(curr.y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
                    sw.Write("_z");
                    sw.Write(curr.z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
                    sw.WriteLine();
                }

                sw.Flush();

                // Compute SHA256 of the vertex plate content
                ms.Position = 0;
                string hashHex;
                using (var sha = SHA256.Create())
                {
                    byte[] data = ms.ToArray();
                    byte[] hash = sha.ComputeHash(data);
                    StringBuilder sb = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++)
                        sb.Append(hash[i].ToString("X2"));
                    hashHex = sb.ToString();
                }

                // Now actually write to disk: plate + matrix hash
                using (var final = new StreamWriter(path, false, Encoding.UTF8))
                {
                    ms.Position = 0;
                    using (var reader = new StreamReader(ms, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                            final.WriteLine(line);
                    }

                    final.WriteLine();
                    final.WriteLine("[HASH_MATRIX]");
                    for (int i = 0; i < hashHex.Length; i += 16)
                    {
                        int len = Mathf.Min(16, hashHex.Length - i);
                        final.WriteLine(hashHex.Substring(i, len));
                    }
                    final.WriteLine("[/HASH_MATRIX]");
                }
            }

#if UNITY_EDITOR
            Debug.Log($"[Sihouetter] Hash stamp written to: {path}");
#endif
        }

        // ============================================================
        // STAMPING: BLOB (TINY XXYY GRID)
        // ============================================================

        private void WriteBlobStampToFile()
        {
            if (!TryGetBlobCentroid(out var pix))
                return;

            if (maskWidth <= 1 || maskHeight <= 1)
                return;

            float u = pix.x / (maskWidth - 1);
            float v = pix.y / (maskHeight - 1);

            // Quantize to 10x10 grid -> XXYY (00–09)
            int gx = Mathf.Clamp(Mathf.RoundToInt(u * 9f), 0, 9);
            int gy = Mathf.Clamp(Mathf.RoundToInt(v * 9f), 0, 9);

            string code = $"{gx:00}{gy:00}";

            string path = Path.Combine(Application.persistentDataPath, blobFileName);
            File.WriteAllText(path, code, Encoding.UTF8);

#if UNITY_EDITOR
            Debug.Log($"[Sihouetter] Blob stamp {code} written to: {path}");
#endif
        }

        // ============================================================
        // STAMPING: LOAD HASH (ON SAVE/LOAD ENABLE)
        // ============================================================

        private void TryLoadFromHashFile()
        {
            if (vertexControllers == null || vertexControllers.Length == 0)
                return;

            string path = Path.Combine(Application.persistentDataPath, hashFileName);
            if (!File.Exists(path))
                return;

            try
            {
                string[] lines = File.ReadAllLines(path);
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;
                    if (line.StartsWith("#"))
                        continue;
                    if (line.StartsWith("["))
                        continue;

                    // Expect: vID_xX_yY_zZ or VID_xX_yY_zZ
                    char prefix = line[0];
                    if (prefix != 'v' && prefix != 'V')
                        continue;

                    int idEnd = line.IndexOf("_x", StringComparison.Ordinal);
                    if (idEnd < 0)
                        continue;

                    string idStr = line.Substring(1, idEnd - 1);
                    if (!int.TryParse(idStr, out int idx))
                        continue;

                    if (idx < 0 || idx >= vertexControllers.Length)
                        continue;

                    int xIndex = line.IndexOf("_x", StringComparison.Ordinal);
                    int yIndex = line.IndexOf("_y", StringComparison.Ordinal);
                    int zIndex = line.IndexOf("_z", StringComparison.Ordinal);
                    if (xIndex < 0 || yIndex < 0 || zIndex < 0)
                        continue;

                    string xStr = line.Substring(xIndex + 2, yIndex - (xIndex + 2));
                    string yStr = line.Substring(yIndex + 2, zIndex - (yIndex + 2));
                    string zStr = line.Substring(zIndex + 2);

                    if (!float.TryParse(xStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x))
                        continue;
                    if (!float.TryParse(yStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                        continue;
                    if (!float.TryParse(zStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                        continue;

                    GameObject v = vertexControllers[idx];
                    if (v == null)
                        continue;

                    Vector3 pos = new Vector3(x, y, z);
                    v.transform.position = pos;

                    if (!originalVertexPositions.ContainsKey(v))
                        originalVertexPositions[v] = pos;

                    if (prefix == 'v')
                        pushedVertices.Add(v);
                    else
                        pushedVertices.Remove(v);
                }

#if UNITY_EDITOR
                Debug.Log($"[Sihouetter] Loaded state from hash file: {path}");
#endif
            }
            catch (Exception e)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"[Sihouetter] Failed to load hash file: {path}\n{e}");
#endif
            }
        }

        // ============================================================
        // STAMPING: ASCII (BIG, DETAILED, ON SAVE/LOAD DISABLE)
        // ============================================================

        private void WriteAsciiStampToFile()
        {
            if (vertexControllers == null || vertexControllers.Length == 0)
                return;

            string path = Path.Combine(Application.persistentDataPath, asciiFileName);

            using (StreamWriter sw = new StreamWriter(path, false, Encoding.UTF8))
            {
                sw.WriteLine("# StatiAscii v1");
                sw.WriteLine("# Big detailed ASCII dump");
                sw.WriteLine("# Format: index;isPushed;origX,origY,origZ;currX,currY,currZ");
                sw.WriteLine();

                for (int i = 0; i < vertexControllers.Length; i++)
                {
                    GameObject v = vertexControllers[i];
                    if (v == null)
                        continue;

                    originalVertexPositions.TryGetValue(v, out var orig);
                    Vector3 curr = v.transform.position;
                    bool isPushed = pushedVertices.Contains(v);

                    sw.WriteLine(
                        $"{i};{(isPushed ? 1 : 0)};" +
                        $"{orig.x:F6},{orig.y:F6},{orig.z:F6};" +
                        $"{curr.x:F6},{curr.y:F6},{curr.z:F6}"
                    );
                }

                sw.WriteLine();
                sw.WriteLine("# Silhouette Polygon (pixel space)");
                if (polygons != null && polygons.Count > 0)
                {
                    var poly = polygons[0];
                    for (int i = 0; i < poly.Count; i++)
                    {
                        Vector2 p = poly[i];
                        sw.WriteLine($"P;{p.x:F3},{p.y:F3}");
                    }
                }
            }

#if UNITY_EDITOR
            Debug.Log($"[Sihouetter] ASCII stamp written to: {path}");
#endif
        }

        // ============================================================
        // GIZMOS (2D CANVAS)
        // ============================================================

        private void OnDrawGizmos()
        {
            if (!draw2DContourGizmos)
                return;

            if (cvRawImage == null)
                return;

            if (polygons == null || polygons.Count == 0)
                return;

            if (ProcessingWidth <= 0 || ProcessingHeight <= 0)
                return;

            RectTransform rt = cvRawImage.rectTransform;
            Rect rect = rt.rect;

            Gizmos.color = Color.cyan;
            Gizmos.matrix = rt.localToWorldMatrix;

            foreach (var poly in polygons)
            {
                if (poly == null || poly.Count < 2)
                    continue;

                int count = poly.Count;
                for (int i = 0; i < count; i++)
                {
                    Vector2 a = poly[i];
                    Vector2 b = poly[(i + 1) % count];

                    float uA = a.x / (maskWidth - 1);
                    float vA = a.y / (maskHeight - 1);
                    float uB = b.x / (maskWidth - 1);
                    float vB = b.y / (maskHeight - 1);

                    float xA = (uA - 0.5f) * rect.width;
                    float yA = (vA - 0.5f) * rect.height;
                    float xB = (uB - 0.5f) * rect.width;
                    float yB = (vB - 0.5f) * rect.height;

                    Vector3 pA = new Vector3(xA, yA, 0f);
                    Vector3 pB = new Vector3(xB, yB, 0f);

                    Gizmos.DrawLine(pA, pB);
                }
            }
        }

        // ============================================================
        // GIZMOS (3D PROJECTION PYRAMID)
        // ============================================================

        private void OnDrawGizmosSelected()
        {
            if (!draw3DSilhouette)
                return;

            if (silhouetteCamera == null)
                return;

            if (polygons == null || polygons.Count == 0)
                return;

            int texW = ProcessingWidth;
            int texH = ProcessingHeight;

            if (texW <= 0 || texH <= 0)
                return;

            Vector3 camPos = silhouetteCamera.transform.position;

            foreach (var poly in polygons)
            {
                if (poly == null || poly.Count < 3)
                    continue;

                List<Vector3> worldPoints = new List<Vector3>(poly.Count);

                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 p = poly[i];

                    float u = p.x / (texW - 1);
                    float v = p.y / (texH - 1);

                    Vector3 vp = new Vector3(u, v, 1f);
                    Vector3 world = silhouetteCamera.ViewportToWorldPoint(vp);
                    worldPoints.Add(world);
                }

                Gizmos.color = Color.yellow;
                for (int i = 0; i < worldPoints.Count; i++)
                {
                    Vector3 a = worldPoints[i];
                    Vector3 b = worldPoints[(i + 1) % worldPoints.Count];
                    Gizmos.DrawLine(a, b);
                }

                Gizmos.color = Color.magenta;
                foreach (var wp in worldPoints)
                    Gizmos.DrawLine(camPos, wp);

                Gizmos.color = Color.cyan;
                float extendDist = gizmoDepth > 0f ? gizmoDepth : 5f;
                foreach (var wp in worldPoints)
                {
                    Vector3 dir = (wp - camPos).normalized;
                    Vector3 farPoint = camPos + dir * extendDist;
                    Gizmos.DrawLine(wp, farPoint);
                }
            }
        }

        // ============================================================
        // GEOMETRY HELPERS
        // ============================================================

        private bool PointInPolygon2D(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            int count = poly.Count;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Vector2 pi = poly[i];
                Vector2 pj = poly[j];

                bool intersect =
                    ((pi.y > p.y) != (pj.y > p.y)) &&
                    (p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y + 0.00001f) + pi.x);

                if (intersect)
                    inside = !inside;
            }

            return inside;
        }
    }
}
