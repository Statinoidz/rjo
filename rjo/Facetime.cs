using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace CVatGPT
{
    [ExecuteAlways]
    public class Facetime : MonoBehaviour
    {
        [Header("Target surface")]
        public RawImage targetRawImage;
        public Renderer targetQuadRenderer;

        [Tooltip("How many text columns to simulate.")]
        public int columns = 120;

        [Tooltip("How many text rows to simulate.")]
        public int rows = 40;

        [Tooltip("Pixel size of each character cell.")]
        public int pixelsPerCell = 8;

        [Header("Sihouetter link")]
        public Sihouetter sihouetter;

        [Tooltip("When enabled, writes Sihouetter blob centroid to StatiBlob.txt each frame.")]
        public bool writeBlobToStatiBlob = true;

        [Header("StatiBlob.txt")]
        public string statiBlobFileName = "StatiBlob.txt";

        [Header("Visual style")]
        public Color backgroundColor = Color.black;
        public Color foregroundColor = Color.green;

        [Header("Mode")]
        [Tooltip("When enabled, renders EXACTLY like the Bash shell script (no scaling, stripe background).")]
        public bool shellMode = false;

        private Texture2D _asciiTexture;
        private bool _initialized;

        // FPS tracking
        private float _lastFpsTime;
        private int _fpsValue;

        // Digit glyphs
        private readonly string[][] _digits =
        {
            new[]{ "███","█ █","█ █","█ █","█ █","█ █","███" },
            new[]{ " ██","███"," ██"," ██"," ██"," ██","████" },
            new[]{ "███","  █","  █","███","█  ","█  ","███" },
            new[]{ "███","  █","  █","███","  █","  █","███" },
            new[]{ "█ █","█ █","█ █","███","  █","  █","  █" },
            new[]{ "███","█  ","█  ","███","  █","  █","███" },
            new[]{ "███","█  ","█  ","███","█ █","█ █","███" },
            new[]{ "███","  █","  █","  █","  █","  █","  █" },
            new[]{ "███","█ █","█ █","███","█ █","█ █","███" },
            new[]{ "███","█ █","█ █","███","  █","  █","███" }
        };

        private void OnEnable()
        {
            InitTexture();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                InitTexture();
                string frame = GenerateXyFullscreenFrame(0, 0, 0);
                DrawAsciiToTexture(frame);
            }
        }

        private void InitTexture()
        {
            int w = Mathf.Max(1, columns * pixelsPerCell);
            int h = Mathf.Max(1, rows * pixelsPerCell);

            if (_asciiTexture != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(_asciiTexture);
                else
                    Destroy(_asciiTexture);
#else
                Destroy(_asciiTexture);
#endif
            }

            _asciiTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _asciiTexture.filterMode = FilterMode.Point;
            ClearTexture();

            if (targetRawImage != null)
                targetRawImage.texture = _asciiTexture;

            if (targetQuadRenderer != null)
                targetQuadRenderer.material.mainTexture = _asciiTexture;

            _initialized = true;
        }

        private void ClearTexture()
        {
            if (_asciiTexture == null)
                return;

            var pixels = _asciiTexture.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = shellMode ? Color.black : backgroundColor;
            _asciiTexture.SetPixels(pixels);
            _asciiTexture.Apply();
        }

        private void Update()
        {
            if (!_initialized)
                InitTexture();

            if (!Application.isPlaying)
                return;

            float now = Time.unscaledTime;
            float dt = now - _lastFpsTime;
            if (dt > 0.0001f)
            {
                _fpsValue = Mathf.Clamp((int)(1.0f / dt), 0, 999);
                _lastFpsTime = now;
            }

            int x, y;
            if (sihouetter != null && sihouetter.TryGetBlobCentroid(out var centroid))
            {
                x = Mathf.RoundToInt(centroid.x);
                y = Mathf.RoundToInt(centroid.y);
            }
            else
            {
                x = 0;
                y = 0;
            }

            if (writeBlobToStatiBlob && sihouetter != null)
                WriteBlobXYToFile(x, y);

            string frame = GenerateXyFullscreenFrame(x, y, _fpsValue);
            DrawAsciiToTexture(frame);
        }

        private void WriteBlobXYToFile(int x, int y)
        {
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string path = !string.IsNullOrEmpty(home)
                    ? System.IO.Path.Combine(home, statiBlobFileName)
                    : System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), statiBlobFileName);

                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                System.IO.File.WriteAllText(path, $"{x},{y}", Encoding.UTF8);
            }
            catch { }
        }

        // ---------------------------------------------------------
        //   XY FULLSCREEN (with Shell Mode)
        // ---------------------------------------------------------
        private string GenerateXyFullscreenFrame(int X, int Y, int fps)
        {
            char[,] grid = new char[rows, columns];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                    grid[r, c] = ' ';

            string status = string.Format("FPS:{0,3}  X:{1,4}  Y:{2,4}", fps, X, Y);
            status = PadRight(status, columns);
            WriteTextToGrid(grid, 0, 0, status);

            string[] glyphRows = RenderXyDigits(X, Y);

            int glyphRowsCount = 7;
            int topMargin = 2;
            int usableLines = Mathf.Max(rows - topMargin, glyphRowsCount);

            int scaleY, scaleX;

            if (shellMode)
            {
                scaleY = 1;
                scaleX = 1;
            }
            else
            {
                scaleY = Mathf.Max(usableLines / glyphRowsCount, 1);
                string baseRow = glyphRows[0];
                int baseLen = Mathf.Max(baseRow.Length, 1);
                scaleX = Mathf.Max(columns / baseLen, 1);
            }

            int padTop = (usableLines - glyphRowsCount * scaleY) / 2;
            int currentRow = topMargin + padTop;

            for (int i = 0; i < glyphRowsCount; i++)
            {
                string scaledRow = ScaleRow(glyphRows[i], scaleX);

                for (int k = 0; k < scaleY; k++)
                {
                    if (currentRow >= rows)
                        break;

                    string lineOut;
                    int len = scaledRow.Length;

                    if (len > columns)
                        lineOut = scaledRow.Substring(0, columns);
                    else
                    {
                        int pad = Mathf.Max((columns - len) / 2, 0);
                        lineOut = PadRight(new string(' ', pad) + scaledRow, columns);
                    }

                    WriteTextToGrid(grid, currentRow, 0, lineOut);
                    currentRow++;
                }
            }

            int startRow = Mathf.Min(currentRow + 1, rows - 1);
            DrawCheckerboardRegion(grid, startRow, rows - 1);

            var sb = new StringBuilder(rows * (columns + 1));
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                    sb.Append(grid[r, c]);
                sb.Append('\n');
            }

            return sb.ToString();
        }

        private string[] RenderXyDigits(int X, int Y)
        {
            int ax = Mathf.Abs(X);
            int ay = Mathf.Abs(Y);

            string axStr = ax.ToString("00");
            string ayStr = ay.ToString("00");

            char d1 = axStr[0];
            char d2 = axStr[1];
            char d3 = ayStr[0];
            char d4 = ayStr[1];

            string[] g1 = GetDigitGlyph(d1);
            string[] g2 = GetDigitGlyph(d2);
            string[] g3 = GetDigitGlyph(d3);
            string[] g4 = GetDigitGlyph(d4);

            if (X < 0)
            {
                g1 = StrikeDigit(g1);
                g2 = StrikeDigit(g2);
            }
            if (Y < 0)
            {
                g3 = StrikeDigit(g3);
                g4 = StrikeDigit(g4);
            }

            string[] rowsOut = new string[7];
            for (int i = 0; i < 7; i++)
                rowsOut[i] = g1[i] + " " + g2[i] + "  " + g3[i] + " " + g4[i];

            return rowsOut;
        }

        private string[] GetDigitGlyph(char ch)
        {
            int idx = ch - '0';
            if (idx < 0 || idx > 9)
                idx = 0;
            return _digits[idx];
        }

        private string[] StrikeDigit(string[] glyph)
        {
            string[] outGlyph = new string[glyph.Length];
            for (int i = 0; i < glyph.Length; i++)
                outGlyph[i] = (i == 3) ? "███" : glyph[i];
            return outGlyph;
        }

        private string ScaleRow(string text, int scaleX)
        {
            if (scaleX <= 1)
                return text;

            var sb = new StringBuilder(text.Length * scaleX);
            foreach (char ch in text)
                for (int i = 0; i < scaleX; i++)
                    sb.Append(ch);

            return sb.ToString();
        }

        private void WriteTextToGrid(char[,] grid, int row, int col, string text)
        {
            if (row < 0 || row >= rows)
                return;

            int maxCols = Mathf.Min(columns - col, text.Length);
            for (int i = 0; i < maxCols; i++)
            {
                int c = col + i;
                if (c >= 0 && c < columns)
                    grid[row, c] = text[i];
            }
        }

        private string PadRight(string s, int width)
        {
            if (s.Length >= width)
                return s.Substring(0, width);
            return s + new string(' ', width - s.Length);
        }

        private void DrawCheckerboardRegion(char[,] grid, int startRow, int endRow)
        {
            startRow = Mathf.Clamp(startRow, 0, rows - 1);
            endRow = Mathf.Clamp(endRow, 0, rows - 1);

            if (shellMode)
            {
                for (int y = startRow; y <= endRow; y++)
                {
                    bool whiteStripe = (y % 2 == 0);
                    for (int x = 0; x < columns; x++)
                        grid[y, x] = whiteStripe ? '█' : ' ';
                }
                return;
            }

            int voxelX = Mathf.Max(columns / 16, 1);
            int voxelY = Mathf.Max(rows / 16, 1);

            for (int y = startRow; y <= endRow; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int cx = x / voxelX;
                    int cy = y / voxelY;
                    grid[y, x] = (((cx + cy) % 2) == 0) ? ' ' : '█';
                }
            }
        }

        private void DrawAsciiToTexture(string frame)
        {
            if (_asciiTexture == null)
                return;

            ClearTexture();

            if (string.IsNullOrEmpty(frame))
            {
                _asciiTexture.Apply();
                return;
            }

            string[] lines = frame.Split('\n');

            Color fg = shellMode ? Color.white : foregroundColor;

            int maxRows = Mathf.Min(rows, lines.Length);
            for (int y = 0; y < maxRows; y++)
            {
                string line = lines[y];
                int maxCols = Mathf.Min(columns, line.Length);

                for (int x = 0; x < maxCols; x++)
                {
                    char c = line[x];
                    if (c != ' ' && c != '\r' && c != '\t')
                        FillCell(x, rows - 1 - y, fg);
                }
            }

            _asciiTexture.Apply();
        }

        private void FillCell(int cx, int cy, Color color)
        {
            if (_asciiTexture == null)
                return;

            int startX = cx * pixelsPerCell;
            int startY = cy * pixelsPerCell;

            for (int y = 0; y < pixelsPerCell; y++)
            {
                for (int x = 0; x < pixelsPerCell; x++)
                {
                    int px = startX + x;
                    int py = startY + y;

                    if (px >= 0 && px < _asciiTexture.width &&
                        py >= 0 && py < _asciiTexture.height)
                    {
                        _asciiTexture.SetPixel(px, py, color);
                    }
                }
            }
        }
    }
}
