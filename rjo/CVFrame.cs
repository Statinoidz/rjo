using UnityEngine;

/// <summary>
/// Represents a single captured frame
/// </summary>
public sealed class CVFrame
{
    public readonly Texture2D image;
    public readonly float timestamp;

    public CVFrame(Texture2D img, float time)
    {
        image = img;
        timestamp = time;
    }
}
