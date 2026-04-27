using UnityEngine;

[CreateAssetMenu(menuName = "CVatGPT/Tracking Settings")]
public class CVTrackingSettings : ScriptableObject
{
    [Header("Unlocking")]
    public int minInteriorPoints = 25;
    public int maxInteriorPoints = 200;
    public float interiorConfidenceThreshold = 0.75f;
    public int stableFramesRequired = 5;

    [Header("Quad Tracking")]
    public float quadLossFidelityThreshold = 0.4f;
    public float maxQuadSkew = 0.25f;
    public float expectedAspectRatio = 1.0f;

    [Header("Gyro")]
    public float gyroTimeoutSeconds = 2.0f;
    public float maxTravelDistance = 3.0f;
    public float gyroSmoothing = 8.0f;
    public bool assumeCameraVertical = true;
}
