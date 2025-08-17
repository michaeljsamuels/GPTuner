// Assets/Scripts/AudioFeed.cs
using System;
using UnityEngine;

public static class AudioFeed
{
    public static AudioSource Source { get; private set; }
    public static int SampleRate { get; private set; }

    // NEW: publish a simple loudness proxy (RMS of the last block)
    public static float LevelRms { get; private set; }   // 0..~1 typical mic, often <= 0.3
    public static float LevelDb  => 20f * Mathf.Log10(Mathf.Max(1e-7f, LevelRms));

    static float[] _latest;
    static readonly object _lock = new object();

    public static void SetSource(AudioSource src, int sampleRate = 0)
    {
        Source = src;
        SampleRate = sampleRate > 0
            ? sampleRate
            : (src && src.clip ? src.clip.frequency : AudioSettings.outputSampleRate);
#if UNITY_EDITOR
        Debug.Log($"[AudioFeed] Source='{(src ? src.name : "null")}', sr={SampleRate}");
#endif
    }

    public static void PublishSamples(float[] data, int count, int channels, int sampleRate)
    {
        if (data == null || count <= 0) return;
        lock (_lock)
        {
            if (_latest == null || _latest.Length < count) _latest = new float[count];
            Array.Copy(data, _latest, count);
            SampleRate = sampleRate;

            // NEW: compute mono RMS for brightness control
            double s = 0;
            for (int i = 0; i < count; i++) s += (double)data[i] * data[i];
            LevelRms = (float)Math.Sqrt(s / Math.Max(1, count));
        }
    }

    public static int CopyLatest(ref float[] dst)
    {
        lock (_lock)
        {
            if (_latest == null) return 0;
            if (dst == null || dst.Length < _latest.Length) dst = new float[_latest.Length];
            Array.Copy(_latest, dst, _latest.Length);
            return _latest.Length;
        }
    }

    public static void Clear()
    {
        lock (_lock) { _latest = null; LevelRms = 0f; }
        Source = null; SampleRate = 0;
    }
}