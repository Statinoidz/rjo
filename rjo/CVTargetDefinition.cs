using UnityEngine;

[System.Serializable]
public class CVTargetDefinition
{
    public string targetID;
    public Texture2D referenceImage;

    public Vector2[] quadUVs = new Vector2[4];
    public float aspectRatio = 1.0f;

    [HideInInspector] public bool unlocked;
}
