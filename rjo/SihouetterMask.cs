using UnityEngine;

public partial class Sihouetter
{
    /// <summary>
    /// Returns the binary mask used for contour extraction.
    /// For now, assumes the source texture IS the mask.
    /// </summary>
    protected Texture2D GetMaskTexture(Texture2D source)
    {
        return source;
    }
}
