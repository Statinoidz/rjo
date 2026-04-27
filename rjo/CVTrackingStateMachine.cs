using UnityEngine;
using System.Collections.Generic;

public class CVTrackingStateMachine
{
    public CVTrackingPhase Phase { get; private set; }

    public CVFeatureUnlocker unlocker = new();
    public CVQuadTracker quadTracker = new();

    public bool gyroEnabled;
    public bool warningTrackingLost;

    public void ProcessFrame(
        List<Vector2> interiorPoints,
        float interiorConfidence,
        Vector2[] quadPoints)
    {
        switch (Phase)
        {
            case CVTrackingPhase.Idle:
                Phase = CVTrackingPhase.Unlocking;
                break;

            case CVTrackingPhase.Unlocking:
                if (unlocker.TryUnlock(interiorPoints, interiorConfidence))
                {
                    Phase = CVTrackingPhase.CornerTracking;
                    unlocker.Reset();
                }
                break;

            case CVTrackingPhase.CornerTracking:
                if (!quadTracker.Update(quadPoints, out _))
                {
                    EnterGyroFallback();
                }
                break;

            case CVTrackingPhase.GyroFallback:
                // Vision recovery handled externally
                break;
        }
    }

    void EnterGyroFallback()
    {
        Phase = CVTrackingPhase.GyroFallback;
        gyroEnabled = true;
        warningTrackingLost = true;
    }

    public void RestoreVision()
    {
        gyroEnabled = false;
        warningTrackingLost = false;
        Phase = CVTrackingPhase.CornerTracking;
    }
}
