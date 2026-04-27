using System;
using UnityEngine;

namespace CVatGPT
{
    // Removed old CVKeypoint definition — now lives in CVKeypoint.cs

    [Serializable]
    public struct CVPoseData
    {
        public int id;
        public CVKeypoint[] keypoints;
    }

    [Serializable]
    public struct CVFlowVector
    {
        public Vector2 position;
        public Vector2 flow;
    }

    [Serializable]
    public struct CVComponent
    {
        public int id;
        public int area;
        public Rect boundingBox;
        public Vector2 centroid;
    }

    [Serializable]
    public struct CVMask
    {
        public int width;
        public int height;
        public byte[] data;
    }

    [Serializable]
    public struct CVPolygon
    {
        public Vector2[] points;
    }

    [Serializable]
    public class CVFrameResult
    {
        public int frameIndex;
        public float timestamp;
        public CVPoseData[] poses;
        public CVFlowVector[] opticalFlow;
        public CVMask[] masks;
        public CVComponent[] components;
        public CVPolygon[] polygons;

        public bool HasPoses => poses != null && poses.Length > 0;
    }
}
