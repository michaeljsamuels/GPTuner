// Assets/Scripts/NoteTicker.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if TMP_PRESENT || TEXTMESHPRO
using TMPro;
#endif

/// <summary>
/// A vertical "ticker tape" of note names where the center represents the
/// nearest equal-tempered note; the tape scrolls up/down by cents.
/// - Uses TunerNative.GetLatestPitchHz / GetCentsOffset / GetConfidence
/// - Brightness driven by (AudioFeed.LevelDb remapped 0..1) * confidence
/// - Works with TMP or legacy Text (choose via editor by setting prefabs)
/// </summary>
[ExecuteAlways]
public class NoteTicker : MonoBehaviour
{
    [Header("Layout (UI)")]
    [Tooltip("Viewport RectTransform (usually has a Mask). The ticker is centered inside this.")]
    public RectTransform viewport;

    [Tooltip("Optional: a thin line/graphic at center (for visual reference).")]
    public Graphic centerLine;

    [Tooltip("Prefab for normal note label (TextMeshProUGUI or legacy Text).")]
    public Graphic noteLabelPrefab;

    [Tooltip("Prefab for the CENTER note label (bigger/bolder). If null, normal prefab is reused & scaled).")]
    public Graphic centerNoteLabelPrefab;

    [Header("Scale")]
    [Tooltip("Pixels per semitone (100 cents).")]
    public float pixelsPerSemitone = 48f;

    [Tooltip("How many semitones to show above/below the center (total = 2*radius+1 labels).")]
    [Range(2, 36)] public int semitoneRadius = 8;

    [Header("Appearance")]
    public Color centerNoteColor = Color.white;
    public Color sideNoteColor   = new Color(1,1,1,0.6f);
    public float centerNoteScale = 1.35f;
    public float sideNoteScale   = 1.0f;

    [Tooltip("When in tune (± inTuneCents) we tint toward green; else red.")]
    public Color inTuneHue  = new Color(0.15f, 0.85f, 0.25f);
    public Color offTuneHue = new Color(0.95f, 0.25f, 0.25f);
    public float inTuneCents = 10f;
    public float hysteresisCents = 2f;

    [Header("Dynamics / Brightness")]
    [Tooltip("dB floor -> 0 brightness, ceiling -> 1 brightness.")]
    public float minDb = -60f;
    public float maxDb = -10f;

    [Tooltip("Brightness = (loudness * confidence)^power.")]
    [Range(0.25f, 3f)] public float brightnessPower = 1.0f;

    [Range(0f, 0.99f)] public float posSmooth = 0.7f;
    [Range(0f, 0.99f)] public float colorSmooth = 0.85f;
    [Range(0f, 0.99f)] public float brightnessSmooth = 0.7f;

    [Header("Pitch Reference")]
    public float a4Hz = 440f;

    // ----- runtime state -----
    RectTransform _content;              // internal container holding labels
    List<Graphic> _labels = new List<Graphic>();
    int _poolSemitones;                  // how many labels in pool

    float _smPos;                        // smoothed continuous semitone position (nearestMidi + cents/100)
    float _smConf;                       // smoothed confidence 0..1
    Color _smHue;                        // smoothed hue between inTuneHue / offTuneHue
    float _smBrightness;                 // smoothed brightness 0..1
    bool _inTuneVisual;                  // hysteresis for hue

    static readonly string[] NOTE = { "C","C#","D","D#","E","F","F#","G","G#","A","A#","B" };

    void Reset()
    {
        if (!viewport) viewport = GetComponent<RectTransform>();
    }

    void OnEnable()
    {
        EnsureContent();
        BuildPool();
    }

    void OnRectTransformDimensionsChange()
    {
        // keep labels laid out correctly when rotated/resized
        PositionLabels(_smPos);
    }

    void OnValidate()
    {
        pixelsPerSemitone = Mathf.Max(8f, pixelsPerSemitone);
        semitoneRadius    = Mathf.Clamp(semitoneRadius, 2, 36);
        _needRebuild = true;
    }

    bool _needRebuild;

    void Update()
    {
        if (_needRebuild)
        {
            _needRebuild = false;
            BuildPool();
        }

        // 1) Pull pitch info
        float hz    = TunerNative.GetLatestPitchHz();
        float cents = TunerNative.GetCentsOffset();
        float conf  = Mathf.Clamp01(TunerNative.GetConfidence());

        // Continuous semitone position: nearestMidi + cents/100
        // If no hz, don't change drastically; lerp toward last
        float nearestMidi = (hz > 0f) ? (69f + 12f * Mathf.Log(Mathf.Max(1e-6f, hz) / a4Hz, 2f)) : _smPos;
        float midiRounded = Mathf.Round(nearestMidi);
        float pos = midiRounded + (cents / 100f);

        // 2) Smooth position & confidence
        float a = 1f - posSmooth;
        _smPos = Mathf.Lerp(_smPos, pos, a);
        _smConf = Mathf.Lerp(_smConf, conf, a);

        // 3) Decide hue via hysteresis around inTune band
        float absCents = Mathf.Abs(cents);
        float enterBand = inTuneCents;
        float exitBand  = inTuneCents + Mathf.Max(0f, hysteresisCents);
        if (_inTuneVisual) _inTuneVisual = (absCents <= exitBand);
        else               _inTuneVisual = (absCents <= enterBand);

        Color targetHue = _inTuneVisual ? inTuneHue : offTuneHue;
        _smHue = Color.Lerp(_smHue, targetHue, 1f - colorSmooth);

        // 4) Brightness from loudness × confidence
        float rmsDb = AudioFeed.LevelDb;
        float loud01 = Mathf.Clamp01(Mathf.InverseLerp(minDb, maxDb, rmsDb));
        float targetBrightness = Mathf.Pow(loud01 * _smConf, brightnessPower);
        _smBrightness = Mathf.Lerp(_smBrightness, targetBrightness, 1f - brightnessSmooth);

        // 5) Place labels (center note at center, others every semitone)
        PositionLabels(_smPos);

        // 6) Colorize alpha by brightness; center label tinted toward hue stronger
        ApplyColors();
    }

    // ---------- UI building ----------

    void EnsureContent()
    {
        if (!viewport) return;

        var existing = viewport.Find("__NoteTickerContent") as RectTransform;
        if (!existing)
        {
            var go = new GameObject("__NoteTickerContent", typeof(RectTransform));
            go.transform.SetParent(viewport, false);
            existing = go.GetComponent<RectTransform>();
            existing.anchorMin = new Vector2(0.5f, 0.5f);
            existing.anchorMax = new Vector2(0.5f, 0.5f);
            existing.pivot     = new Vector2(0.5f, 0.5f);
            existing.anchoredPosition = Vector2.zero;
            existing.sizeDelta = Vector2.zero;
        }
        _content = existing;

        // Optional center line alignment
        if (centerLine)
        {
            var rt = centerLine.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }
    }

    void BuildPool()
    {
        EnsureContent();
        if (!_content) return;

        // Target pool size
        _poolSemitones = 2 * semitoneRadius + 1;

        // Create/reuse labels
        // First deactivate all
        for (int i = 0; i < _labels.Count; i++)
            if (_labels[i]) _labels[i].gameObject.SetActive(false);

        // Ensure we have enough
        for (int i = 0; i < _poolSemitones; i++)
        {
            Graphic g = (i < _labels.Count) ? _labels[i] : null;
            if (!g)
            {
                var prefab = (i == semitoneRadius && centerNoteLabelPrefab) ? centerNoteLabelPrefab : noteLabelPrefab;
                if (!prefab) // create a simple Text if none provided
                {
                    var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
                    go.transform.SetParent(_content, false);
                    var t = go.GetComponent<Text>();
                    t.alignment = TextAnchor.MiddleCenter;
                    t.fontSize = 36;
                    t.color = Color.white;
                    g = t;
                }
                else
                {
                    g = Instantiate(prefab, _content);
                }

                // Ensure raycast off for perf
                g.raycastTarget = false;

                if (i >= _labels.Count) _labels.Add(g);
                else _labels[i] = g;
            }

            g.gameObject.SetActive(true);

            // Scale center vs side
            float s = (i == semitoneRadius) ? centerNoteScale : sideNoteScale;
            g.rectTransform.localScale = new Vector3(s, s, 1f);
        }

        // Trim extras if any
        if (_labels.Count > _poolSemitones)
        {
            for (int i = _poolSemitones; i < _labels.Count; i++)
                if (_labels[i]) _labels[i].gameObject.SetActive(false);
        }

        // Initial position
        PositionLabels(_smPos);
        ApplyColors();
    }

    void PositionLabels(float semitonePos)
    {
        if (!_content || _labels.Count == 0 || !viewport) return;

        // Center label represents nearest semitone integer around semitonePos
        // We’ll place labels at integer semitone indices: baseIndex + offset
        float centerFloat = Mathf.Round(semitonePos);      // integer center
        int centerIndex = (int)centerFloat;

        // fractional offset within a semitone; positive => we are sharp, tape scrolls upward
        float frac = semitonePos - centerFloat; // -0.5..+0.5 typically

        float step = pixelsPerSemitone;
        float halfH = viewport.rect.height * 0.5f;

        for (int i = 0; i < _poolSemitones; i++)
        {
            int semitone = centerIndex + (i - semitoneRadius);
            string label = MidiToNameOctave(semitone);

            var g = _labels[i];
            if (!g) continue;

            // set text
#if TMP_PRESENT || TEXTMESHPRO
            var tmp = g as TMP_Text;
            if (tmp) tmp.text = label;
#endif
            if (g is Text lt) lt.text = label;

            // y position: center is zero; up is sharp
            float y = (i - semitoneRadius - frac) * step;
            var rt = g.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);

            // Optional: fade distant labels slightly by distance
            float dist = Mathf.Abs(i - semitoneRadius - frac);
            float atten = Mathf.Clamp01(1f - (dist / (semitoneRadius + 0.5f)));
            Color baseCol = (i == semitoneRadius) ? centerNoteColor : sideNoteColor;
            baseCol.a *= Mathf.Lerp(0.5f, 1f, atten); // subtle distance fade; final alpha applied in ApplyColors
#if TMP_PRESENT || TEXTMESHPRO
            if (tmp) tmp.color = baseCol;
#endif
            if (g is Text lt2) lt2.color = baseCol;
            else g.color = baseCol;
        }

        // Ensure content itself is centered in viewport
        _content.anchoredPosition = Vector2.zero;
    }

    void ApplyColors()
    {
        // Brightness gates the whole ticker; hue is strongest at the center label
        float b = Mathf.Clamp01(_smBrightness);
        Color hue = _smHue;

        for (int i = 0; i < _poolSemitones && i < _labels.Count; i++)
        {
            var g = _labels[i];
            if (!g || !g.gameObject.activeSelf) continue;

            // Read current base color (set in PositionLabels) and modulate toward hue by proximity
            float proximity = 1f - Mathf.Clamp01(Mathf.Abs(i - semitoneRadius) / (semitoneRadius + 0.001f));
            float hueMix = Mathf.Lerp(0.35f, 1f, proximity); // center label gets most hue

            Color baseCol =
#if TMP_PRESENT || TEXTMESHPRO
                (g is TMP_Text tt) ? tt.color :
#endif
                (g is Text t ? t.color : g.color);

            // Mix baseCol → hue by hueMix, then scale alpha by brightness b
            Color outCol = Color.Lerp(baseCol, hue, hueMix);
            outCol.a *= b;

#if TMP_PRESENT || TEXTMESHPRO
            if (g is TMP_Text tt2) { tt2.color = outCol; continue; }
#endif
            if (g is Text t2)      { t2.color = outCol; continue; }
            g.color = outCol;
        }

        // Center line brightness too (optional)
        if (centerLine)
        {
            var c = centerLine.color; c.a = Mathf.Clamp01(0.25f + 0.5f * b);
            centerLine.color = c;
        }
    }

    // ---------- Helpers ----------
    static string MidiToNameOctave(int midi)
    {
        int note = ((midi % 12) + 12) % 12;
        int octave = (midi / 12) - 1;
        return NOTE[note] + octave.ToString();
    }
}
