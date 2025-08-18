using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if TMP_PRESENT || TEXTMESHPRO
using TMPro;
#endif

[ExecuteAlways]
public class NoteTicker : MonoBehaviour
{
    [Header("Layout (UI)")]
    public RectTransform viewport;
    public Graphic centerLine;
    public Graphic noteLabelPrefab;
    public Graphic centerNoteLabelPrefab;

    [Header("Scale")]
    public float pixelsPerSemitone = 48f;
    [Range(2, 36)] public int semitoneRadius = 8;

    [Header("Appearance")]
    public Color centerNoteColor = Color.white;
    public Color sideNoteColor   = new Color(1,1,1,0.6f);
    public float centerNoteScale = 1.35f;
    public float sideNoteScale   = 1.0f;

    public Color inTuneHue  = new Color(0.15f, 0.85f, 0.25f);
    public Color offTuneHue = new Color(0.95f, 0.25f, 0.25f);
    public float inTuneCents = 10f;
    public float hysteresisCents = 2f;

    [Header("Dynamics / Brightness")]
    public float minDb = -60f;
    public float maxDb = -10f;
    [Range(0.25f, 3f)] public float brightnessPower = 1.0f;
    [Range(0f, 0.99f)] public float posSmooth = 0.7f;
    [Range(0f, 0.99f)] public float colorSmooth = 0.85f;
    [Range(0f, 0.99f)] public float brightnessSmooth = 0.7f;

    [Header("Pitch Reference")]
    public float a4Hz = 440f;
    [Range(0f, 1f)] public float confThreshold = 0.15f;

    // ===== NEW: Cents Gauge =====
    [Header("Cents Gauge")]
    [Tooltip("± range shown by the gauge (cents)")]
    public int centsRange = 50;             // ±50¢ by default
    public float gaugeWidth = 8f;           // px
    public float tickLong = 20f;            // px (±50 labels)
    public float tickShort = 10f;           // px (every 10c)
    public Color gaugeColor = new Color(1,1,1,0.5f);
    public Color needleColor = Color.white;
    public float needleThickness = 3f;      // px

    // internal smoothing/state
    float _smPos, _smConf, _smBrightness;
    Color _smHue;
    bool _inTuneVisual;

    static readonly string[] NOTE = { "C","C#","D","D#","E","F","F#","G","G#","A","A#","B" };

    // UI pool
    RectTransform _content;
    List<Graphic> _labels = new List<Graphic>();
    int _poolSemitones;

    // Gauge UI
    RectTransform _gaugeRoot;
    Image _gaugeTrack;
    Image _needle;
    readonly List<Image> _ticks = new List<Image>();
#if TMP_PRESENT || TEXTMESHPRO
    readonly List<TMP_Text> _tickLabelsTMP = new List<TMP_Text>();
#else
    readonly List<Text> _tickLabelsText = new List<Text>();
#endif

    void Reset(){ if (!viewport) viewport = GetComponent<RectTransform>(); }

    void OnEnable(){ EnsureContent(); BuildPool(); BuildGauge(); }
    void OnRectTransformDimensionsChange(){ PositionLabels(_smPos); LayoutGauge(); }
    void OnValidate(){ pixelsPerSemitone = Mathf.Max(8f, pixelsPerSemitone); semitoneRadius = Mathf.Clamp(semitoneRadius,2,36); _needRebuild=true; }
    bool _needRebuild;

    void Update()
    {
        if (_needRebuild){ _needRebuild=false; BuildPool(); BuildGauge(); }

        float hz    = TunerNative.GetLatestPitchHz();
        float cents = TunerNative.GetCentsOffset();
        float conf  = Mathf.Clamp01(TunerNative.GetConfidence());

        // Continuous semitone position: nearestMidi + cents/100
        float a = 1f - posSmooth;
        float nearestMidi = (hz > 0f) ? 69f + 12f * Mathf.Log(Mathf.Max(1e-6f, hz) / a4Hz, 2f) : _smPos;
        float midiRounded = Mathf.Round(nearestMidi);
        float pos = midiRounded + (cents / 100f);

        _smPos  = Mathf.Lerp(_smPos, pos, a);
        _smConf = Mathf.Lerp(_smConf, conf, a);

        // Hue hysteresis
        float absC = Mathf.Abs(cents);
        float enterBand = inTuneCents, exitBand = inTuneCents + Mathf.Max(0f, hysteresisCents);
        if (_inTuneVisual) _inTuneVisual = (absC <= exitBand);
        else               _inTuneVisual = (absC <= enterBand);

        Color targetHue = _inTuneVisual ? inTuneHue : offTuneHue;
        _smHue = Color.Lerp(_smHue, targetHue, 1f - colorSmooth);

        // Brightness (loudness × confidence)
        float loud01 = Mathf.Clamp01(Mathf.InverseLerp(minDb, maxDb, AudioFeed.LevelDb));
        float targetB = Mathf.Pow(loud01 * _smConf, brightnessPower);
        _smBrightness = Mathf.Lerp(_smBrightness, targetB, 1f - brightnessSmooth);

        // Labels & gauge
        PositionLabels(_smPos);
        ApplyColors();
        UpdateGauge(cents);
    }

    // ===== content & labels =====
    void EnsureContent()
    {
        if (!viewport) return;
        var existing = viewport.Find("__NoteTickerContent") as RectTransform;
        if (!existing)
        {
            var go = new GameObject("__NoteTickerContent", typeof(RectTransform));
            go.transform.SetParent(viewport, false);
            existing = go.GetComponent<RectTransform>();
            existing.anchorMin = existing.anchorMax = new Vector2(0.5f,0.5f);
            existing.pivot = new Vector2(0.5f,0.5f);
        }
        _content = existing;

        if (centerLine)
        {
            var rt = centerLine.rectTransform;
            rt.anchorMin = new Vector2(0f,0.5f);
            rt.anchorMax = new Vector2(1f,0.5f);
            rt.pivot     = new Vector2(0.5f,0.5f);
            rt.anchoredPosition = Vector2.zero;
        }
    }

    void BuildPool()
    {
        EnsureContent();
        if (!_content) return;

        _poolSemitones = 2*semitoneRadius + 1;

        for (int i=0;i<_labels.Count;i++) if (_labels[i]) _labels[i].gameObject.SetActive(false);

        for (int i=0;i<_poolSemitones;i++)
        {
            Graphic g = (i < _labels.Count) ? _labels[i] : null;
            if (!g)
            {
                var prefab = (i == semitoneRadius && centerNoteLabelPrefab) ? centerNoteLabelPrefab : noteLabelPrefab;
                if (!prefab)
                {
                    var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
                    go.transform.SetParent(_content, false);
                    var t = go.GetComponent<Text>(); t.alignment = TextAnchor.MiddleCenter; t.fontSize = 36; t.color = Color.white;
                    g = t;
                }
                else g = Instantiate(prefab, _content);

                g.raycastTarget = false;
                if (i >= _labels.Count) _labels.Add(g); else _labels[i] = g;
            }

            g.gameObject.SetActive(true);
            float s = (i == semitoneRadius) ? centerNoteScale : sideNoteScale;
            g.rectTransform.localScale = new Vector3(s,s,1f);
        }

        for (int i=_poolSemitones; i<_labels.Count; i++)
            if (_labels[i]) _labels[i].gameObject.SetActive(false);

        PositionLabels(_smPos);
        ApplyColors();
    }

    void PositionLabels(float semitonePos)
    {
        if (!_content || _labels.Count==0 || !viewport) return;

        float centerFloat = Mathf.Round(semitonePos);
        int centerIndex = (int)centerFloat;
        float frac = semitonePos - centerFloat;

        float step = pixelsPerSemitone;

        for (int i=0;i<_poolSemitones;i++)
        {
            int semitone = centerIndex + (i - semitoneRadius);
            string label = MidiToNameOctave(semitone);

            var g = _labels[i];
            if (!g) continue;

            SetLabelText(g, label);

            float y = (i - semitoneRadius - frac) * step;
            var rt = g.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f,0.5f);
            rt.anchoredPosition = new Vector2(0f, y);

            float dist = Mathf.Abs(i - semitoneRadius - frac);
            float atten = Mathf.Clamp01(1f - (dist / (semitoneRadius + 0.5f)));
            Color baseCol = (i == semitoneRadius) ? centerNoteColor : sideNoteColor;
            baseCol.a *= Mathf.Lerp(0.5f, 1f, atten);
            SetLabelColor(g, baseCol);
        }

        _content.anchoredPosition = Vector2.zero;
    }

    void ApplyColors()
    {
        float b = Mathf.Clamp01(_smBrightness);
        Color hue = _smHue;

        for (int i=0;i<_poolSemitones && i<_labels.Count;i++)
        {
            var g = _labels[i]; if (!g || !g.gameObject.activeSelf) continue;

            float proximity = 1f - Mathf.Clamp01(Mathf.Abs(i - semitoneRadius) / (semitoneRadius + 0.001f));
            float hueMix = Mathf.Lerp(0.35f, 1f, proximity);

            Color baseCol = GetLabelColor(g);
            Color outCol = Color.Lerp(baseCol, hue, hueMix);
            outCol.a *= b;
            SetLabelColor(g, outCol);
        }

        if (centerLine)
        {
            var c = centerLine.color; c.a = Mathf.Clamp01(0.25f + 0.5f * b);
            centerLine.color = c;
        }
    }

    // ======= Cents Gauge =======
    void BuildGauge()
    {
        if (!viewport) return;

        if (!_gaugeRoot)
        {
            var go = new GameObject("__CentsGauge", typeof(RectTransform));
            go.transform.SetParent(viewport, false);
            _gaugeRoot = go.GetComponent<RectTransform>();
            _gaugeRoot.anchorMin = _gaugeRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _gaugeRoot.pivot = new Vector2(0.5f, 0.5f);
            _gaugeRoot.sizeDelta = Vector2.zero;
        }

        if (!_gaugeTrack)
            _gaugeTrack = CreateLine(_gaugeRoot, gaugeWidth, gaugeColor * 0.5f, vertical: true);

        // Total ticks: one every 10 cents from −range..+range (inclusive)
        int total = (centsRange * 2) / 10 + 1;
        EnsureTickPool(total);
        LayoutGauge();

        if (_needle == null)
            _needle = CreateLine(_gaugeRoot, needleThickness, needleColor, vertical: false);
    }


    void EnsureTickPool(int count)
    {
        // Grow ticks
        while (_ticks.Count < count)
            _ticks.Add(CreateLine(_gaugeRoot, tickShort, gaugeColor, vertical: false));

        // Shrink/disable extras
        for (int i = 0; i < _ticks.Count; i++)
            _ticks[i].gameObject.SetActive(i < count);

#if TMP_PRESENT || TEXTMESHPRO
        // Grow TMP labels
        while (_tickLabelsTMP.Count < count)
        {
            var go = new GameObject("TickLabel", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            go.transform.SetParent(_gaugeRoot, false);
            var t = go.GetComponent<TMPro.TextMeshProUGUI>();
            t.fontSize = 20;
            t.alignment = TMPro.TextAlignmentOptions.MidlineRight;
            t.raycastTarget = false;
            t.color = gaugeColor;
            _tickLabelsTMP.Add(t);
        }
        // Enable/disable to match count
        for (int i = 0; i < _tickLabelsTMP.Count; i++)
            _tickLabelsTMP[i].gameObject.SetActive(i < count);
#else
    // Legacy Text labels
    while (_tickLabelsText.Count < count)
    {
        var go = new GameObject("TickLabel", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(_gaugeRoot, false);
        var t = go.GetComponent<Text>();
        t.fontSize = 14;
        t.alignment = TextAnchor.MiddleRight;
        t.raycastTarget = false;
        t.color = gaugeColor;
        _tickLabelsText.Add(t);
    }
    for (int i = 0; i < _tickLabelsText.Count; i++)
        _tickLabelsText[i].gameObject.SetActive(i < count);
#endif
    }


    void LayoutGauge()
    {
        if (!_gaugeRoot || !_gaugeTrack) return;

        float h = viewport.rect.height;
        float trackLen = Mathf.Min(pixelsPerSemitone, h * 0.7f);
        _gaugeTrack.rectTransform.sizeDelta = new Vector2(gaugeWidth, trackLen);

        int totalExpected = (centsRange * 2) / 10 + 1;
        int total = Mathf.Min(totalExpected, _ticks.Count); // safety

        float pxPerCent = pixelsPerSemitone / 100f;

        for (int i = 0; i < total; i++)
        {
            int cents = -centsRange + i * 10;
            float y = cents * pxPerCent;

            var tick = _ticks[i];
            var rt = tick.rectTransform;
            float len = (Mathf.Abs(cents) == centsRange || cents == 0 || Mathf.Abs(cents) == 50) ? tickLong : tickShort;
            rt.sizeDelta = new Vector2(len, needleThickness);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(gaugeWidth * 0.5f + 6f, y);

#if TMP_PRESENT || TEXTMESHPRO
            var tt = (i < _tickLabelsTMP.Count) ? _tickLabelsTMP[i] : null;
            if (tt)
            {
                tt.text = (Mathf.Abs(cents) == 50) ? (cents > 0 ? "+50" : "-50") : "";
                var tr = tt.rectTransform;
                tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
                tr.pivot = new Vector2(1f, 0.5f);
                tr.anchoredPosition = new Vector2(gaugeWidth * 0.5f + len + 10f, y);
            }
#else
        var tt = (i < _tickLabelsText.Count) ? _tickLabelsText[i] : null;
        if (tt)
        {
            tt.text = (Mathf.Abs(cents) == 50) ? (cents > 0 ? "+50" : "-50") : "";
            var tr = tt.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
            tr.pivot = new Vector2(1f, 0.5f);
            tr.anchoredPosition = new Vector2(gaugeWidth * 0.5f + len + 10f, y);
        }
#endif
        }
    }


    void UpdateTickLabels()
    {
        float a = Mathf.Clamp01(_smBrightness);
#if TMP_PRESENT || TEXTMESHPRO
        for (int i = 0; i < _tickLabelsTMP.Count; i++)
        {
            var t = _tickLabelsTMP[i];
            if (t && t.gameObject.activeSelf) t.color = gaugeColor * new Color(1,1,1,a);
        }
#else
    for (int i = 0; i < _tickLabelsText.Count; i++)
    {
        var t = _tickLabelsText[i];
        if (t && t.gameObject.activeSelf) t.color = gaugeColor * new Color(1,1,1,a);
    }
#endif
        if (_gaugeTrack) _gaugeTrack.color = gaugeColor * new Color(1,1,1, a*0.8f);
    }

    void UpdateGauge(float cents)
    {
        if (_needle == null) return;
        // needle Y in pixels: 100¢ = one semitone of travel
        float pxPerCent = pixelsPerSemitone / 100f;
        float clamped = Mathf.Clamp(cents, -centsRange, centsRange);
        var rt = _needle.rectTransform;
        rt.sizeDelta = new Vector2(pixelsPerSemitone*0.9f, needleThickness);
        rt.anchoredPosition = new Vector2(0f, clamped * pxPerCent);

        // Colors with brightness & hue
        var needleCol = Color.Lerp(needleColor, _smHue, 0.5f);
        needleCol.a *= Mathf.Clamp01(_smBrightness);
        _needle.color = needleCol;

        // keep gauge elements bright/dim with signal
        UpdateTickLabels();
    }

    // ===== helpers =====
    static string MidiToNameOctave(int midi)
    {
        int note = ((midi % 12) + 12) % 12;
        int octave = (midi / 12) - 1;
        return NOTE[note] + octave.ToString();
    }

    static Image CreateLine(RectTransform parent, float thickness, Color color, bool vertical)
    {
        var go = new GameObject("Line", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        img.color = color;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f,0.5f);
        rt.pivot = new Vector2(0.5f,0.5f);
        rt.sizeDelta = vertical ? new Vector2(thickness, 100f) : new Vector2(100f, thickness);
        return img;
    }

    static void SetLabelText(Graphic g, string s)
    {
        if (!g) return;
#if TMP_PRESENT || true
        var tmp = g.GetComponent<TMP_Text>() ?? g.GetComponentInChildren<TMP_Text>(true);
        if (tmp) { tmp.text = s; return; }
#endif
        var t = g.GetComponent<Text>() ?? g.GetComponentInChildren<Text>(true);
        if (t) t.text = s;
    }
    static Color GetLabelColor(Graphic g)
    {
#if TMP_PRESENT || true
        var tmp = g.GetComponent<TMP_Text>() ?? g.GetComponentInChildren<TMP_Text>(true);
        if (tmp) return tmp.color;
#endif
        var t = g.GetComponent<Text>() ?? g.GetComponentInChildren<Text>(true);
        if (t) return t.color;
        return g ? g.color : Color.white;
    }
    static void SetLabelColor(Graphic g, Color c)
    {
        if (!g) return;
#if TMP_PRESENT || true
        var tmp = g.GetComponent<TMP_Text>() ?? g.GetComponentInChildren<TMP_Text>(true);
        if (tmp) { tmp.color = c; return; }
#endif
        var t = g.GetComponent<Text>() ?? g.GetComponentInChildren<Text>(true);
        if (t) { t.color = c; return; }
        g.color = c;
    }
}
