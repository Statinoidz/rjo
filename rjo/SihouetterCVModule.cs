using UnityEngine;

namespace CVatGPT
{
    public partial class Sihouetter
    {
        /// <summary>
        /// Reference to the main video source
        /// </summary>
        public static AAAVideo VideoSource { get; set; }

        /// <summary>
        /// Returns true if the CV texture is valid and available
        /// </summary>
        public static bool HasValidVideoTexture()
        {
            return VideoSource != null && VideoSource.GetCvTexture() != null;
        }

        /// <summary>
        /// Get the current CV processing texture (RawImage-backed)
        /// </summary>
        public static Texture GetCvProcessingTexture()
        {
            return VideoSource?.GetCvTexture();
        }

        /// <summary>
        /// Get a snapshot (Texture2D) from the CV output
        /// </summary>
        public static Texture2D GetCvProcessingSnapshot()
        {
            if (VideoSource == null) return null;

            if (VideoSource.TryGetLatestCvSnapshot(out Texture2D snapshot))
                return snapshot;

            return null;
        }

        /// <summary>
        /// Convenience: Get width / height of current CV frame
        /// </summary>
        public static Vector2Int CvFrameSize()
        {
            var tex = GetCvProcessingTexture();
            if (tex != null)
                return new Vector2Int(tex.width, tex.height);

            return Vector2Int.zero;
        }
    }
}
