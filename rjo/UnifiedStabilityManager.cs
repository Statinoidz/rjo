using UnityEngine;

public class UnifiedStabilityManager : MonoBehaviour
{
    public ComputeShader unifiedShader;
    public RenderTexture source;
    public RenderTexture result;
    public bool enabledGPU = true;

    int kernel;

    void Start()
    {
        if (!unifiedShader) return;
        kernel = unifiedShader.FindKernel("CSMain");

        if (result == null)
        {
            result = new RenderTexture(source.width, source.height, 0);
            result.enableRandomWrite = true;
            result.Create();
        }
    }

    public void Run()
    {
        if (!enabledGPU) return;

        unifiedShader.SetTexture(kernel, "_Source", source);
        unifiedShader.SetTexture(kernel, "_Result", result);

        int threadX = Mathf.CeilToInt(source.width / 8f);
        int threadY = Mathf.CeilToInt(source.height / 8f);
        unifiedShader.Dispatch(kernel, threadX, threadY, 1);
    }

    public void Snapshot(string path)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = result;

        Texture2D tex = new Texture2D(result.width, result.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0,0,result.width,result.height),0,0);
        tex.Apply();

        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = prev;
        Destroy(tex);
    }
}
