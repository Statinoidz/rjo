using UnityEngine;

namespace CVatGPT
{
    public static class CVPoseUtils
    {
        // Use the canonical identity
        public static CVPose Identity() => CVPose.Identity;

        public static CVPose Copy(CVPose src) => src;

        /// <summary>
        /// Normalized linear interpolation with quaternion hemisphere correction
        /// </summary>
        public static CVPose NlerpPose(CVPose a, CVPose b, float alpha)
        {
            // Position
            CVPose r = new CVPose
            {
                px = Mathf.Lerp(a.px, b.px, alpha),
                py = Mathf.Lerp(a.py, b.py, alpha),
                pz = Mathf.Lerp(a.pz, b.pz, alpha),
                confidence = Mathf.Lerp(a.confidence, b.confidence, alpha)
            };

            // Quaternion hemisphere correction
            float dot =
                a.qx * b.qx +
                a.qy * b.qy +
                a.qz * b.qz +
                a.qw * b.qw;

            float bx = b.qx;
            float by = b.qy;
            float bz = b.qz;
            float bw = b.qw;

            if (dot < 0f)
            {
                bx = -bx;
                by = -by;
                bz = -bz;
                bw = -bw;
            }

            r.qx = Mathf.Lerp(a.qx, bx, alpha);
            r.qy = Mathf.Lerp(a.qy, by, alpha);
            r.qz = Mathf.Lerp(a.qz, bz, alpha);
            r.qw = Mathf.Lerp(a.qw, bw, alpha);

            NormalizeQuat(ref r);
            return r;
        }

        public static void NormalizeQuat(ref CVPose p)
        {
            float mag = Mathf.Sqrt(
                p.qx * p.qx +
                p.qy * p.qy +
                p.qz * p.qz +
                p.qw * p.qw
            );

            if (mag < 1e-6f)
            {
                p.qx = 0f;
                p.qy = 0f;
                p.qz = 0f;
                p.qw = 1f;
                return;
            }

            float inv = 1f / mag;
            p.qx *= inv;
            p.qy *= inv;
            p.qz *= inv;
            p.qw *= inv;
        }

        public static Quaternion ToUnityQuat(CVPose p)
            => new Quaternion(p.qx, p.qy, p.qz, p.qw);

        public static void FromUnityQuat(ref CVPose p, Quaternion q)
        {
            p.qx = q.x;
            p.qy = q.y;
            p.qz = q.z;
            p.qw = q.w;
        }

        /// <summary>
        /// Proper exponential-map angular velocity integration
        /// </summary>
        public static Quaternion DeltaQuatFromAngVel(Vector3 angVelRad, float deltaTime)
        {
            float angle = angVelRad.magnitude * deltaTime;
            if (angle < 1e-6f)
                return Quaternion.identity;

            Vector3 axis = angVelRad.normalized;
            return Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis);
        }
    }
}
