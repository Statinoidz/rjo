using UnityEngine;
using System.Collections.Generic;

public class CVInteriorUnlocker
{
    int stableFrameCount;

    public bool TryUnlock(
        List<Vector2> interiorPoints,
        float confidence,
        CVTrackingSettings settings)
    {
        if (interiorPoints == null)
            return false;

        if (interiorPoints.Count < settings.minInteriorPoints ||
            interiorPoints.Count > settings.maxInteriorPoints)
        {
            stableFrameCount = 0;
            return false;
        }

        if (confidence < settings.interiorConfidenceThreshold)
        {
            stableFrameCount = 0;
            return false;
        }

        stableFrameCount++;

        return stableFrameCount >= settings.stableFramesRequired;
    }

    public void Reset()
    {
        stableFrameCount = 0;
    }
}
