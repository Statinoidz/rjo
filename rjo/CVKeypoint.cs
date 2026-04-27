using UnityEngine;

namespace CVatGPT
{
    public enum CVKeypointQuality
    {
        Invalid = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    public struct CVKeypoint
    {
        public Vector2 imagePosition;
        public Vector3 worldPosition;
        public float response;
        public bool matched;
        public CVKeypointQuality quality;

        public bool IsValid => quality != CVKeypointQuality.Invalid;

        public Vector3 WorldPosition => worldPosition;
    }
}
