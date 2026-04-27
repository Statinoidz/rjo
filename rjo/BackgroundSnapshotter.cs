using System.IO;
using UnityEngine;

namespace CVatGPT
{
    [ExecuteAlways]
    public class BackgroundSnapshotter : MonoBehaviour
    {
        [Header("References")]
        public Sihouetter sihouetter;     // The main silhouette system
        public AAAVideo videoSource;      // The live video feed

        [Header("Settings")]
        public string snapshotFileName = "DynamicBackground.png";

        private bool hasRun = false;

        private void OnEnable()
        {
            hasRun = false;
        }

        private void Update()
        {
            if (hasRun)
                return;

            if (!Application.isPlaying)
                return;

            if (sihouetter == null || videoSource == null)
            {
                Debug.LogWarning("[BackgroundSnapshotter] Missing references.");
                DisableSelf();
                return;
            }

            // FIXED: AAAVideo does NOT have GetColorTexture()
            Texture src = videoSource.GetVideoTexture();
            if (src == null)
            {
                Debug.LogWarning("[BackgroundSnapshotter] No color frame available.");
                DisableSelf();
                return;
            }

            // Convert current frame to Texture2D
            Texture2D snap = ConvertToTexture2D(src);

            // Save PNG
            string path = Path.Combine(Application.persistentDataPath, snapshotFileName);
            File.WriteAllBytes(path, snap.EncodeToPNG());

#if UNITY_EDITOR
            Debug.Log($"[BackgroundSnapshotter] Snapshot saved to: {path}");
#endif

            // Load PNG back into a new Texture2D
            Texture2D loaded = new Texture2D(snap.width, snap.height, TextureFormat.RGBA32, false);
            loaded.LoadImage(File.ReadAllBytes(path));

            // Assign as new background reference
            sihouetter.backgroundReference = loaded;
            sihouetter.useDynamicBackground = false;

#if UNITY_EDITOR
            Debug.Log("[BackgroundSnapshotter] Snapshot loaded into Sihouetter.backgroundReference");
#endif

            hasRun = true;
            DisableSelf();
        }

        private Texture2D ConvertToTexture2D(Texture src)
        {
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            tex.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            return tex;
        }

        private void DisableSelf()
        {
            this.enabled = false;
        }
    }
}
