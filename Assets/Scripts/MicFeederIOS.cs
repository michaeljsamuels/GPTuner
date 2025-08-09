using UnityEngine;

public class MicFeederIOS : MonoBehaviour
{
    AudioSource _src;
    string _device;
    int _clipFreq;

    void Start()
    {
#if UNITY_IOS && !UNITY_EDITOR
        _src = gameObject.AddComponent<AudioSource>();
        _device = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;
        _clipFreq = 48000; // pick common rate; iOS will adapt, and we forward actual clip freq below

        var clip = Microphone.Start(_device, true, 1, _clipFreq);
        while (Microphone.GetPosition(_device) <= 0) {} // wait a tick
        _src.clip = clip;
        _src.loop = true;
        _src.mute = true;      // we don't want to hear raw mic
        _src.Play();

        TunerNative.StartTuner(); // sets buffers on the native side
        TunerNative.SetA4Hz(440f);
        TunerNative.SetMinMaxFreq(60f, 1200f);
#endif
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
#if UNITY_IOS && !UNITY_EDITOR
        // data is interleaved frames for this block
        int frames = data.Length / channels;
        int sr = AudioSettings.outputSampleRate; // usually matches mic clip freq on iOS
        TunerNative.Push(data, frames, channels, sr);
#endif
    }

    void OnDestroy()
    {
#if UNITY_IOS && !UNITY_EDITOR
        TunerNative.StopTuner();
        if (_device != null) Microphone.End(_device);
#endif
    }
}