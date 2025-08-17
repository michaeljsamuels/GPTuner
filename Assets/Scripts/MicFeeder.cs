using System.Collections;
using UnityEngine;

public class MicFeeder : MonoBehaviour
{
    AudioSource _src;
    string _device;
    bool _ready;
    int _sr;
    int _readHead;
    float[] _pull;

    public UnityEngine.Audio.AudioMixerGroup capture;

    [Header("Mic")]
    public int requestedMicHz = 48000;

    [Header("Tuner params (iOS)")]
    public float a4Hz = 440f;
    public Vector2 minMaxHz = new Vector2(70f, 1200f);

    IEnumerator Start()
    {
#if UNITY_IOS && !UNITY_EDITOR
        // Start native tuner only on iOS device
        TunerNative.StartTuner();
        TunerNative.SetA4Hz(a4Hz);
        TunerNative.SetMinMaxFreq(minMaxHz.x, minMaxHz.y);
#endif

        _src = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        _src.loop = true; _src.playOnAwake = false;
        _src.volume = 1f; _src.mute = false;             // keep non-zero spectrum
        _src.bypassEffects = _src.bypassListenerEffects = _src.bypassReverbZones = true;
        _src.outputAudioMixerGroup = capture;

        if (Microphone.devices.Length == 0) { Debug.LogError("No mic"); yield break; }
        _device = Microphone.devices[0];
        var clip = Microphone.Start(_device, true, 1, requestedMicHz);

        float t0 = Time.realtimeSinceStartup;
        while (Microphone.GetPosition(_device) <= 0 && Time.realtimeSinceStartup - t0 < 3f) yield return null;
        if (Microphone.GetPosition(_device) <= 0) { Debug.LogError("Mic failed"); yield break; }

        _src.clip = clip; _src.Play();
        _sr = _src.clip.frequency;
        _pull = new float[Mathf.Max(256, _sr / 20)]; // ~50 ms
        _readHead = 0;
        _ready = true;

        // Let SpectrumBars bind to the exact source
        AudioFeed.SetSource(_src, _sr);
    }

    void Update()
    {
        if (!_ready || _src.clip == null) return;

        int clipSamples = _src.clip.samples;
        int micPos = Microphone.GetPosition(_device);
        int available = (micPos - _readHead + clipSamples) % clipSamples;
        if (available <= 0) return;

        int toRead = Mathf.Min(available, _pull.Length);
        int first = Mathf.Min(toRead, clipSamples - _readHead);
        if (first > 0) _src.clip.GetData(_pull, _readHead);
        if (toRead > first) _src.clip.GetData(_pull, 0);

#if UNITY_IOS && !UNITY_EDITOR
        // Push to native tuner ONLY on iOS device
        TunerNative.PushAudioBuffer(_pull, toRead, 1, _sr);
#endif
        // Always publish for Spectrum/UI (Editor + iOS)
        AudioFeed.PublishSamples(_pull, toRead, 1, _sr);

        _readHead = (_readHead + toRead) % clipSamples;
        
    }

    void OnDestroy()
    {
#if UNITY_IOS && !UNITY_EDITOR
        TunerNative.StopTuner();
#endif
        if (!string.IsNullOrEmpty(_device)) Microphone.End(_device);
        AudioFeed.Clear();
    }
}
