using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum RuntimeTerrainMeshFidelity
{
    Lowest,
    Low,
    Medium,
    High
}

[ExecuteAlways]
public class RuntimeTerrainMeshGenerator : MonoBehaviour
{
    [Header("Targets")]
    public Transform meshTarget;
    public MeshFilter meshFilter;

    [Header("Resolution")]
    public int vertexResolution = 16;
    public bool resetGridOnResolutionChange = false;

    [Header("Controllers")]
    public GameObject vertexPrefab;
    public GameObject insiderPrefab;
    public GameObject outsiderPrefab;

    [Header("Hull / Watertight")]
    public bool maintainWatertightInsiders = true;
    public bool maintainWatertightOutsiders = true;
    public float thickness = 0.1f;

    [Header("Mesh")]
    public bool flipWindingZ = false;

    [Header("Runtime")]
    public float updateInterval = 0.1f;

    // ============================================================
    // DEGENERATE OCTAHEDRON MODE
    // ============================================================
    [Header("Degenerate Octahedron Mode")]
    public bool enableDegenerateOctahedron = false;

    [Tooltip("Base radius of the canonical 3x3 octahedron (extent from center to corner).")]
    public float octaBaseRadius = 0.5f;

    [Tooltip("If true, hull radius scales linearly with resolution so higher subdiv = larger hull.")]
    public bool scaleOctaWithResolution = true;

    [Header("Subdivision Controls")]
    public bool increaseSubdivision = false;
    public bool decreaseSubdivision = false;

    // ============================================================
    // SILHOUETTE EXTRUSION (SPIN TEST)
    // ============================================================
    [Header("Silhouette Extrusion (Spin Test)")]
    public bool enableSilhouetteExtrusion = false;
    public CVatGPT.Sihouetter sihouetter;
    public Camera silhouetteCamera;
    public float silhouettePushDistance = 1f;

    [Tooltip("If true, cached base hull positions will NOT be overwritten at runtime.")]
    public bool preventBaseHullOverwrite = false;

    Mesh mesh;
    int lastResolution = -1;

    // Track last octa mode to detect toggles
    bool lastDegenerateOctahedron = false;

    // NOTE: made public again so BorgCubeBuilder can see it
    public GameObject[] vertexControllers;
    Vector3[] cachedVertices;

    Dictionary<int, Vector3[]> levelPositionCache = new Dictionary<int, Vector3[]>();

    bool _runtimeRedetectionRequested = false;
    float _timeSinceLastUpdate = 0f;

    [System.Serializable]
    public class InsiderLink
    {
        public Transform vertexA;
        public Transform vertexB;
        public Transform insiderObject;
    }

    [System.Serializable]
    public class OutsiderLink
    {
        public Transform vertexA;
        public Transform vertexB;
        public Transform outsiderObject;
    }

    public List<InsiderLink> insiders;
    public List<OutsiderLink> outsiders;

    // --------------------------------------------------------------------
    // Compatibility surface for AutoWireRJOMesh
    // --------------------------------------------------------------------
    public MeshFilter MeshFilter => meshFilter;
    public MeshRenderer MeshRenderer =>
        meshTarget != null ? meshTarget.GetComponent<MeshRenderer>() : null;

    void OnEnable()
    {
        if (meshTarget == null)
            meshTarget = transform;

        if (!meshFilter)
            meshFilter = meshTarget.GetComponent<MeshFilter>();

        if (!meshFilter)
        {
            meshFilter = meshTarget.gameObject.AddComponent<MeshFilter>();
        }

        if (meshFilter.sharedMesh == null)
        {
            mesh = new Mesh { name = "GeneratedTerrainMesh" };
            meshFilter.sharedMesh = mesh;
        }
        else
        {
            mesh = meshFilter.sharedMesh;
        }

        int res = Mathf.Clamp(vertexResolution, 2, 1024);
        vertexResolution = res;

        // Ensure initial grid exists and respects current octa mode
        GenerateInitialGrid(res);
        lastResolution = res;
        lastDegenerateOctahedron = enableDegenerateOctahedron;

        GenerateMesh();
    }

    void Update()
    {
        // Detect octa mode toggle and rebuild grid immediately
        if (enableDegenerateOctahedron != lastDegenerateOctahedron)
        {
            lastDegenerateOctahedron = enableDegenerateOctahedron;
            int res = Mathf.Clamp(vertexResolution, 2, 1024);
            vertexResolution = res;

            GenerateInitialGrid(res);
            GenerateMesh();
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // Editor-time: allow manual subdivision flags
            if (increaseSubdivision)
            {
                increaseSubdivision = false;
                SubdivideUp();
            }
            if (decreaseSubdivision)
            {
                decreaseSubdivision = false;
                SubdivideDown();
            }

            GenerateMesh();
            return;
        }
#endif
        // Runtime: subdivision flags take priority and freeze+subdivide
        if (increaseSubdivision)
        {
            increaseSubdivision = false;
            SubdivideUp();
            GenerateMesh();
            return;
        }

        if (decreaseSubdivision)
        {
            decreaseSubdivision = false;
            SubdivideDown();
            GenerateMesh();
            return;
        }

        _timeSinceLastUpdate += Time.deltaTime;

        if (_runtimeRedetectionRequested || _timeSinceLastUpdate >= updateInterval)
        {
            _timeSinceLastUpdate = 0f;
            _runtimeRedetectionRequested = false;
            GenerateMesh();
        }
    }

    Transform GetOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (!child)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            child = go.transform;
        }
        return child;
    }

    void ClearChildren(Transform t)
    {
        if (!t) return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                DestroyImmediate(t.GetChild(i).gameObject);
        }
        else
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Destroy(t.GetChild(i).gameObject);
        }
#else
        for (int i = t.childCount - 1; i >= 0; i--)
            Destroy(t.GetChild(i).gameObject);
#endif
    }

    Vector3[] GetCachedPositionsOrCurrent(int res)
    {
        Vector3[] positions;
        if (levelPositionCache.TryGetValue(res, out positions) && positions != null && positions.Length == res * res)
            return positions;

        if (vertexControllers == null || vertexControllers.Length != res * res)
            return null;

        positions = new Vector3[res * res];
        for (int i = 0; i < res * res; i++)
        {
            GameObject c = vertexControllers[i];
            if (c != null)
            {
                Vector3 world = c.transform.position;
                Vector3 local = meshTarget.transform.InverseTransformPoint(world);
                positions[i] = local;
            }
            else
            {
                positions[i] = Vector3.zero;
            }
        }

        levelPositionCache[res] = positions;
        return positions;
    }

    void CacheCurrentLevelPositions(int res)
    {
        if (preventBaseHullOverwrite)
            return;

        if (vertexControllers == null || vertexControllers.Length != res * res)
            return;

        Vector3[] positions = new Vector3[res * res];
        for (int i = 0; i < res * res; i++)
        {
            GameObject c = vertexControllers[i];
            if (c != null)
            {
                Vector3 world = c.transform.position;
                Vector3 local = meshTarget.transform.InverseTransformPoint(world);
                positions[i] = local;
            }
            else
            {
                positions[i] = Vector3.zero;
            }
        }

        levelPositionCache[res] = positions;
    }

    // ============================================================
    // DEGENERATE OCTAHEDRON MAPPING (WITH INFLATION)
    // ============================================================

    Vector3 MapToDegenerateOctahedron(int x, int y, int res)
    {
        if (res <= 1) return Vector3.zero;

        float u = (2f * x / (res - 1)) - 1f;
        float v = (2f * y / (res - 1)) - 1f;

        float absU = Mathf.Abs(u);
        float absV = Mathf.Abs(v);

        float w = 1f - absU - absV;

        Vector3 p;

        if (w >= 0f)
        {
            p = new Vector3(u, v, w);
        }
        else
        {
            p = new Vector3(
                (1f - absV) * Mathf.Sign(u),
                (1f - absU) * Mathf.Sign(v),
                w
            );
        }

        float norm = Mathf.Abs(p.x) + Mathf.Abs(p.y) + Mathf.Abs(p.z);
        if (norm > 0f)
            p /= norm;

        float scaleFactor = octaBaseRadius;

        if (scaleOctaWithResolution)
        {
            const float baseRes = 3f;
            float stepsFromBase = (res - 1f) / (baseRes - 1f);
            scaleFactor *= Mathf.Max(stepsFromBase, 0.0001f);
        }

        p *= scaleFactor;

        return p;
    }

    // ============================================================
    // INITIAL GRID
    // ============================================================

    void GenerateInitialGrid(int res)
    {
        res = Mathf.Clamp(res, 2, 1024);
        vertexResolution = res;
        lastResolution = res;

        Transform vertexFolder   = GetOrCreateChild(meshTarget.transform, "Vertexes");
        Transform insiderFolder  = GetOrCreateChild(meshTarget.transform, "Insiders");
        Transform outsiderFolder = GetOrCreateChild(meshTarget.transform, "Outsiders");

        ClearChildren(vertexFolder);
        ClearChildren(insiderFolder);
        ClearChildren(outsiderFolder);

        GameObject[] controllers = new GameObject[res * res];
        Vector3[] positions = new Vector3[res * res];

        float step = 1f;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                int i = y * res + x;

#if UNITY_EDITOR
                GameObject v;
                if (!Application.isPlaying)
                    v = (GameObject)PrefabUtility.InstantiatePrefab(vertexPrefab);
                else
                    v = Instantiate(vertexPrefab);
#else
                GameObject v = Instantiate(vertexPrefab);
#endif
                v.transform.SetParent(vertexFolder, false);

                Vector3 local;
                if (enableDegenerateOctahedron)
                    local = MapToDegenerateOctahedron(x, y, res);
                else
                    local = new Vector3(x * step, 0f, y * step);

                Vector3 world = meshTarget.transform.TransformPoint(local);
                v.transform.position = world;
                v.name = $"Vertex ({i})";

                controllers[i] = v;
                positions[i] = local;
            }
        }

        vertexControllers = controllers;
        levelPositionCache[res] = positions;

        BuildInsidersOutsiders(res, vertexControllers, insiderFolder, outsiderFolder);
    }

    void BuildInsidersOutsiders(int res, GameObject[] controllers, Transform insiderFolder, Transform outsiderFolder)
    {
        insiders = new List<InsiderLink>();
        outsiders = new List<OutsiderLink>();

        ClearChildren(insiderFolder);
        ClearChildren(outsiderFolder);

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                int i = y * res + x;

                if (x < res - 1)
                {
                    int j = y * res + (x + 1);

                    Transform a = controllers[i].transform;
                    Transform b = controllers[j].transform;

                    if (maintainWatertightInsiders && insiderPrefab != null)
                    {
#if UNITY_EDITOR
                        GameObject go;
                        if (!Application.isPlaying)
                            go = (GameObject)PrefabUtility.InstantiatePrefab(insiderPrefab);
                        else
                            go = Instantiate(insiderPrefab);
#else
                        GameObject go = Instantiate(insiderPrefab);
#endif
                        go.transform.SetParent(insiderFolder, false);

                        InsiderLink link = new InsiderLink
                        {
                            vertexA = a,
                            vertexB = b,
                            insiderObject = go.transform
                        };
                        insiders.Add(link);
                    }

                    if (maintainWatertightOutsiders && outsiderPrefab != null)
                    {
#if UNITY_EDITOR
                        GameObject go;
                        if (!Application.isPlaying)
                            go = (GameObject)PrefabUtility.InstantiatePrefab(outsiderPrefab);
                        else
                            go = Instantiate(outsiderPrefab);
#else
                        GameObject go = Instantiate(outsiderPrefab);
#endif
                        go.transform.SetParent(outsiderFolder, false);

                        OutsiderLink link = new OutsiderLink
                        {
                            vertexA = a,
                            vertexB = b,
                            outsiderObject = go.transform
                        };
                        outsiders.Add(link);
                    }
                }

                if (y < res - 1)
                {
                    int j = (y + 1) * res + x;

                    Transform a = controllers[i].transform;
                    Transform b = controllers[j].transform;

                    if (maintainWatertightInsiders && insiderPrefab != null)
                    {
#if UNITY_EDITOR
                        GameObject go;
                        if (!Application.isPlaying)
                            go = (GameObject)PrefabUtility.InstantiatePrefab(insiderPrefab);
                        else
                            go = Instantiate(insiderPrefab);
#else
                        GameObject go = Instantiate(insiderPrefab);
#endif
                        go.transform.SetParent(insiderFolder, false);

                        InsiderLink link = new InsiderLink
                        {
                            vertexA = a,
                            vertexB = b,
                            insiderObject = go.transform
                        };
                        insiders.Add(link);
                    }

                    if (maintainWatertightOutsiders && outsiderPrefab != null)
                    {
#if UNITY_EDITOR
                        GameObject go;
                        if (!Application.isPlaying)
                            go = (GameObject)PrefabUtility.InstantiatePrefab(outsiderPrefab);
                        else
                            go = Instantiate(outsiderPrefab);
#else
                        GameObject go = Instantiate(outsiderPrefab);
#endif
                        go.transform.SetParent(outsiderFolder, false);

                        OutsiderLink link = new OutsiderLink
                        {
                            vertexA = a,
                            vertexB = b,
                            outsiderObject = go.transform
                        };
                        outsiders.Add(link);
                    }
                }
            }
        }
    }

    // ============================================================
    // SUBDIVISION
    // ============================================================

    void SubdivideUp()
    {
        int oldRes = Mathf.Clamp(vertexResolution, 2, 1024);
        int newRes = Mathf.Clamp(oldRes * 2, 2, 1024);
        if (newRes == oldRes) return;

        vertexResolution = newRes;

        Vector3[] newPositions = null;
        if (!levelPositionCache.TryGetValue(newRes, out newPositions) || newPositions == null || newPositions.Length != newRes * newRes)
        {
            if (enableDegenerateOctahedron)
            {
                newPositions = new Vector3[newRes * newRes];
                for (int y = 0; y < newRes; y++)
                {
                    for (int x = 0; x < newRes; x++)
                    {
                        int i = y * newRes + x;
                        newPositions[i] = MapToDegenerateOctahedron(x, y, newRes);
                    }
                }
                levelPositionCache[newRes] = newPositions;
            }
            else
            {
                if (vertexControllers == null || vertexControllers.Length != oldRes * oldRes)
                {
                    GenerateInitialGrid(newRes);
                    return;
                }

                Vector3[] oldPositions = GetCachedPositionsOrCurrent(oldRes);
                if (oldPositions == null)
                {
                    GenerateInitialGrid(newRes);
                    return;
                }

                newPositions = new Vector3[newRes * newRes];

                for (int y = 0; y < newRes; y++)
                {
                    for (int x = 0; x < newRes; x++)
                    {
                        int i = y * newRes + x;

                        float u = (float)x / (newRes - 1);
                        float v = (float)y / (newRes - 1);

                        float oldX = u * (oldRes - 1);
                        float oldY = v * (oldRes - 1);

                        int x0 = Mathf.FloorToInt(oldX);
                        int y0 = Mathf.FloorToInt(oldY);
                        int x1 = Mathf.Min(x0 + 1, oldRes - 1);
                        int y1 = Mathf.Min(y0 + 1, oldRes - 1);

                        float tx = oldX - x0;
                        float ty = oldY - y0;

                        Vector3 p00 = oldPositions[y0 * oldRes + x0];
                        Vector3 p10 = oldPositions[y0 * oldRes + x1];
                        Vector3 p01 = oldPositions[y1 * oldRes + x0];
                        Vector3 p11 = oldPositions[y1 * oldRes + x1];

                        Vector3 p0 = Vector3.Lerp(p00, p10, tx);
                        Vector3 p1 = Vector3.Lerp(p01, p11, tx);
                        Vector3 p  = Vector3.Lerp(p0,  p1,  ty);

                        newPositions[i] = p;
                    }
                }

                levelPositionCache[newRes] = newPositions;
            }
        }

        Transform vertexFolder   = GetOrCreateChild(meshTarget.transform, "Vertexes");
        Transform insiderFolder  = GetOrCreateChild(meshTarget.transform, "Insiders");
        Transform outsiderFolder = GetOrCreateChild(meshTarget.transform, "Outsiders");

        GameObject[] oldControllers = vertexControllers;
        GameObject[] newControllers = new GameObject[newRes * newRes];

        if (!enableDegenerateOctahedron)
        {
            if (oldControllers == null || oldControllers.Length != oldRes * oldRes)
            {
                GenerateInitialGrid(newRes);
                return;
            }
        }

        for (int y = 0; y < newRes; y++)
        {
            for (int x = 0; x < newRes; x++)
            {
                int i = y * newRes + x;

                GameObject vObj = null;

                if (!enableDegenerateOctahedron)
                {
                    int ox = x / 2;
                    int oy = y / 2;

                    bool xEven = (x % 2 == 0);
                    bool yEven = (y % 2 == 0);

                    if (xEven && yEven)
                    {
                        int oldIndex = oy * oldRes + ox;
                        GameObject existing = oldControllers[oldIndex];
                        newControllers[i] = existing;
                        existing.transform.SetParent(vertexFolder, false);

                        Vector3 local = newPositions[i];
                        Vector3 world = meshTarget.transform.TransformPoint(local);
                        existing.transform.position = world;
                        existing.name = $"Vertex ({i})";
                        continue;
                    }
                }

#if UNITY_EDITOR
                if (!Application.isPlaying)
                    vObj = (GameObject)PrefabUtility.InstantiatePrefab(vertexPrefab);
                else
                    vObj = Instantiate(vertexPrefab);
#else
                vObj = Instantiate(vertexPrefab);
#endif
                vObj.transform.SetParent(vertexFolder, false);

                Vector3 localPos = newPositions[i];
                Vector3 worldPos = meshTarget.transform.TransformPoint(localPos);
                vObj.transform.position = worldPos;
                vObj.name = $"Vertex ({i})";
                newControllers[i] = vObj;
            }
        }

        if (!enableDegenerateOctahedron && oldControllers != null)
        {
            HashSet<GameObject> survivors = new HashSet<GameObject>(newControllers);
            for (int i = 0; i < oldControllers.Length; i++)
            {
                GameObject c = oldControllers[i];
                if (c != null && !survivors.Contains(c))
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        DestroyImmediate(c);
                    else
                        Destroy(c);
#else
                    Destroy(c);
#endif
                }
            }
        }

        vertexControllers = newControllers;

        BuildInsidersOutsiders(newRes, vertexControllers, insiderFolder, outsiderFolder);
        lastResolution = newRes;
    }

    void SubdivideDown()
    {
        int oldRes = Mathf.Clamp(vertexResolution, 2, 1024);
        if (oldRes <= 2) return;

        int newRes = Mathf.Clamp(oldRes / 2, 2, 1024);
        if (newRes == oldRes) return;

        vertexResolution = newRes;

        Vector3[] newPositions = null;
        if (!levelPositionCache.TryGetValue(newRes, out newPositions) || newPositions == null || newPositions.Length != newRes * newRes)
        {
            if (enableDegenerateOctahedron)
            {
                newPositions = new Vector3[newRes * newRes];
                for (int y = 0; y < newRes; y++)
                {
                    for (int x = 0; x < newRes; x++)
                    {
                        int i = y * newRes + x;
                        newPositions[i] = MapToDegenerateOctahedron(x, y, newRes);
                    }
                }
                levelPositionCache[newRes] = newPositions;
            }
            else
            {
                if (vertexControllers == null || vertexControllers.Length != oldRes * oldRes)
                {
                    Debug.LogError("[RTMG] Cannot subdivide down: missing controllers.");
                    return;
                }

                newPositions = new Vector3[newRes * newRes];
                for (int y = 0; y < newRes; y++)
                {
                    for (int x = 0; x < newRes; x++)
                    {
                        int i = y * newRes + x;
                        int fx = x * 2;
                        int fy = y * 2;
                        int fi = fy * oldRes + fx;

                        Vector3 world = vertexControllers[fi].transform.position;
                        Vector3 local = meshTarget.transform.InverseTransformPoint(world);
                        newPositions[i] = local;
                    }
                }

                levelPositionCache[newRes] = newPositions;
            }
        }

        Transform vertexFolder   = GetOrCreateChild(meshTarget.transform, "Vertexes");
        Transform insiderFolder  = GetOrCreateChild(meshTarget.transform, "Insiders");
        Transform outsiderFolder = GetOrCreateChild(meshTarget.transform, "Outsiders");

        GameObject[] oldControllers = vertexControllers;
        GameObject[] newControllers = new GameObject[newRes * newRes];

        if (!enableDegenerateOctahedron)
        {
            if (oldControllers == null || oldControllers.Length != oldRes * oldRes)
            {
                GenerateInitialGrid(newRes);
                return;
            }
        }

        for (int y = 0; y < newRes; y++)
        {
            for (int x = 0; x < newRes; x++)
            {
                int i = y * newRes + x;

                GameObject existing = null;

                if (!enableDegenerateOctahedron)
                {
                    int fx = x * 2;
                    int fy = y * 2;
                    int fi = fy * oldRes + fx;

                    existing = oldControllers[fi];
                    if (existing == null)
                    {
#if UNITY_EDITOR
                        if (!Application.isPlaying)
                            existing = (GameObject)PrefabUtility.InstantiatePrefab(vertexPrefab);
                        else
                            existing = Instantiate(vertexPrefab);
#else
                        existing = Instantiate(vertexPrefab);
#endif
                    }
                }
                else
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        existing = (GameObject)PrefabUtility.InstantiatePrefab(vertexPrefab);
                    else
                        existing = Instantiate(vertexPrefab);
#else
                    existing = Instantiate(vertexPrefab);
#endif
                }

                existing.transform.SetParent(vertexFolder, false);

                Vector3 local = newPositions[i];
                Vector3 world = meshTarget.transform.TransformPoint(local);
                existing.transform.position = world;
                existing.name = $"Vertex ({i})";

                newControllers[i] = existing;
            }
        }

        if (!enableDegenerateOctahedron && oldControllers != null)
        {
            HashSet<GameObject> survivors = new HashSet<GameObject>(newControllers);
            for (int i = 0; i < oldControllers.Length; i++)
            {
                GameObject c = oldControllers[i];
                if (c != null && !survivors.Contains(c))
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        DestroyImmediate(c);
                    else
                        Destroy(c);
#else
                    Destroy(c);
#endif
                }
            }
        }

        vertexControllers = newControllers;

        BuildInsidersOutsiders(newRes, vertexControllers, insiderFolder, outsiderFolder);
        lastResolution = newRes;
    }

    void HandleResolutionChangeIfNeeded()
    {
        int res = Mathf.Clamp(vertexResolution, 2, 1024);

        if (lastResolution == -1)
        {
            GenerateInitialGrid(res);
            lastResolution = res;
            return;
        }

        if (res == lastResolution)
            return;

        if (resetGridOnResolutionChange)
        {
            GenerateInitialGrid(res);
        }
        else
        {
            if (vertexControllers == null || vertexControllers.Length != lastResolution * lastResolution)
            {
                GenerateInitialGrid(res);
            }
            else
            {
                Transform vertexFolder   = GetOrCreateChild(meshTarget.transform, "Vertexes");
                Transform insiderFolder  = GetOrCreateChild(meshTarget.transform, "Insiders");
                Transform outsiderFolder = GetOrCreateChild(meshTarget.transform, "Outsiders");

                Vector3[] newPositions = new Vector3[res * res];

                if (enableDegenerateOctahedron)
                {
                    for (int y = 0; y < res; y++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            int i = y * res + x;
                            newPositions[i] = MapToDegenerateOctahedron(x, y, res);
                        }
                    }
                }
                else
                {
                    Vector3[] oldPositions = GetCachedPositionsOrCurrent(lastResolution);
                    if (oldPositions == null)
                    {
                        GenerateInitialGrid(res);
                        lastResolution = res;
                        return;
                    }

                    float oldMax = (lastResolution - 1);
                    float newMax = (res - 1);

                    for (int y = 0; y < res; y++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            int i = y * res + x;

                            float u = (res == 1) ? 0f : (float)x / newMax;
                            float v = (res == 1) ? 0f : (float)y / newMax;

                            float oldX = u * oldMax;
                            float oldY = v * oldMax;

                            int x0 = Mathf.FloorToInt(oldX);
                            int y0 = Mathf.FloorToInt(oldY);
                            int x1 = Mathf.Min(x0 + 1, lastResolution - 1);
                            int y1 = Mathf.Min(y0 + 1, lastResolution - 1);

                            float tx = oldX - x0;
                            float ty = oldY - y0;

                            Vector3 p00 = oldPositions[y0 * lastResolution + x0];
                            Vector3 p10 = oldPositions[y0 * lastResolution + x1];
                            Vector3 p01 = oldPositions[y1 * lastResolution + x0];
                            Vector3 p11 = oldPositions[y1 * lastResolution + x1];

                            Vector3 p0 = Vector3.Lerp(p00, p10, tx);
                            Vector3 p1 = Vector3.Lerp(p01, p11, tx);
                            Vector3 p  = Vector3.Lerp(p0,  p1,  ty);

                            newPositions[i] = p;
                        }
                    }
                }

                levelPositionCache[res] = newPositions;

                ClearChildren(vertexFolder);

                GameObject[] newControllers = new GameObject[res * res];

                for (int y = 0; y < res; y++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        int i = y * res + x;

                        GameObject v;
#if UNITY_EDITOR
                        if (!Application.isPlaying)
                            v = (GameObject)PrefabUtility.InstantiatePrefab(vertexPrefab);
                        else
                            v = Instantiate(vertexPrefab);
#else
                        v = Instantiate(vertexPrefab);
#endif
                        v.transform.SetParent(vertexFolder, false);

                        Vector3 local = newPositions[i];
                        Vector3 world = meshTarget.transform.TransformPoint(local);
                        v.transform.position = world;
                        v.name = $"Vertex ({i})";
                        newControllers[i] = v;
                    }
                }

                vertexControllers = newControllers;
                BuildInsidersOutsiders(res, vertexControllers, insiderFolder, outsiderFolder);
            }
        }

        lastResolution = res;
    }

    public void GenerateMesh()
    {
        HandleResolutionChangeIfNeeded();

        if (!meshTarget)
            return;

        if (!meshFilter)
            meshFilter = meshTarget.GetComponent<MeshFilter>();

        if (!meshFilter)
            return;

        if (mesh == null)
            mesh = meshFilter.sharedMesh != null ? meshFilter.sharedMesh : new Mesh { name = "GeneratedTerrainMesh" };

        int res = Mathf.Clamp(vertexResolution, 2, 1024);
        vertexResolution = res;
        int count = res * res;

        if (cachedVertices == null || cachedVertices.Length != count)
            cachedVertices = new Vector3[count];

        if (vertexControllers == null || vertexControllers.Length != count)
        {
            GenerateInitialGrid(res);
        }

        if (vertexControllers == null || vertexControllers.Length != count)
            return;

        Vector3[] vertices = new Vector3[count];
        Vector2[] uvs = new Vector2[count];
        int[] triangles = new int[(res - 1) * (res - 1) * 6];

        float step = 1f;
        float uvStep = (res > 1) ? 1f / (res - 1) : 1f;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                int i = y * res + x;

                Vector3 basePos = new Vector3(x * step, 0f, y * step);
                Vector3 v = basePos;

                GameObject controller = vertexControllers[i];

                if (controller != null && controller.activeInHierarchy)
                {
                    Vector3 worldPos = controller.transform.position;

                    if (float.IsNaN(worldPos.x) || float.IsNaN(worldPos.y) || float.IsNaN(worldPos.z))
                    {
                        Debug.LogError($"[RTMG] NaN controller position at index {i} on {controller.name}", controller);
                        worldPos = basePos;
                    }

                    v = meshTarget.transform.InverseTransformPoint(worldPos);

                    if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z))
                    {
                        Debug.LogError($"[RTMG] NaN after InverseTransformPoint at index {i} on {meshTarget.name}", meshTarget);
                        v = basePos;
                    }

                    cachedVertices[i] = v;
                }
                else
                {
                    Vector3 cached = cachedVertices[i];

                    if (float.IsNaN(cached.x) || float.IsNaN(cached.y) || float.IsNaN(cached.z))
                    {
                        Debug.LogError($"[RTMG] NaN in cachedVertices at index {i}, resetting to basePos.", this);
                        cached = basePos;
                        cachedVertices[i] = cached;
                    }

                    if (cached == Vector3.zero)
                        cached = basePos;

                    v = cached;
                }

                vertices[i] = v;

                float u = (res > 1) ? (x * uvStep) : 0f;
                float w = (res > 1) ? (y * uvStep) : 0f;
                uvs[i] = new Vector2(u, w);
            }
        }

        int tri = 0;
        for (int y = 0; y < res - 1; y++)
        {
            for (int x = 0; x < res - 1; x++)
            {
                int i = y * res + x;

                if (!flipWindingZ)
                {
                    triangles[tri++] = i;
                    triangles[tri++] = i + res + 1;
                    triangles[tri++] = i + res;

                    triangles[tri++] = i;
                    triangles[tri++] = i + 1;
                    triangles[tri++] = i + res + 1;
                }
                else
                {
                    triangles[tri++] = i;
                    triangles[tri++] = i + res;
                    triangles[tri++] = i + res + 1;

                    triangles[tri++] = i;
                    triangles[tri++] = i + res + 1;
                    triangles[tri++] = i + 1;
                }
            }
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (meshFilter.sharedMesh != mesh)
            meshFilter.sharedMesh = mesh;

        if (maintainWatertightInsiders && insiders != null)
        {
            foreach (var link in insiders)
                UpdateInsider(link);
        }

        if (maintainWatertightOutsiders && outsiders != null)
        {
            foreach (var link in outsiders)
                UpdateOutsider(link);
        }

        CacheCurrentLevelPositions(res);
    }

    void UpdateInsider(InsiderLink link)
    {
        if (link.vertexA == null || link.vertexB == null || link.insiderObject == null)
            return;

        Vector3 a = link.vertexA.position;
        Vector3 b = link.vertexB.position;

        float cx = (a.x + b.x) * 0.5f;
        float cy = (a.y + b.y) * 0.5f;
        float cz = (a.z + b.z) * 0.5f;

        float sx = Mathf.Abs(a.x - b.x);
        float sz = Mathf.Abs(a.z - b.z);

        float dy = Mathf.Abs(a.y - b.y);
        float sy = Mathf.Max(thickness, dy);

        link.insiderObject.position = new Vector3(cx, cy, cz);
        link.insiderObject.rotation = Quaternion.identity;
        link.insiderObject.localScale = new Vector3(sx, sy, sz);
    }

    void UpdateOutsider(OutsiderLink link)
    {
        if (link.vertexA == null || link.vertexB == null || link.outsiderObject == null)
            return;

        Vector3 a = link.vertexA.position;
        Vector3 b = link.vertexB.position;

        float cx = (a.x + b.x) * 0.5f;
        float cz = (a.z + b.z) * 0.5f;

        float topY    = Mathf.Max(a.y, b.y);
        float bottomY = Mathf.Min(a.y, b.y);
        float dy      = topY - bottomY;

        float sx = Mathf.Abs(a.x - b.x);
        float sz = Mathf.Abs(a.z - b.z);

        float sy;
        float cy;

        if (dy < 0.0001f)
        {
            sy = Mathf.Max(thickness, 1f);
            cy = (topY + bottomY) * 0.5f;
        }
        else
        {
            sy = Mathf.Max(thickness, dy);
            cy = (topY + bottomY) * 0.5f;
        }

        link.outsiderObject.position = new Vector3(cx, cy, cz);
        link.outsiderObject.rotation = Quaternion.identity;
        link.outsiderObject.localScale = new Vector3(sx, sy, sz);
    }

    // ============================================================
    // SILHOUETTE EXTRUSION HELPERS
    // ============================================================

    bool PointInPolygon2D(List<Vector2> poly, Vector2 p)
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

    public void ApplySilhouetteExtrusion()
    {
        if (!enableSilhouetteExtrusion)
            return;

        if (sihouetter == null || silhouetteCamera == null)
            return;

        if (!sihouetter.HasValidFrame())
            return;

        var polys = sihouetter.polygons;
        if (polys == null || polys.Count == 0)
            return;

        int res = Mathf.Clamp(vertexResolution, 2, 1024);
        if (vertexControllers == null || vertexControllers.Length != res * res)
            return;

        Vector3[] baseLocal = GetCachedPositionsOrCurrent(res);
        if (baseLocal == null || baseLocal.Length != res * res)
            return;

        int texW = sihouetter.ProcessingWidth;
        int texH = sihouetter.ProcessingHeight;

        for (int i = 0; i < vertexControllers.Length; i++)
        {
            GameObject controller = vertexControllers[i];
            if (controller == null)
                continue;

            Vector3 baseWorld = meshTarget.transform.TransformPoint(baseLocal[i]);

            Vector3 vp = silhouetteCamera.WorldToViewportPoint(baseWorld);
            if (vp.z <= 0f)
            {
                controller.transform.position = baseWorld;
                continue;
            }

            float px = vp.x * texW;
            float py = vp.y * texH;
            Vector2 pTex = new Vector2(px, py);

            bool hit = false;

            foreach (var poly in polys)
            {
                if (poly == null || poly.Count < 3)
                    continue;

                if (PointInPolygon2D(poly, pTex))
                {
                    hit = true;
                    break;
                }
            }

            if (hit)
            {
                Vector3 camPos = silhouetteCamera.transform.position;
                Vector3 dir = (baseWorld - camPos).normalized;
                controller.transform.position = baseWorld + dir * silhouettePushDistance;
            }
            else
            {
                controller.transform.position = baseWorld;
            }
        }

        RequestRuntimeRedetection();
    }

    // --------------------------------------------------------------------
    // PUBLIC API FOR OTHER SYSTEMS (Binder + Orchestrator)
    // --------------------------------------------------------------------

    public GameObject[] GetVertexControllers()
    {
        return vertexControllers;
    }

    public void RequestRuntimeRedetection()
    {
        _runtimeRedetectionRequested = true;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            GenerateMesh();
            _runtimeRedetectionRequested = false;
        }
#endif
    }

    public void SetFidelity(RuntimeTerrainMeshFidelity fidelity)
    {
        int targetRes;
        switch (fidelity)
        {
            case RuntimeTerrainMeshFidelity.Lowest:
                targetRes = 8;
                break;
            case RuntimeTerrainMeshFidelity.Low:
                targetRes = 16;
                break;
            case RuntimeTerrainMeshFidelity.Medium:
                targetRes = 32;
                break;
            case RuntimeTerrainMeshFidelity.High:
                targetRes = 64;
                break;
            default:
                targetRes = 32;
                break;
        }

        targetRes = Mathf.Clamp(targetRes, 2, 1024);

        if (vertexResolution == targetRes)
            return;

        vertexResolution = targetRes;
        HandleResolutionChangeIfNeeded();
        RequestRuntimeRedetection();
    }

    public void SetAdaptiveFidelity()
    {
        SetFidelity(RuntimeTerrainMeshFidelity.Medium);
    }
}
