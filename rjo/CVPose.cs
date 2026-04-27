using UnityEngine;
using System.Collections.Generic;

namespace CVatGPT
{
    /// <summary>
    /// Lightweight CV pose container.
    /// px/py/pz + qx/qy/qz/qw are the authoritative fields.
    /// </summary>
    public struct CVPose
    {
        // Authoritative raw fields (used by trackers)
        public float px, py, pz;
        public float qx, qy, qz, qw;

        public float confidence;
        public List<CVKeypoint> keypoints;

        // -------------------- PROPERTIES --------------------

        public Vector3 Position
        {
            get => new Vector3(px, py, pz);
            set
            {
                px = value.x;
                py = value.y;
                pz = value.z;
            }
        }

        public Quaternion Rotation
        {
            get => new Quaternion(qx, qy, qz, qw);
            set
            {
                qx = value.x;
                qy = value.y;
                qz = value.z;
                qw = value.w;
            }
        }

        public List<CVKeypoint> Keypoints
        {
            get
            {
                keypoints ??= new List<CVKeypoint>();
                return keypoints;
            }
            set => keypoints = value;
        }

        public bool IsValid => confidence > 0.001f;

        // -------------------- PRESETS --------------------

        public static CVPose Identity => new CVPose
        {
            px = 0f,
            py = 0f,
            pz = 0f,
            qx = 0f,
            qy = 0f,
            qz = 0f,
            qw = 1f,
            confidence = 0f,
            keypoints = null
        };
    }
}
