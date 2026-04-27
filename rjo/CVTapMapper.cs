using UnityEngine;

/// <summary>
/// Maps phone tap positions to CV-tracked mesh hits.
/// </summary>
public class CVTapMapper : MonoBehaviour
{
    [Header("Video/CV")]
    public AAAVideo aaaVideo;
    public LayerMask meshLayerMask;

    [Header("CV Threshold")]
    [Range(0f, 1f)]
    public float cvHitThreshold = 0.5f;

    [Header("Debug")]
    public bool verbose = false;

    /// <summary>
    /// Given a screen tap position, returns the world position on tracked mesh.
    /// </summary>
    public bool MapTapToMesh(Vector2 screenPos, out Vector3 hitWorldPos)
    {
        hitWorldPos = Vector3.zero;

        Camera cam = Camera.main;
        if (cam == null || aaaVideo == null)
            return false;

        Ray ray = cam.ScreenPointToRay(screenPos);

        // 1) Physics raycast (authoritative)
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, meshLayerMask))
        {
            hitWorldPos = hit.point;

            if (verbose)
                Debug.Log($"[CVTapMapper] Physics hit at {hitWorldPos}");

            return true;
        }

        // 2) CV fallback
        if (!aaaVideo.TryGetLatestCvSnapshot(out Texture2D snapshot) || snapshot == null)
            return false;

        float u = screenPos.x / Screen.width;
        float v = 1f - (screenPos.y / Screen.height); // Y FLIP

        Color pixel = snapshot.GetPixelBilinear(u, v);

        if (pixel.grayscale > cvHitThreshold)
        {
            // Approximate depth fallback
            hitWorldPos = ray.origin + ray.direction * 2f;

            if (verbose)
                Debug.Log($"[CVTapMapper] CV fallback hit at {hitWorldPos}");

            return true;
        }

        return false;
    }
}
