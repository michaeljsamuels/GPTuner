using System;
using System.Runtime.InteropServices;
using UnityEngine;

public static class TunerNative
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] public static extern bool  StartTuner();
    [DllImport("__Internal")] public static extern void  StopTuner();
    [DllImport("__Internal")] public static extern void  SetA4Hz(float a4Hz);
    [DllImport("__Internal")] public static extern void  SetMinMaxFreq(float minHz, float maxHz);
    [DllImport("__Internal")] public static extern float GetLatestPitchHz();
    [DllImport("__Internal")] public static extern float GetConfidence();
    [DllImport("__Internal")] public static extern float GetCentsOffset();
    [DllImport("__Internal")] public static extern void  PushAudioBuffer(float[] interleaved, int numFrames, int numChannels, int sampleRate);

#else
    // Editor (macOS) / desktop: dynamic library name
    [DllImport("JucePitchLib")] public static extern bool  StartTuner();
    [DllImport("JucePitchLib")] public static extern void  StopTuner();
    [DllImport("JucePitchLib")] public static extern void  SetA4Hz(float a4Hz);
    [DllImport("JucePitchLib")] public static extern void  SetMinMaxFreq(float minHz, float maxHz);
    [DllImport("JucePitchLib")] public static extern float GetLatestPitchHz();
    [DllImport("JucePitchLib")] public static extern float GetConfidence();
    [DllImport("JucePitchLib")] public static extern float GetCentsOffset();
    [DllImport("JucePitchLib")] public static extern void  PushAudioBuffer(float[] interleaved, int numFrames, int numChannels, int sampleRate);
#endif
   // private static extern void PushAudioBuffer(float[] interleaved, int numFrames, int numChannels, int sampleRate);

// Wrap for all platforms:
    public static void Push(float[] interleaved, int numFrames, int numChannels, int sampleRate)
    {
#if UNITY_IOS && !UNITY_EDITOR
    PushAudioBuffer(interleaved, numFrames, numChannels, sampleRate);
#endif
    }
}