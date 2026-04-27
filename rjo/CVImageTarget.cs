using UnityEngine;

public class CVImageTarget
{
    public readonly string name;
    public readonly Texture2D reference;

    public CVImageTarget(string name, Texture2D tex)
    {
        this.name = name;
        reference = tex;
    }
}
