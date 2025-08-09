// Assets/Scripts/MicFeederIOS.cs
using System.Collections;
using UnityEngine;

public class MicFeederIOS : MonoBehaviour
{
#if UNITY_IOS && !UNITY_EDITOR
    string _device;
    AudioSource _src;
    bool _ready;

    // diagnostics
    int   _framesPushed;
    float _lastRms;
    int   _micSampleRate;

  // Public diagnostics
    public int FramesPushed => _framesPushed;
    public float LastRms => _lastRms;
    public int SampleRate => _micSampleRate;

    // ring-read head over the mic clip
    int _readHead;
    float[] _pullBuf;
#endif

    [Header("Tuner params")]
    public float a4Hz = 440f;
    public Vector2 minMaxHz = new Vector2(70f, 1200f);

    [Header("Microphone")]
    public int requestedMicHz = 48000;

    [Header("Debug HUD")]
    public bool showHud = true;

  

    IEnumerator Start()
    {
#if UNITY_IOS && !UNITY_EDITOR
        // Ask for mic permission
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);

        // Start native first to avoid race
        TunerNative.StartTuner();
        TunerNative.SetA4Hz(a4Hz);
        TunerNative.SetMinMaxFreq(minMaxHz.x, minMaxHz.y);

        // Create a source (only to own the clip; we won't rely on OnAudioFilterRead)
        _src = GetComponent<AudioSource>();
        if (_src == null) _src = gameObject.AddComponent<AudioSource>();
        _src.loop = true;
        _src.playOnAwake = false;
        _src.volume = 0f;     // silence output
        _src.mute = false;    // but don't block processing
        _src.bypassEffects = true;
        _src.bypassListenerEffects = true;
        _src.bypassReverbZones = true;

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[MicFeederIOS] No microphone devices found.");
            yield break;
        }
        _device = Microphone.devices[0];

        var clip = Microphone.Start(_device, true, 1, requestedMicHz);

        // Wait until mic starts
        float t0 = Time.realtimeSinceStartup;
        while (Microphone.GetPosition(_device) <= 0 && Time.realtimeSinceStartup - t0 < 3f)
            yield return null;

        if (Microphone.GetPosition(_device) <= 0)
        {
            Debug.LogError("[MicFeederIOS] Microphone failed to start.");
            yield break;
        }

        _src.clip = clip;
        _src.Play();

        _micSampleRate = _src.clip.frequency;
        _pullBuf = new float[Mathf.Max(256, _micSampleRate / 20)]; // ~50 ms chunk
        _readHead = 0;
        _ready = true;

        Debug.Log($"[MicFeederIOS] Started. micSR={_micSampleRate}, outSR={AudioSettings.outputSampleRate}, device='{_device}'");
#else
        yield break;
#endif
    }

    void Update()
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (!_ready || _src.clip == null) return;

        int clipSamples = _src.clip.samples;
        int micPos = Microphone.GetPosition(_device);
        if (micPos < 0 || micPos >= clipSamples) return;

        // frames available since last read (circular)
        int available = (micPos - _readHead + clipSamples) % clipSamples;
        if (available <= 0) return;

        int toRead = Mathf.Min(available, _pullBuf.Length);

        // Handle wrap-around safely by reading in two parts if needed
        int first = Mathf.Min(toRead, clipSamples - _readHead);
        if (first > 0)
            _src.clip.GetData(_pullBuf, _readHead);
        if (toRead > first)
            _src.clip.GetData(_pullBuf, 0); // read from start for the wrapped part

        // Compute RMS on exactly 'toRead' samples
        double sum = 0;
        for (int i = 0; i < toRead; i++) { float s = _pullBuf[i]; sum += s * s; }
        _lastRms = Mathf.Sqrt((float)(sum / toRead));

        // Push mono block to native
        TunerNative.PushAudioBuffer(_pullBuf, toRead, 1, _micSampleRate);
        _framesPushed += toRead;

        _readHead = (_readHead + toRead) % clipSamples;
#endif
    }

    void OnGUI()
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (!showHud) return;
        GUI.Label(new Rect(10, 10, Screen.width - 20, 30),
            $"MicFeederIOS  frames={FramesPushed}  rms={LastRms:F4}  micSR={SampleRate}");
#endif
    }

    void OnDestroy()
    {
#if UNITY_IOS && !UNITY_EDITOR
        _ready = false;
        TunerNative.StopTuner();
        if (!string.IsNullOrEmpty(_device)) Microphone.End(_device);
#endif
    }
}
