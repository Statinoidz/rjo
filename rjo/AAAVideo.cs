using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AAAVideo : MonoBehaviour
{
    public static AAAVideo Instance { get; private set; }

    [Header("RawImages")]
    public RawImage cvRawImage;
    public RawImage rgbRawImage;

    [Header("Aspect")]
    public AspectRatioFitter aspectFitter;

    [Header("Camera Selection")]
    public Button[] cameraButtons = new Button[4];
    public TMP_Text[] cameraLabels = new TMP_Text[4];

    private WebCamDevice[] devices;
    private WebCamTexture webcamTex;

    public enum CvPreset { Low, Medium, Max }

    [Header("CV Presets")]
    public Button lowButton;
    public Button mediumButton;
    public Button maxButton;

    public int lowBaseHeight = 120;
    public int mediumBaseHeight = 240;

    private CvPreset currentPreset = CvPreset.Medium;

    private Texture2D videoTexture;
    private Texture2D cvTexture;

    private Color32[] videoPixels;
    private Color32[] cvPixels;
    private byte[] cvGray;

    private float lockedAspect = -1f;

    public TMP_Text fpsText;
    public TMP_Text cvRateText;

    private int frameCount;
    private float fpsTimer;
    private int cvTicks;
    private float cvTimer;

    [Header("Scene Camera")]
    public Camera mainCamera;

    [Header("Unified Stability")]
    public ComputeShader unifiedStabilityShader;
    public RenderTexture stabilityRT;

    [Range(0f, 1f)]
    public float stabilityOverlayStrength = 0.35f;

    private Texture2D stabilityMask;

    [Header("Output RenderTextures")]
    public RenderTexture colorRT;
    public RenderTexture cvRT;

    [Header("Output Flags")]
    public bool enableCvRawImage = true;
    public bool enableCvRenderTexture = true;
    public bool enableColorRawImage = true;
    public bool enableColorRenderTexture = true;

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
        if (mainCamera == null)
            mainCamera = Camera.main;

        EnumerateCameras();
        HookPresetButtons();

        if (devices.Length > 0)
            SelectCamera(0);
    }

    private void OnDestroy()
    {
        if (webcamTex) webcamTex.Stop();
        if (videoTexture) Destroy(videoTexture);
        if (cvTexture) Destroy(cvTexture);
        if (stabilityRT) stabilityRT.Release();
        if (stabilityMask) Destroy(stabilityMask);
        if (colorRT) colorRT.Release();
        if (cvRT) cvRT.Release();
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        UpdateFrame();
        UpdateFPS();
        UpdateCVRate();
    }

    // ============================================================
    // CORE PIPELINE (FIXED)
    // ============================================================
    private void UpdateFrame()
    {
        if (webcamTex == null || !webcamTex.isPlaying || webcamTex.width < 16)
            return;

        bool needColor = enableColorRawImage || enableColorRenderTexture;

        // 🚀 ALWAYS RUN CV
        EnsureVideoTexture();

        if (needColor)
        {
            videoPixels = webcamTex.GetPixels32();
            videoTexture.SetPixels32(videoPixels);
            videoTexture.Apply(false, false);

            if (aspectFitter)
                aspectFitter.aspectRatio = (float)videoTexture.width / videoTexture.height;
        }

        // ---- CV ALWAYS ----
        EnsureCvTexture();

        if (videoPixels == null)
            videoPixels = webcamTex.GetPixels32();

        DownscaleNearest(videoPixels, videoTexture.width, videoTexture.height);
        GenerateGrayscale();

        for (int i = 0; i < cvPixels.Length; i++)
        {
            byte g = cvGray[i];
            cvPixels[i] = new Color32(g, g, g, 255);
        }

        cvTexture.SetPixels32(cvPixels);
        cvTexture.Apply(false, false);

        RunUnifiedStability();
        OverlayStability(cvTexture, stabilityRT);

        cvTicks++;

        // OUTPUT (optional)
        if (enableColorRawImage && rgbRawImage)
            rgbRawImage.texture = videoTexture;

        if (enableColorRenderTexture && colorRT)
            Graphics.Blit(videoTexture, colorRT);

        if (enableCvRawImage && cvRawImage)
            cvRawImage.texture = cvTexture;

        if (enableCvRenderTexture && cvRT)
            Graphics.Blit(cvTexture, cvRT);
    }

    // ============================================================
    // TEXTURE BUILD
    // ============================================================
    private void EnsureVideoTexture()
    {
        if (videoTexture &&
            videoTexture.width == webcamTex.width &&
            videoTexture.height == webcamTex.height)
            return;

        if (videoTexture) Destroy(videoTexture);

        videoTexture = new Texture2D(
            webcamTex.width,
            webcamTex.height,
            TextureFormat.RGBA32,
            false
        );

        lockedAspect = (float)videoTexture.width / videoTexture.height;
        RebuildCvTexture();
    }

    private void EnsureCvTexture()
    {
        if (cvTexture == null)
            RebuildCvTexture();
    }

    private void RebuildCvTexture()
    {
        if (!videoTexture)
            return;

        int targetHeight =
            currentPreset == CvPreset.Low ? lowBaseHeight :
            currentPreset == CvPreset.Max ? videoTexture.height :
            mediumBaseHeight;

        if (lockedAspect <= 0f)
            lockedAspect = (float)videoTexture.width / videoTexture.height;

        int width = Mathf.RoundToInt(targetHeight * lockedAspect);

        if (cvTexture) Destroy(cvTexture);

        cvTexture = new Texture2D(width, targetHeight, TextureFormat.RGBA32, false);

        cvPixels = new Color32[width * targetHeight];
        cvGray = new byte[width * targetHeight];

        if (stabilityRT != null)
        {
            stabilityRT.Release();
            stabilityRT = null;
        }

        stabilityRT = new RenderTexture(width, targetHeight, 0, RenderTextureFormat.ARGB32);
        stabilityRT.enableRandomWrite = true;
        stabilityRT.Create();
    }

    // ============================================================
    // PROCESSING
    // ============================================================
    private void DownscaleNearest(Color32[] src, int srcW, int srcH)
    {
        int dstW = cvTexture.width;
        int dstH = cvTexture.height;

        float stepX = (float)srcW / dstW;
        float stepY = (float)srcH / dstH;

        int i = 0;
        for (int y = 0; y < dstH; y++)
        {
            int srcY = (int)(y * stepY) * srcW;
            for (int x = 0; x < dstW; x++)
                cvPixels[i++] = src[srcY + (int)(x * stepX)];
        }
    }

    private void GenerateGrayscale()
    {
        for (int i = 0; i < cvPixels.Length; i++)
        {
            Color32 c = cvPixels[i];
            cvGray[i] = (byte)((c.r * 77 + c.g * 150 + c.b * 29) >> 8);
        }
    }

    private void RunUnifiedStability()
    {
        if (unifiedStabilityShader == null || cvTexture == null || stabilityRT == null)
            return;

        int kernel = unifiedStabilityShader.FindKernel("CSMain");

        unifiedStabilityShader.SetTexture(kernel, "_Source", cvTexture);
        unifiedStabilityShader.SetTexture(kernel, "_Result", stabilityRT);

        int gx = Mathf.CeilToInt(stabilityRT.width / 8f);
        int gy = Mathf.CeilToInt(stabilityRT.height / 8f);

        unifiedStabilityShader.Dispatch(kernel, gx, gy, 1);
    }

    private void OverlayStability(Texture2D tex, RenderTexture stability)
    {
        if (tex == null || stability == null)
            return;

        if (stabilityMask == null ||
            stabilityMask.width != stability.width ||
            stabilityMask.height != stability.height)
        {
            if (stabilityMask) Destroy(stabilityMask);
            stabilityMask = new Texture2D(stability.width, stability.height, TextureFormat.RGBA32, false);
        }

        RenderTexture.active = stability;
        stabilityMask.ReadPixels(new Rect(0, 0, stability.width, stability.height), 0, 0);
        stabilityMask.Apply();
        RenderTexture.active = null;

        Color32[] video = tex.GetPixels32();
        Color32[] mask = stabilityMask.GetPixels32();

        float s = stabilityOverlayStrength;

        for (int i = 0; i < video.Length; i++)
        {
            byte m = mask[i].r;
            video[i].r = (byte)Mathf.Lerp(video[i].r, m, s);
            video[i].g = (byte)Mathf.Lerp(video[i].g, m, s);
            video[i].b = (byte)Mathf.Lerp(video[i].b, m, s);
        }

        tex.SetPixels32(video);
        tex.Apply(false, false);
    }

    // ============================================================
    // 🔥 RESTORED API (THIS FIXES YOUR ERRORS)
    // ============================================================

    public Texture2D GetCvTexture()
    {
        if (cvTexture == null)
            RebuildCvTexture();
        return cvTexture;
    }

    public bool TryGetLatestCvSnapshot(out Texture2D tex)
    {
        tex = cvTexture;
        return tex != null;
    }

    public Texture2D GetVideoTexture() => videoTexture;
    public byte[] GetCvGray() => cvGray;

    public bool HasValidVideoTexture()
    {
        return videoTexture != null &&
               videoTexture.width > 0 &&
               videoTexture.height > 0;
    }

    // ============================================================
    // UI / CAMERA
    // ============================================================

    private void EnumerateCameras()
    {
        devices = WebCamTexture.devices;

        for (int i = 0; i < cameraButtons.Length; i++)
        {
            if (i < devices.Length)
            {
                int idx = i;

                if (cameraLabels != null && i < cameraLabels.Length && cameraLabels[i])
                    cameraLabels[i].text = devices[i].name;

                cameraButtons[i].onClick.RemoveAllListeners();
                cameraButtons[i].onClick.AddListener(() => SelectCamera(idx));
                cameraButtons[i].gameObject.SetActive(true);
            }
            else
            {
                cameraButtons[i].gameObject.SetActive(false);
            }
        }
    }

    private void SelectCamera(int index)
    {
        if (index < 0 || index >= devices.Length)
            return;

        if (webcamTex)
            webcamTex.Stop();

        webcamTex = new WebCamTexture(devices[index].name);
        webcamTex.Play();
    }

    private void HookPresetButtons()
    {
        if (lowButton) lowButton.onClick.AddListener(() => SetPreset(CvPreset.Low));
        if (mediumButton) mediumButton.onClick.AddListener(() => SetPreset(CvPreset.Medium));
        if (maxButton) maxButton.onClick.AddListener(() => SetPreset(CvPreset.Max));
    }

    private void SetPreset(CvPreset preset)
    {
        if (currentPreset == preset)
            return;

        currentPreset = preset;
        RebuildCvTexture();
    }

    private void UpdateFPS()
    {
        frameCount++;
        fpsTimer += Time.deltaTime;

        if (fpsTimer >= 1f)
        {
            if (fpsText)
                fpsText.text = $"FPS: {frameCount}";

            frameCount = 0;
            fpsTimer = 0f;
        }
    }

    private void UpdateCVRate()
    {
        cvTimer += Time.deltaTime;

        if (cvTimer >= 1f)
        {
            if (cvRateText)
                cvRateText.text = $"CV: {cvTicks}";

            cvTicks = 0;
            cvTimer = 0f;
        }
    }
}