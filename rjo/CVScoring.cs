using UnityEngine;

public static class CVScoring
{
    public static float ScoreMatch(Texture2D frame, Texture2D reference)
    {
        if (frame == null || reference == null) return 0f;

        // 🔬 Placeholder math CV
        // Replace later with feature / silhouette / flow logic
        float ratio =
            Mathf.Min(frame.width, reference.width) /
            (float)Mathf.Max(frame.width, reference.width);

        return Mathf.Clamp01(ratio);
    }
}
