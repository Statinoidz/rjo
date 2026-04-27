using System.Collections.Generic;
using UnityEngine;

namespace CVatGPT
{
    public static class CVKeypointUtils
    {
        public static void Score(ref CVKeypoint kp)
        {
            if (kp.response < 0.01f)
                kp.quality = CVKeypointQuality.Invalid;
            else if (kp.response < 0.1f)
                kp.quality = CVKeypointQuality.Low;
            else if (kp.response < 0.3f)
                kp.quality = CVKeypointQuality.Medium;
            else
                kp.quality = CVKeypointQuality.High;
        }

        public static void ScoreAll(List<CVKeypoint> keypoints)
        {
            if (keypoints == null) return;

            for (int i = 0; i < keypoints.Count; i++)
            {
                var kp = keypoints[i];
                Score(ref kp);
                keypoints[i] = kp;
            }
        }
    }
}
