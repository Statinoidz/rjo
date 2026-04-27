using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pi Zero–safe fixed-point sparse optical flow provider.
/// Now reads directly from AAAVideo instead of RawImage,
/// and uses RawImage ONLY for scale reference.
/// </summary>
public class CVFrameProvider : MonoBehaviour
{
    public class CVGrayFrame
    {
        public byte[] data;
        public int width;
        public int height;
    }

    public static CVFrameProvider Instance { get; private set; }

    [Header("Source")]
    public AAAVideo videoSource;        // ← assign AAAVideo in Inspector (or auto-grab)
    public RawImage scaleReference;     // ← assign cvRawImage in Inspector (scale only)

    [Header("Settings")]
    public int width = 128;
    public int height = 96;
    public int gridStep = 8;
    public int targetFPS = 15;

    public bool enableMotionTracking = true;

    public bool HasFrame { get; private set; }

    private Texture2D tex2D;

    private byte[] grayPrev;
    private byte[] grayCurr;

    private int[] flowX;
    private int[] flowY;

    private float lastCaptureTime;

    private const int FP_SHIFT = 8;        // Q8.8 fixed
    private const int FP_ONE = 1 << FP_SHIFT;

    private int featureCount;
    private bool initialized;

    // Global motion vector
    public Vector2 GlobalMotion { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (!videoSource)
            videoSource = AAAVideo.Instance;
    }

    private void Update()
    {
        if (!enableMotionTracking) return;

        if (Time.time - lastCaptureTime < 1f / targetFPS) return;
        lastCaptureTime = Time.time;

        if (!videoSource)
        {
            videoSource = AAAVideo.Instance;
            if (!videoSource)
                return;
        }

        if (!initialized)
        {
            tex2D = videoSource.GetVideoTexture();

            if (tex2D == null || tex2D.width == 0 || tex2D.height == 0)
                return;

            width = tex2D.width;
            height = tex2D.height;

            grayPrev = new byte[width * height];
            grayCurr = new byte[width * height];

            featureCount = (width / gridStep) * (height / gridStep);
            flowX = new int[featureCount];
            flowY = new int[featureCount];

            initialized = true;
            Debug.Log("[CVFrameProvider] Initialized after AAAVideo became ready");
        }

        tex2D = videoSource.GetVideoTexture();
        if (tex2D == null || tex2D.width == 0 || tex2D.height == 0)
            return;

        int needed = tex2D.width * tex2D.height;
        if (grayCurr == null || grayCurr.Length != needed)
        {
            grayPrev = new byte[needed];
            grayCurr = new byte[needed];

            width = tex2D.width;
            height = tex2D.height;
            featureCount = (width / gridStep) * (height / gridStep);
            flowX = new int[featureCount];
            flowY = new int[featureCount];
        }

        var pixels = tex2D.GetPixels32();
        ConvertToGray(pixels, grayCurr);

        if (scaleReference)
        {
            Vector2 uiSize = scaleReference.rectTransform.rect.size;
            float uiWidth = uiSize.x;
            float uiHeight = uiSize.y;
            // optional: use uiWidth/uiHeight
        }

        if (HasFrame)
        {
            ComputeFlowFixed();
            EstimateGlobalMotionFixed();
        }

        var temp = grayPrev;
        grayPrev = grayCurr;
        grayCurr = temp;

        HasFrame = true;
    }

    public CVGrayFrame GetGrayFrame()
    {
        if (!HasFrame || grayPrev == null)
            return null;

        return new CVGrayFrame
        {
            data = grayPrev,
            width = width,
            height = height
        };
    }

    private void ConvertToGray(Color32[] pixels, byte[] gray)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            gray[i] = (byte)((p.r * 30 + p.g * 59 + p.b * 11) / 100);
        }
    }

    private void ComputeFlowFixed()
    {
        int index = 0;

        for (int y = 4; y < height - 4; y += gridStep)
        {
            for (int x = 4; x < width - 4; x += gridStep)
            {
                int A11 = 0, A12 = 0, A22 = 0;
                int b1 = 0, b2 = 0;

                for (int ky = -1; ky <= 1; ky++)
                {
                    for (int kx = -1; kx <= 1; kx++)
                    {
                        int i = (y + ky) * width + (x + kx);

                        int Ix = grayCurr[i + 1] - grayCurr[i - 1];
                        int Iy = grayCurr[i + width] - grayCurr[i - width];
                        int It = grayCurr[i] - grayPrev[i];

                        A11 += Ix * Ix;
                        A12 += Ix * Iy;
                        A22 += Iy * Iy;

                        b1 += Ix * It;
                        b2 += Iy * It;
                    }
                }

                int det = A11 * A22 - A12 * A12;

                if (det > 5000 && A11 > A22)
                {
                    int numX = (A22 * -b1 - A12 * -b2) << FP_SHIFT;
                    int numY = (A11 * -b2 - A12 * -b1) << FP_SHIFT;

                    flowX[index] = numX / det;
                    flowY[index] = numY / det;
                }
                else
                {
                    flowX[index] = 0;
                    flowY[index] = 0;
                }

                index++;
            }
        }
    }

    private void EstimateGlobalMotionFixed()
    {
        long sumX = 0;
        long sumY = 0;
        int count = 0;

        int limit = 5 << FP_SHIFT;

        for (int i = 0; i < flowX.Length; i++)
        {
            int vx = flowX[i];
            int vy = flowY[i];

            if (vx < limit && vx > -limit &&
                vy < limit && vy > -limit)
            {
                sumX += vx;
                sumY += vy;
                count++;
            }
        }

        if (count == 0)
        {
            GlobalMotion = Vector2.zero;
            return;
        }

        int avgX = (int)(sumX / count);
        int avgY = (int)(sumY / count);

        float motionX = avgX / (float)FP_ONE;
        float motionY = avgY / (float)FP_ONE;

        GlobalMotion = new Vector2(motionX, motionY);

        ApplyMotion(avgX, avgY);
    }

    private void ApplyMotion(int avgX, int avgY)
    {
        float motionX = avgX / (float)FP_ONE;
        float motionY = avgY / (float)FP_ONE;

        // TODO: feed into your voxel system
    }
}
