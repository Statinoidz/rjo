using UnityEngine;

public partial class Sihouetter
{
    public static float ComputeInstability(byte[] mask, byte[] prevMask)
    {
        if (mask == null || prevMask == null || mask.Length != prevMask.Length)
            return 0f;

        int unstable = 0;
        int total = mask.Length;

        for (int i = 0; i < total; i++)
        {
            if (mask[i] != prevMask[i])
                unstable++;
        }

        return (float)unstable / total;
    }

    public static float SmoothInstability(float currentInstability, float previousSmooth)
    {
        // match RjoOctree expected signature
        return Mathf.Lerp(previousSmooth, currentInstability, 0.15f);
    }

    public static int CountForegroundPixels(byte[] mask)
    {
        if (mask == null) return 0;
        int count = 0;
        for (int i = 0; i < mask.Length; i++)
            if (mask[i] != 0) count++;
        return count;
    }

    public static float ForegroundFraction(byte[] mask)
    {
        if (mask == null) return 0f;
        int fg = CountForegroundPixels(mask);
        return (float)fg / mask.Length;
    }
}
