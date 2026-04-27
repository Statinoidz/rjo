using System;

public static class CVTrackingEvents
{
    public static Action<string> OnTargetUnlocked;
    public static Action OnVisionLost;
    public static Action OnVisionRestored;
    public static Action OnGyroEnabled;
    public static Action OnGyroDisabled;
}
