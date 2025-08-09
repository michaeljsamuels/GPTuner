using System;
using System.Runtime.InteropServices;
using UnityEngine;

public static class TunerNative
{
#if UNITY_IOS && !UNITY_EDITOR
    const string LIB = "__Internal";
#elif UNITY_STANDALONE_OSX
    const string LIB = "JucePitchLib"; // JucePitchLib.dylib (put in Assets/Plugins/macOS/)
#elif UNITY_STANDALONE_WIN
    const string LIB = "JucePitchLib"; // JucePitchLib.dll (put in Assets/Plugins/x86_64/)
#else
    const string LIB = "JucePitchLib"; // adjust if needed
#endif

    [DllImport(LIB)] public static extern bool  StartTuner();
    [DllImport(LIB)] public static extern void  StopTuner();
    [DllImport(LIB)] public static extern void  SetA4Hz(float a4Hz);
    [DllImport(LIB)] public static extern void  SetMinMaxFreq(float minHz, float maxHz);
    [DllImport(LIB)] public static extern float GetLatestPitchHz();
    [DllImport(LIB)] public static extern float GetConfidence();
    [DllImport(LIB)] public static extern float GetCentsOffset();
    
    [DllImport("__Internal")] // iOS
    private static extern void PushAudioBuffer(float[] interleaved, int numFrames, int numChannels, int sampleRate);

// Wrap for all platforms:
    public static void Push(float[] interleaved, int numFrames, int numChannels, int sampleRate)
    {
#if UNITY_IOS && !UNITY_EDITOR
    PushAudioBuffer(interleaved, numFrames, numChannels, sampleRate);
#endif
    }
}