using System;
using UnityEngine;

namespace CVatGPT
{
    /// <summary>
    /// Self-contained tracker for shell/Facetime XY fullscreen targets.
    /// - Detects a bright quad (remote screen) in the camera frame.
    /// - Estimates its rotation.
    /// - Warps the quad to a canonical grid.
    /// - Reads the 4 XY digits using 7x3 glyph templates.
    /// 
    /// Attach this to a Facetime-target quad prefab on the observing device.
    /// It does NOT read from the quad's texture; it reads from CVFrameProvider.
    /// </summary>
    public class CVFacetimeTargetTracker : MonoBehaviour
    {
        [Header("Target Identity")]
        [Tooltip("Logical name for this Facetime target in the CV system.")]
        public string targetName = "FacetimeTarget";

        [Header("Canonical grid")]
        [Tooltip("Width of the warped canonical grid (cells).")]
        public int canonicalWidth = 120;

        [Tooltip("Height of the warped canonical grid (cells).")]
        public int canonicalHeight = 40;

        [Header("Digit band (in canonical space, 0-1)")]
        [Tooltip("Normalized rect in canonical grid where the 4 digits live.")]
        public Rect digitBand = new Rect(0.2f, 0.3f, 0.6f, 0.4f);

        [Header("Glyph sampling")]
        [Tooltip("Columns per digit (matches 7x3 glyphs).")]
        public int digitCols = 3;

        [Tooltip("Rows per digit (matches 7x3 glyphs).")]
        public int digitRows = 7;

        [Header("Detection / Thresholds")]
        [Tooltip("Grayscale threshold for screen detection (0-255).")]
        [Range(0, 255)] public int screenThreshold = 180;

        [Tooltip("Minimum fraction of bright pixels to accept a quad.")]
        [Range(0f, 1f)] public float minBrightFraction = 0.01f;

        [Tooltip("Minimum per-digit match score (0-1) to consider the reading valid.")]
        public float minDigitConfidence = 0.7f;

        [Tooltip("Minimum overall confidence to consider the target 'locked'.")]
        public float lockConfidenceThreshold = 0.8f;

        [Header("Debug")]
        public bool verbose = false;

        // Public outputs
        [NonSerialized] public int decodedX;
        [NonSerialized] public int decodedY;
        [NonSerialized] public float confidence;
        [NonSerialized] public bool isLocked;
        [NonSerialized] public Quaternion screenRotation = Quaternion.identity;
        [NonSerialized] public bool hasQuad;

        // Internal glyph templates (7x3 per digit)
        private readonly string[][] _digitGlyphs =
        {
            new[]{ "███","█ █","█ █","█ █","█ █","█ █","███" }, // 0
            new[]{ " ██","███"," ██"," ██"," ██"," ██","████" }, // 1
            new[]{ "███","  █","  █","███","█  ","█  ","███" }, // 2
            new[]{ "███","  █","  █","███","  █","  █","███" }, // 3
            new[]{ "█ █","█ █","█ █","███","  █","  █","  █" }, // 4
            new[]{ "███","█  ","█  ","███","  █","  █","███" }, // 5
            new[]{ "███","█  ","█  ","███","█ █","█ █","███" }, // 6
            new[]{ "███","  █","  █","  █","  █","  █","  █" }, // 7
            new[]{ "███","█ █","█ █","███","█ █","█ █","███" }, // 8
            new[]{ "███","█ █","█ █","███","  █","  █","███" }  // 9
        };

        // Flattened templates: [10 * digitRows, digitCols]
        private bool[,] _digitTemplates;

        // Canonical warped buffer (grayscale, 0-255)
        private byte[] _canonical;

        private void Awake()
        {
            BuildDigitTemplates();
            _canonical = new byte[canonicalWidth * canonicalHeight];
        }

        private void Update()
        {
            var provider = CVFrameProvider.Instance;
            if (provider == null || !provider.HasFrame)
                return;

            var grayFrame = provider.GetGrayFrame();
            if (grayFrame == null || grayFrame.data == null)
                return;

            ProcessFrame(grayFrame.data, grayFrame.width, grayFrame.height);
        }

        // ---------------------------------------------------------
        // Main per-frame processing
        // ---------------------------------------------------------
        private void ProcessFrame(byte[] gray, int width, int height)
        {
            hasQuad = false;
            confidence = 0f;
            isLocked = false;

            // 1) Detect bright quad (screen) in the frame
            Vector2[] quad;
            if (!DetectScreenQuad(gray, width, height, out quad))
            {
                if (verbose)
                    Debug.Log("[CVFacetimeTargetTracker] No quad detected");
                return;
            }

            hasQuad = true;

            // 2) Estimate rotation from quad corners
            screenRotation = EstimateRotationFromQuad(quad);

            // 3) Warp quad to canonical grid
            WarpQuadToCanonical(gray, width, height, quad, _canonical, canonicalWidth, canonicalHeight);

            // 4) Decode digits from canonical grid
            DecodeDigitsFromCanonical(_canonical, canonicalWidth, canonicalHeight);
        }

        // ---------------------------------------------------------
        // Screen quad detection (simple high-contrast heuristic)
        // ---------------------------------------------------------
        private bool DetectScreenQuad(byte[] gray, int width, int height, out Vector2[] quad)
        {
            quad = null;

            int minX = width, maxX = -1;
            int minY = height, maxY = -1;
            int brightCount = 0;
            int total = width * height;

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    byte v = gray[row + x];
                    if (v >= screenThreshold)
                    {
                        brightCount++;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (brightCount == 0)
                return false;

            float frac = (float)brightCount / total;
            if (frac < minBrightFraction)
                return false;

            // Approximate corners as axis-aligned quad
            quad = new Vector2[4];
            quad[0] = new Vector2(minX, minY); // top-left
            quad[1] = new Vector2(maxX, minY); // top-right
            quad[2] = new Vector2(maxX, maxY); // bottom-right
            quad[3] = new Vector2(minX, maxY); // bottom-left

            return true;
        }

        // ---------------------------------------------------------
        // Rotation estimation from quad
        // ---------------------------------------------------------
        private Quaternion EstimateRotationFromQuad(Vector2[] quad)
        {
            // Use top edge as "right" direction in image space
            Vector2 top = quad[1] - quad[0];
            if (top.sqrMagnitude < 1e-6f)
                return Quaternion.identity;

            top.Normalize();

            // Map image space to world:
            // x right, y up, camera looking forward (Z+)
            Vector3 right = new Vector3(top.x, -top.y, 0f);
            Vector3 forward = Vector3.forward;
            Vector3 up = Vector3.Cross(forward, right).normalized;

            if (up.sqrMagnitude < 1e-6f)
                up = Vector3.up;

            return Quaternion.LookRotation(forward, up);
        }

        // ---------------------------------------------------------
        // Quad warp to canonical grid
        // ---------------------------------------------------------
        private void WarpQuadToCanonical(
            byte[] src, int srcW, int srcH,
            Vector2[] quad,
            byte[] dst, int dstW, int dstH)
        {
            if (dst == null || dst.Length != dstW * dstH)
                dst = new byte[dstW * dstH];

            Vector2 p0 = quad[0];
            Vector2 p1 = quad[1];
            Vector2 p2 = quad[2];
            Vector2 p3 = quad[3];

            for (int y = 0; y < dstH; y++)
            {
                float v = (y + 0.5f) / dstH;
                for (int x = 0; x < dstW; x++)
                {
                    float u = (x + 0.5f) / dstW;

                    // Bilinear interpolation on quad
                    Vector2 a = Vector2.Lerp(p0, p1, u);
                    Vector2 b = Vector2.Lerp(p3, p2, u);
                    Vector2 p = Vector2.Lerp(a, b, v);

                    int sx = Mathf.Clamp(Mathf.RoundToInt(p.x), 0, srcW - 1);
                    int sy = Mathf.Clamp(Mathf.RoundToInt(p.y), 0, srcH - 1);

                    dst[y * dstW + x] = src[sy * srcW + sx];
                }
            }
        }

        // ---------------------------------------------------------
        // Digit decoding from canonical grid
        // ---------------------------------------------------------
        private void DecodeDigitsFromCanonical(byte[] canonical, int w, int h)
        {
            // 1) Compute digit band in canonical space
            int bandX = Mathf.Clamp(Mathf.RoundToInt(digitBand.x * w), 0, w - 1);
            int bandY = Mathf.Clamp(Mathf.RoundToInt(digitBand.y * h), 0, h - 1);
            int bandW = Mathf.Clamp(Mathf.RoundToInt(digitBand.width * w), 8, w - bandX);
            int bandH = Mathf.Clamp(Mathf.RoundToInt(digitBand.height * h), 8, h - bandY);

            int digitW = bandW / 4;
            int digitH = bandH;

            int[] digits = new int[4];
            float[] scores = new float[4];
            bool xNeg = false;
            bool yNeg = false;

            for (int i = 0; i < 4; i++)
            {
                int dx0 = bandX + i * digitW;
                int dy0 = bandY;

                bool[,] sampled = SampleDigit(canonical, w, h, dx0, dy0, digitW, digitH);
                int bestDigit;
                float bestScore;
                bool struck;

                ClassifyDigit(sampled, out bestDigit, out bestScore, out struck);

                digits[i] = bestDigit;
                scores[i] = bestScore;

                if (i < 2 && struck) xNeg = true;
                if (i >= 2 && struck) yNeg = true;
            }

            float avg = 0f;
            for (int i = 0; i < 4; i++)
                avg += scores[i];
            avg /= 4f;

            confidence = avg;
            isLocked = confidence >= lockConfidenceThreshold;

            int ax = digits[0] * 10 + digits[1];
            int ay = digits[2] * 10 + digits[3];

            decodedX = xNeg ? -ax : ax;
            decodedY = yNeg ? -ay : ay;

            if (verbose)
            {
                Debug.Log($"[CVFacetimeTargetTracker] X={decodedX}, Y={decodedY}, conf={confidence:0.00}, locked={isLocked}, rot={screenRotation.eulerAngles}");
            }
        }

        private bool[,] SampleDigit(byte[] canonical, int w, int h,
                                    int x0, int y0, int dw, int dh)
        {
            bool[,] cells = new bool[digitRows, digitCols];

            float cellW = dw / (float)digitCols;
            float cellH = dh / (float)digitRows;

            for (int r = 0; r < digitRows; r++)
            {
                for (int c = 0; c < digitCols; c++)
                {
                    int sx = Mathf.Clamp(x0 + Mathf.RoundToInt((c + 0.5f) * cellW), 0, w - 1);
                    int sy = Mathf.Clamp(y0 + Mathf.RoundToInt((r + 0.5f) * cellH), 0, h - 1);

                    byte v = canonical[sy * w + sx];
                    cells[r, c] = v > 128;
                }
            }

            return cells;
        }

        private void ClassifyDigit(bool[,] sampled, out int bestDigit, out float bestScore, out bool struck)
        {
            bestDigit = 0;
            bestScore = -1f;
            struck = false;

            for (int d = 0; d < 10; d++)
            {
                float normal = CompareToTemplate(sampled, d, false);
                float strike = CompareToTemplate(sampled, d, true);

                float localBest = normal;
                bool localStruck = false;

                if (strike > localBest)
                {
                    localBest = strike;
                    localStruck = true;
                }

                if (localBest > bestScore)
                {
                    bestScore = localBest;
                    bestDigit = d;
                    struck = localStruck;
                }
            }
        }

        private float CompareToTemplate(bool[,] sampled, int digit, bool forceStrikeRow)
        {
            int matches = 0;
            int total = digitRows * digitCols;

            for (int r = 0; r < digitRows; r++)
            {
                for (int c = 0; c < digitCols; c++)
                {
                    bool tmpl = _digitTemplates[digit * digitRows + r, c];
                    if (forceStrikeRow && r == 3)
                        tmpl = true;

                    if (sampled[r, c] == tmpl)
                        matches++;
                }
            }

            return (float)matches / total;
        }

        // ---------------------------------------------------------
        // Glyph template construction
        // ---------------------------------------------------------
        private void BuildDigitTemplates()
        {
            _digitTemplates = new bool[10 * digitRows, digitCols];

            for (int d = 0; d < 10; d++)
            {
                string[] glyph = _digitGlyphs[d];
                for (int r = 0; r < digitRows; r++)
                {
                    string row = glyph[r];
                    for (int c = 0; c < digitCols; c++)
                    {
                        char ch = (c < row.Length) ? row[c] : ' ';
                        bool on = (ch != ' ' && ch != '\t' && ch != '\r');
                        _digitTemplates[d * digitRows + r, c] = on;
                    }
                }
            }
        }
    }
}
