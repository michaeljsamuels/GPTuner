// Assets/Scripts/TunerDisplay.cs
using System;
using UnityEngine;
using TMPro;
#if TMP_PRESENT || TEXTMESHPRO
using TMPro;
#endif

public class TunerDisplay : MonoBehaviour
{
    [Header("UI (assign one of each)")]
#if TMP_PRESENT || TEXTMESHPRO
    public TMP_Text noteTMP;
    public TMP_Text centsTMP;
#endif
    public TMP_Text noteText;
    public TMP_Text centsText;

    [Header("Visuals")]
    public Camera targetCamera;                 // If null, uses Camera.main
    public Color inTuneColor  = new Color(0.15f, 0.85f, 0.25f);   // green-ish
    public Color offTuneColor = new Color(0.95f, 0.25f, 0.25f);   // red-ish

    [Tooltip("How fast hue moves toward the target color (0=snappy, 0.9=slow)")]
    [Range(0f, 0.99f)] public float colorSmooth = 0.85f;

    [Header("Pitch / Confidence")]
    public float a4Hz = 440f;                   // Keep in sync with TunerNative.SetA4Hz
    [Tooltip("Only show readings when smoothed confidence >= this")]
    [Range(0f, 1f)] public float confThreshold = 0.15f;

    [Tooltip("± cents for being considered 'in tune'")]
    public float inTuneCents = 10f;

    [Tooltip("Extra leeway to exit/enter the in-tune band to avoid flicker")]
    public float hysteresisCents = 2f;

    [Tooltip("Smoothing for pitch/cent/conf values (0=no smoothing, 0.7 steady)")]
    [Range(0f, 0.99f)] public float valueSmooth = 0.7f;

    [Header("Brightness (Loudness × Confidence)")]
    [Tooltip("dB level considered silence")]
    public float minDb = -60f;

    [Tooltip("dB level considered loud")]
    public float maxDb = -10f;

    [Tooltip("Emphasize loud signals (>1) or make quiet more visible (<1)")]
    [Range(0.25f, 3f)] public float brightnessPower = 1.0f;

    [Tooltip("Smoothing for brightness (0=snappy, 0.9=slow)")]
    [Range(0f, 0.99f)] public float brightnessSmooth = 0.7f;

    // internal smoothing state
    float _smHz, _smCents, _smConf;
    bool  _inTuneVisual;        // hysteresis state machine (visual)
    Color _smColor;             // smoothed hue
    float _smBrightness;        // smoothed brightness (0..1)

    static readonly string[] NOTE_NAMES = { "C","C#","D","D#","E","F","F#","G","G#","A","A#","B" };

    void Awake()
    {
        if (!targetCamera) targetCamera = Camera.main;
        _smColor = offTuneColor;
        _smBrightness = 0f;
    }

    void Update()
    {
        // 1) Pull from native tuner (on iOS device; Editor stubs return 0)
        float hz    = TunerNative.GetLatestPitchHz();
        float conf  = Mathf.Clamp01(TunerNative.GetConfidence());
        float cents = TunerNative.GetCentsOffset();  // relative to nearest ET note

        // 2) Smooth values
        float a = 1f - valueSmooth;
        _smHz    = Mathf.Lerp(_smHz,    hz,    a);
        _smCents = Mathf.Lerp(_smCents, cents, a);
        _smConf  = Mathf.Lerp(_smConf,  conf,  a);

        // 3) Have a usable pitch?
        bool havePitch = (_smHz > 0.01f) && (_smConf >= confThreshold);

        // 4) Nearest note/octave label (from smoothed Hz)
        string noteStr = "—";
        string centsStr = havePitch ? $"{_smCents:+0.0;-0.0;0} ¢" : "Listening…";

        if (havePitch)
        {
            float midi = 69f + 12f * Mathf.Log(_smHz / Mathf.Max(1e-6f, a4Hz), 2f); // A4=440
            int midiRounded = Mathf.RoundToInt(midi);
            int noteIndex = (midiRounded % 12 + 12) % 12;
            int octave = (midiRounded / 12) - 1;
            noteStr = $"{NOTE_NAMES[noteIndex]}{octave}";
        }

        // 5) Update text (supports Text or TMP)
#if TMP_PRESENT || TEXTMESHPRO
        if (noteTMP)  noteTMP.text  = noteStr;
        if (centsTMP) centsTMP.text = centsStr;
#endif
        if (noteText)  noteText.text  = noteStr;
        if (centsText) centsText.text = centsStr;

        // 6) Decide target hue (green vs red) with hysteresis
        //    Enter green at inTuneCents; leave green only after exceeding inTuneCents + hysteresisCents.
        float absCents = Mathf.Abs(_smCents);
        float enterBand = inTuneCents;
        float exitBand  = inTuneCents + Mathf.Max(0f, hysteresisCents);

        if (_inTuneVisual)
            _inTuneVisual = (absCents <= exitBand);
        else
            _inTuneVisual = (absCents <= enterBand);

        Color targetHue = (_inTuneVisual && havePitch) ? inTuneColor : offTuneColor;

        // 7) Loudness → brightness (alpha-like), combining RMS (from AudioFeed) and confidence
        //    RMS is already computed where you publish mic blocks: AudioFeed.LevelDb
        float rmsDb = AudioFeed.LevelDb; // -inf..0 typically
        float loud01 = Mathf.InverseLerp(minDb, maxDb, rmsDb); // 0..1
        loud01 = Mathf.Clamp01(loud01);

        // Final brightness: (loudness * confidence) ^ power
        float targetBrightness = Mathf.Pow(loud01 * Mathf.Clamp01(_smConf), brightnessPower);

        // 8) Smooth hue + brightness to avoid flicker
        _smColor      = Color.Lerp(_smColor, targetHue, 1f - colorSmooth);
        _smBrightness = Mathf.Lerp(_smBrightness, targetBrightness, 1f - brightnessSmooth);

        // 9) Apply to background: mix from black by brightness toward target hue
        //    Camera.backgroundColor has no alpha—so we simulate by lerping from black to target hue.
        if (targetCamera)
        {
            Color final = Color.Lerp(Color.black, _smColor, Mathf.Clamp01(_smBrightness));
            targetCamera.backgroundColor = final;
        }
    }

    // Optional helper if you want to sync A4 at runtime from elsewhere
    public void SetA4(float newA4)
    {
        a4Hz = Mathf.Clamp(newA4, 200f, 500f);
        TunerNative.SetA4Hz(a4Hz);
    }
}
