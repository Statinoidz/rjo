using System.Collections.Generic;
using UnityEngine;

public struct CVTrackingInputFrame
{
    public string candidateTargetID;

    public List<Vector2> interiorPoints;
    public float interiorConfidence;

    public Vector2[] observedQuad;
}
