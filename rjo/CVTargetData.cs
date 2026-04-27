using UnityEngine;
using System.Collections.Generic;

public class CVTargetData
{
    public string name;

    // Used by DynamicImageLoaderV4
    public Texture2D referenceTex;

    // Optional geometry data (already used elsewhere)
    public List<Vector3> referencePoints = new List<Vector3>();
}
