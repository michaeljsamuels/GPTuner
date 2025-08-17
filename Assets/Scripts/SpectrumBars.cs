using System;
using UnityEngine;
using UnityEngine.UI;

public class SpectrumBars : MonoBehaviour
{
    public enum Orientation { HorizontalBottom, VerticalRight, VerticalLeft }

    [Header("Input")]
    public AudioSource source;
    public bool autoBind = true;
    public bool searchSceneForMic = true;

    [Header("FFT")]
    public int fftSize = 1024;
    public FFTWindow window = FFTWindow.Hamming;

    [Header("Frequency Range (Hz)")]
    public float minHz = 50f;
    public float maxHz = 4000f;

    [Header("Bands & Container")]
    public int bands = 48;
    public RectTransform container;
    public Image barPrefab;

    [Header("Layout")]
    public Orientation orientation = Orientation.VerticalRight;
    public float paddingX = 8f;
    public float paddingY = 8f;
    public float spacing  = 2f;

    [Header("Size / Fill")]
    [Range(0.1f, 1f)] public float verticalFill   = 0.9f; // for HorizontalBottom
    [Range(0.1f, 1f)] public float horizontalFill = 0.9f; // for Vertical modes

    [Header("Appearance")]
    public Color barColor = new Color(1, 1, 1, 0.28f);

    [Header("Dynamics")]
    [Range(0f, 0.98f)] public float smoothing = 0.7f;
    public float fallSpeed = 1.5f;
    public float gain = 3000f;
    public float floor = 1e-5f;

    [Header("Debug")]
    public bool debugLogs = true;

    
    public enum FrequencyScale { Linear, Log, Semitone }

    [Header("Frequency Scaling")]
    public FrequencyScale scale = FrequencyScale.Log;  // default to Log as requested

    [Tooltip("Reference for Semitone scale (A4 = 440 by default).")]
    public float a4Hz = 440f;

    [Tooltip("Semitone resolution: 1 = one bar per semitone, 2 = half-steps (100 cents), etc.")]
    [Range(0.25f, 12f)] public float bandsPerSemitone = 1f; // you can try 2 for 50-cent resolution

    
    static float HzToMidi(float hz, float a4 = 440f)
    {
        if (hz <= 0f) return -Mathf.Infinity;
        return 69f + 12f * Mathf.Log(hz / a4, 2f);
    }
    static float MidiToHz(float midi, float a4 = 440f)
    {
        return a4 * Mathf.Pow(2f, (midi - 69f) / 12f);
    }

    
    // Internals
    float[] _fft;
    float[] _bandVals;
    float[] _displayVals;
    int[]   _binStart, _binEnd;
    Image[] _bars;

    float _sampleRate;
    bool  _built;
    bool  _loggedBind;

    RectTransform _rtRoot; // __SpectrumBarsRuntime

    // cached layout
    float _lastW = -1f, _lastH = -1f;
    int   _lastBands = -1;
    float _cachedBarW = 1f, _cachedBarH = 1f;

    // mark-to-rebuild flag (set by OnValidate; applied in Update)
    bool _needsRebuild;

    void Reset()
    {
        if (!container)
        {
            var rt = GetComponent<RectTransform>();
            if (rt) container = rt;
        }
    }

    void OnEnable()
    {
        TryBind();
        EnsureSetup();
    }

    void Start()
    {
        TryBind();
        EnsureSetup();
    }

    void OnValidate()
    {
        fftSize = Mathf.ClosestPowerOfTwo(Mathf.Clamp(fftSize, 256, 8192));
        bands   = Mathf.Clamp(bands, 8, 256);
        minHz   = Mathf.Max(10f, minHz);
        maxHz   = Mathf.Max(minHz + 10f, maxHz);
        spacing = Mathf.Max(0f, spacing);
        gain    = Mathf.Max(1f, gain);
        floor   = Mathf.Max(0f, floor);
        verticalFill   = Mathf.Clamp01(verticalFill);
        horizontalFill = Mathf.Clamp01(horizontalFill);

        // Don’t rebuild during validation; mark and do it safely next frame
        _needsRebuild = true;
    }

    void Update()
    {
        if (_needsRebuild)
        {
            _needsRebuild = false;
            _built = false; // force array + bar rebuild
            EnsureSetup();
        }

        if (autoBind && source == null)
            TryBind();
    }

    void TryBind()
    {
        if (source != null) return;

        // 1) Preferred: the feeder’s source
        if (AudioFeed.Source != null)
        {
            source = AudioFeed.Source;
            _sampleRate = (source && source.clip) ? source.clip.frequency : AudioSettings.outputSampleRate;
            if (!_loggedBind && debugLogs)
            {
                Debug.Log($"[SpectrumBars] Bound to AudioFeed.Source '{source.name}', sr={_sampleRate}");
                _loggedBind = true;
            }
            return;
        }

        // 2) Search scene for mic-like source
        if (searchSceneForMic)
        {
            var sources = FindObjectsOfType<AudioSource>();
            foreach (var s in sources)
            {
                if (!s.isActiveAndEnabled || !s.isPlaying || s.clip == null) continue;
                var n = s.clip.name;
                if (n.IndexOf("microphone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("mic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.clip.length <= 1.05f) // typical looping mic clip
                {
                    source = s;
                    _sampleRate = s.clip.frequency;
                    if (!_loggedBind && debugLogs)
                    {
                        Debug.Log($"[SpectrumBars] Auto-found mic source '{source.name}', sr={_sampleRate}");
                        _loggedBind = true;
                    }
                    return;
                }
            }
        }
        // else leave null (we’ll use AudioListener fallback)
    }

    void EnsureSetup()
    {
        if (!container) return;

        EnsureRuntimeRoot();

        if (_fft == null || _fft.Length != fftSize) _fft = new float[fftSize];
        if (_bandVals == null || _bandVals.Length != bands)
        {
            _bandVals    = new float[bands];
            _displayVals = new float[bands];
            _binStart    = new int[bands];
            _binEnd      = new int[bands];
        }

        if (_bars == null || _bars.Length != bands)
        {
            // pool/build (no destroy)
            BuildBars();
            _lastBands = bands;
        }

        // sample rate
        _sampleRate = 0f;
        if (source && source.clip) _sampleRate = source.clip.frequency;
        if (_sampleRate <= 0f) _sampleRate = AudioSettings.outputSampleRate;

        // bins
    
       // --- Compute band edges in Hz according to selected scale ---
int half = fftSize / 2;
float binHz = _sampleRate / (float)fftSize;

// We’ll fill edgesHz[0..bands] then derive _binStart/_binEnd from those edges
float[] edgesHz = new float[bands + 1];

if (scale == FrequencyScale.Linear)
{
    for (int i = 0; i <= bands; i++)
    {
        float t = (float)i / bands;               // 0..1
        edgesHz[i] = Mathf.Lerp(minHz, maxHz, t); // linear in Hz
    }
}
else if (scale == FrequencyScale.Log)
{
    float logMin = Mathf.Log(minHz);
    float logMax = Mathf.Log(maxHz);
    for (int i = 0; i <= bands; i++)
    {
        float t = (float)i / bands;                   // 0..1
        edgesHz[i] = Mathf.Exp(Mathf.Lerp(logMin, logMax, t)); // exponential = log spacing
    }
}
else // FrequencyScale.Semitone
{
    // Decide semitone edges (can be fractional in 'bandsPerSemitone' steps)
    float midiMin = HzToMidi(minHz, a4Hz);
    float midiMax = HzToMidi(maxHz, a4Hz);
    // Ensure min < max
    if (midiMax <= midiMin) midiMax = midiMin + 0.01f;

    // If 'bands' isn’t aligned with semitone resolution, recompute effective bands
    // so visual bars match the semitone grid cleanly.
    float steps = (midiMax - midiMin) * bandsPerSemitone;
    int effBands = Mathf.Max(1, Mathf.RoundToInt(steps));
    if (effBands != bands)
    {
        // If bands changed, mark for rebuild after this frame (safe pool rebuild)
        bands = effBands;
        _needsRebuild = true;
        // Resize arrays to new 'bands'
        if (_bandVals == null || _bandVals.Length != bands)
        {
            _bandVals    = new float[bands];
            _displayVals = new float[bands];
            _binStart    = new int[bands];
            _binEnd      = new int[bands];
        }
        edgesHz = new float[bands + 1];
    }

    for (int i = 0; i <= bands; i++)
    {
        float midi = midiMin + (i / (float)bandsPerSemitone);
        edgesHz[i] = MidiToHz(midi, a4Hz);
    }
}

// --- Map band edges in Hz to FFT bin ranges ---
for (int b = 0; b < bands; b++)
{
    float hz0 = edgesHz[b];
    float hz1 = edgesHz[b + 1];

    int k0 = Mathf.Clamp(Mathf.RoundToInt(hz0 / binHz), 1, half - 1);
    int k1 = Mathf.Clamp(Mathf.RoundToInt(hz1 / binHz), k0 + 1, half);

    _binStart[b] = k0;
    _binEnd[b]   = k1;
}


        _built = true;
    }

    void EnsureRuntimeRoot()
    {
        if (_rtRoot != null) return;
        var existing = container ? container.Find("__SpectrumBarsRuntime") : null;
        if (existing) _rtRoot = existing as RectTransform;
        if (_rtRoot == null && container)
        {
            var go = new GameObject("__SpectrumBarsRuntime", typeof(RectTransform));
            go.transform.SetParent(container, false);
            _rtRoot = go.GetComponent<RectTransform>();
            _rtRoot.anchorMin = new Vector2(0, 0);
            _rtRoot.anchorMax = new Vector2(1, 1);
            _rtRoot.pivot     = new Vector2(0.5f, 0.5f);
            _rtRoot.offsetMin = Vector2.zero;
            _rtRoot.offsetMax = Vector2.zero;
        }
    }

    void BuildBars()
    {
        if (_rtRoot == null) return;

        // Ensure layout cache is fresh
        _lastW = -1f; _lastH = -1f;
        RecomputeLayout(force:true);

        // Ensure array sized
        if (_bars == null || _bars.Length != bands)
            _bars = new Image[bands];

        // Reuse/create first 'bands' children; deactivate extras
        int childCount = _rtRoot.childCount;

        for (int i = 0; i < bands; i++)
        {
            Image img; RectTransform rt;

            if (i < childCount)
            {
                var go = _rtRoot.GetChild(i).gameObject;
                go.SetActive(true);
                img = go.GetComponent<Image>();
                if (!img) img = go.AddComponent<Image>();
                rt = img.rectTransform;
            }
            else
            {
                img = barPrefab ? Instantiate(barPrefab, _rtRoot) : CreateBar(_rtRoot);
                rt = img.rectTransform;
            }

            img.color = barColor;

            if (orientation == Orientation.HorizontalBottom)
            {
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot     = new Vector2(0f, 0f);

                float x = paddingX + i * (_cachedBarW + spacing);
                rt.anchoredPosition = new Vector2(x, 0f);
                rt.sizeDelta        = new Vector2(_cachedBarW, 1f);
            }
            else
            {
                bool atRight = (orientation == Orientation.VerticalRight);
                rt.anchorMin = atRight ? new Vector2(1f, 0f) : new Vector2(0f, 0f);
                rt.anchorMax = rt.anchorMin;
                rt.pivot     = atRight ? new Vector2(1f, 0f) : new Vector2(0f, 0f);

                float y = paddingY + i * (_cachedBarH + spacing);
                rt.anchoredPosition = atRight ? new Vector2(-paddingX, y) : new Vector2(+paddingX, y);
                rt.sizeDelta        = new Vector2(1f, _cachedBarH);
            }

            _bars[i] = img;
        }

        for (int i = bands; i < childCount; i++)
            _rtRoot.GetChild(i).gameObject.SetActive(false);
    }

    Image CreateBar(RectTransform parent)
    {
        var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    void RecomputeLayout(bool force = false)
    {
        if (container == null) return;

        float w = container.rect.width;
        float h = container.rect.height;
        if (!force && Mathf.Approximately(w, _lastW) && Mathf.Approximately(h, _lastH) && _lastBands == bands) return;

        _lastW = w; _lastH = h; _lastBands = bands;

        if (orientation == Orientation.HorizontalBottom)
        {
            float usableW = Mathf.Max(1f, w - 2f * paddingX);
            _cachedBarW = Mathf.Max(1f, (usableW - (bands - 1) * spacing) / Mathf.Max(1, bands));
        }
        else
        {
            float usableH = Mathf.Max(1f, h - 2f * paddingY);
            _cachedBarH = Mathf.Max(1f, (usableH - (bands - 1) * spacing) / Mathf.Max(1, bands));
        }

        if (_bars != null)
        {
            for (int i = 0; i < _bars.Length; i++)
            {
                var img = _bars[i]; if (!img) continue;
                var rt = img.rectTransform;

                if (orientation == Orientation.HorizontalBottom)
                {
                    float x = paddingX + i * (_cachedBarW + spacing);
                    rt.anchoredPosition = new Vector2(x, 0f);
                    var sd = rt.sizeDelta; sd.x = _cachedBarW; rt.sizeDelta = sd;
                }
                else
                {
                    bool atRight = (orientation == Orientation.VerticalRight);
                    float y = paddingY + i * (_cachedBarH + spacing);
                    rt.anchoredPosition = atRight ? new Vector2(-paddingX, y) : new Vector2(+paddingX, y);
                    var sd = rt.sizeDelta; sd.y = _cachedBarH; rt.sizeDelta = sd;
                }
            }
        }
    }

    void LateUpdate()
    {
        if (!_built || container == null)
        {
            // Safety: if something cleared us mid-play, rebuild once
            EnsureSetup();
            if (!_built) return;
        }

        RecomputeLayout();

        // Pull spectrum
        if (source)
            source.GetSpectrumData(_fft, 0, window);
        else
            AudioListener.GetSpectrumData(_fft, 0, window);

        // Aggregate bins per band
        int half = fftSize / 2;
        for (int b = 0; b < bands; b++)
        {
            int k0 = Mathf.Clamp(_binStart[b], 1, half - 1);
            int k1 = Mathf.Clamp(_binEnd[b],   k0 + 1, half);

            double sum = 0;
            for (int k = k0; k < k1; k++) sum += _fft[k];
            float val = (float)(sum / (k1 - k0));
            if (val < floor) val = 0f;
            _bandVals[b] = val;
        }

        // Smooth + draw
        float dt = Application.isPlaying ? Time.deltaTime : (1f / 60f);
        float rise = 1f - smoothing;
        float fall = fallSpeed * dt;

        // for Vertical modes, bar "length" is width
        float maxH = Mathf.Max(1f, container.rect.height * verticalFill);
        float maxW = Mathf.Max(1f, container.rect.width  * horizontalFill);

        // Debug: simple peak check once per second
        if (debugLogs && Time.frameCount % 60 == 0)
        {
            float peak = 0f;
            for (int i = 2; i < _fft.Length / 2; i++) peak = Mathf.Max(peak, _fft[i]);
            Debug.Log($"[SpectrumBars] bound={(source?source.name:"AudioListener")} peak={peak:0.0000}");
        }

        for (int b = 0; b < bands; b++)
        {
            float target = Mathf.Sqrt(_bandVals[b]) * (gain / 1000f);
            float current = _displayVals[b];
            current = (target > current) ? Mathf.Lerp(current, target, rise) : Mathf.Max(0f, current - fall);
            _displayVals[b] = current;

            var img = _bars[b]; if (!img) continue;
            var rt = img.rectTransform;

            if (orientation == Orientation.HorizontalBottom)
            {
                float h = Mathf.Clamp(current * maxH, 1f, maxH);
                var sd = rt.sizeDelta; sd.y = h; sd.x = _cachedBarW; rt.sizeDelta = sd;
            }
            else
            {
                float wLen = Mathf.Clamp(current * maxW, 1f, maxW);
                var sd = rt.sizeDelta; sd.x = wLen; sd.y = _cachedBarH; rt.sizeDelta = sd;
            }
        }
    }
}
