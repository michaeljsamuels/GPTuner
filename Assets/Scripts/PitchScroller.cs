using System;
using UnityEngine;

/// <summary>
/// Time-evolving pitch visualization using a LineRenderer.
/// - Landscape: scrolls horizontally (time on X), pitch on Y
/// - Portrait:  scrolls vertically   (time on Y), pitch on X
/// Only advances when confidence >= threshold.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class PitchScroller : MonoBehaviour
{
    [Header("Pitch input")]
    [Tooltip("Only draw when native confidence >= this.")]
    public float confidenceThreshold = 0.15f;

    [Tooltip("Reference tuning (must match native).")]
    public float a4Hz = 440f;

    [Tooltip("Display range (Hz). E2≈82 Hz; E6≈1319 Hz.")]
    public float minHz = 70f, maxHz = 1200f;

    [Header("Timeline")]
    [Tooltip("How many seconds of history are visible.")]
    public float secondsOnScreen = 10f;

    [Tooltip("How many points per second to sample/draw.")]
    public int samplesPerSecond = 60;

    [Tooltip("Smoothing factor for Hz & cents (0 = none, 0.7 = smooth).")]
    [Range(0f, 0.95f)] public float smoothing = 0.6f;

    [Header("Style")]
    public float lineWidth = 0.02f;       // in world units (scale as needed)
    public Color lineColor = Color.white;

    // internals
    LineRenderer lr;
    Vector3[] pts;
    int capacity;          // total points (secondsOnScreen * samplesPerSecond)
    int head;              // index of newest sample in ring buffer
    float sampleAccum;     // time accumulator for sampling
    float smoothedHz, smoothedCents, smoothedConf;

    // cached mapping
    float minMidi, maxMidi;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.useWorldSpace = false;               // local space, easy to scale inside a parent
        lr.alignment = LineAlignment.View;      // face camera
        lr.textureMode = LineTextureMode.Stretch;
        lr.widthMultiplier = lineWidth;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        lr.colorGradient = MakeFlatGradient(lineColor);

        ReinitBuffer();
        CacheMidiBounds();
    }

    void OnValidate()
    {
        lr = GetComponent<LineRenderer>();
        secondsOnScreen = Mathf.Max(1f, secondsOnScreen);
        samplesPerSecond = Mathf.Clamp(samplesPerSecond, 10, 240);
        minHz = Mathf.Max(1f, minHz);
        maxHz = Mathf.Max(minHz + 1f, maxHz);
        if (lr != null) { lr.widthMultiplier = lineWidth; lr.colorGradient = MakeFlatGradient(lineColor); }
        CacheMidiBounds();
        ReinitBuffer();
    }

    void CacheMidiBounds()
    {
        minMidi = HzToMidi(minHz);
        maxMidi = HzToMidi(maxHz);
    }

    void ReinitBuffer()
    {
        capacity = Mathf.Max(8, Mathf.RoundToInt(secondsOnScreen * samplesPerSecond));
        pts = new Vector3[capacity];
        lr.positionCount = capacity;
        head = -1;

        // initialize to off-screen flat line (so nothing shows until we draw)
        for (int i = 0; i < capacity; i++) pts[i] = Vector3.positiveInfinity;
        lr.SetPositions(pts);
    }

    void Update()
    {
        // Pull native values
        float hz   = TunerNative.GetLatestPitchHz();
        float conf = TunerNative.GetConfidence();
        float cents = TunerNative.GetCentsOffset();

        // Smooth
        float a = 1f - smoothing;
        smoothedHz    = Mathf.Lerp(smoothedHz,    hz,    a);
        smoothedCents = Mathf.Lerp(smoothedCents, cents, a);
        smoothedConf  = Mathf.Lerp(smoothedConf,  conf,  a);

        // Decide whether to sample this frame
        sampleAccum += Time.deltaTime;
        float sampleInterval = 1f / samplesPerSecond;

        while (sampleAccum >= sampleInterval)
        {
            sampleAccum -= sampleInterval;

            // Only advance when confident
            if (smoothedHz > 0f && smoothedConf >= confidenceThreshold)
            {
                AdvanceAndWrite(smoothedHz);
            }
            else
            {
                // Option: pause scroll (do nothing). If you prefer gaps:
                // AdvanceAndWrite(float.NaN); // but LineRenderer can't draw NaN; we keep pause.
            }
        }

        // Optional: adjust width for screen size if you parent this under a scaled object.
        lr.widthMultiplier = lineWidth;
    }

    void AdvanceAndWrite(float hz)
    {
        // ring head
        head = (head + 1) % capacity;

        // compute normalized pitch position (0..1) using log scale
        float midi = HzToMidi(hz);
        float t = Mathf.InverseLerp(minMidi, maxMidi, midi); // clamp inside
        t = Mathf.Clamp01(t);

        // convert to local coords in [-0.5..+0.5] box; we'll map orientation below
        // the "long axis" is time, the "short axis" is pitch
        bool landscape = Screen.width >= Screen.height;

        // Time index -> along long axis from 0..1
        // Newest on the far right (landscape) or top (portrait).
        // We fill pts so that logical time order is preserved visually.
        for (int i = 0; i < capacity; i++)
        {
            int src = (head - (capacity - 1 - i) + capacity) % capacity; // oldest at i=0, newest at i=capacity-1
            Vector3 p = pts[src];

            // keep previous pitch value if untouched; Vector3.positiveInfinity => not drawn yet
            float pitch01;
            if (float.IsPositiveInfinity(p.x) || float.IsPositiveInfinity(p.y))
                pitch01 = t; // initialize
            else
                pitch01 = landscape ? Remap01(p.y, -0.5f, +0.5f) : Remap01(p.x, -0.5f, +0.5f);

            // place point i along axis 0..1
            float time01 = (float)i / (capacity - 1);

            // map pitch01 (0..1) to -0.5..+0.5 on the short axis
            float pitchAxis = Mathf.Lerp(-0.5f, +0.5f, (i == capacity - 1) ? t : pitch01);

            // finally build local position
            pts[i] = landscape
                ? new Vector3(Mathf.Lerp(-0.5f, +0.5f, time01), pitchAxis, 0f)
                : new Vector3(pitchAxis, Mathf.Lerp(-0.5f, +0.5f, time01), 0f);
        }

        lr.SetPositions(pts);
    }

    static float HzToMidi(float hz, float a4 = 440f) => 69f + 12f * Mathf.Log(hz / Mathf.Max(1e-6f, a4), 2f);

    float HzToMidi(float hz) => HzToMidi(hz, a4Hz);

    static float Remap01(float v, float a, float b) => Mathf.InverseLerp(a, b, v);

    static Gradient MakeFlatGradient(Color c)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(c.a, 0f), new GradientAlphaKey(c.a, 1f) }
        );
        return g;
    }
}
