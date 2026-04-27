using UnityEngine;
using System.IO;
using CVatGPT; // for CVTargetManager / CVTrackedTarget

/// <summary>
/// Loads image targets from disk and registers them with the pure C# CV target database.
/// Now supports dev-flag-based loading, multi-path registration, overwrite behavior,
/// and physical size propagation to the instance manager.
/// </summary>
public class CVImageTargetLoader : MonoBehaviour
{
    [Header("Legacy single paths (optional)")]
    public string primaryPath;
    public string secondaryPath;

    [Header("Dev / Runtime loading")]
    [Tooltip("When true, will load all paths in 'pathsToLoad' once, then reset to false.")]
    public bool loadNow = false;

    [Tooltip("Absolute file paths to PNG/JPG image targets. You can paste these in Inspector or set them from your own UI.")]
    public string[] pathsToLoad;

    [Header("Anchor flag")]
    [Tooltip("If true, marks these targets as anchor-capable in the CV system.")]
    public bool anchor = false;

    [Header("Physical size (1 = 1 meter)")]
    [Tooltip("Width of the physical image target in meters.")]
    public float targetWidthMeters = 1f;

    [Tooltip("Height of the physical image target in meters.")]
    public float targetHeightMeters = 1f;

    void Start()
    {
        // Legacy behavior: optional auto-load of primary/secondary paths
        if (!string.IsNullOrEmpty(primaryPath) && File.Exists(primaryPath))
            LoadSingle(primaryPath);

        if (!string.IsNullOrEmpty(secondaryPath) && File.Exists(secondaryPath))
            LoadSingle(secondaryPath);
    }

    void Update()
    {
        if (!loadNow)
            return;

        // One-shot trigger
        loadNow = false;

        if (pathsToLoad == null || pathsToLoad.Length == 0)
            return;

        foreach (var path in pathsToLoad)
        {
            if (string.IsNullOrEmpty(path))
                continue;

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[CVImageTargetLoader] Path does not exist: {path}");
                continue;
            }

            LoadSingle(path);
        }
    }

    private void LoadSingle(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(bytes, false);
        tex.Apply(false, true);

        string targetName = Path.GetFileNameWithoutExtension(path);

        // --- REGISTER TARGET DATA ---
        CVTargetData data = new CVTargetData
        {
            name = targetName
        };

        // Overwrites automatically because database is keyed by name
        CVTargetDatabase.AddTarget(targetName, data, anchor);

        // --- REGISTER POSE STORAGE ---
        CVTrackingManager.RegisterTarget(targetName);

        // --- REGISTER WITH TARGET MANAGER (primary/secondary slots) ---
        var mgr = FindObjectOfType<CVTargetManager>();
        if (mgr != null)
            mgr.RegisterDynamicTarget(new CVTrackedTarget(targetName));

        // --- REGISTER TEXTURE IN REGISTRY (for prefab visuals) ---
        CVTargetDefinition def = new CVTargetDefinition
        {
            targetID = targetName,
            referenceImage = tex,
            aspectRatio = (float)tex.width / tex.height
        };
        CVTargetRegistry.Register(def);

        // --- REGISTER PHYSICAL SIZE WITH INSTANCE MANAGER ---
        var instMgr = FindObjectOfType<CVImageTargetInstanceManager>();
        if (instMgr != null)
            instMgr.RegisterPhysicalSize(targetName, targetWidthMeters, targetHeightMeters);

        Debug.Log($"[CVImageTargetLoader] Loaded CV target '{targetName}' from '{path}' (W={targetWidthMeters}m, H={targetHeightMeters}m)");
    }
}
