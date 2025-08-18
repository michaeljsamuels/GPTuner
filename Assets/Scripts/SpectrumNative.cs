using System.Runtime.InteropServices;
using UnityEngine;

public static class SpectrumNative
{
#if UNITY_IOS && !UNITY_EDITOR
    const string LIB = "__Internal";
#else
    const string LIB = "JucePitchLib"; // name you use on macOS
#endif

    public enum Scale { Linear = 0, Log = 1, Semitone = 2 }

    [DllImport(LIB)] static extern void SetSpectrumConfig(int bands, float minHz, float maxHz, int scale, float a4Hz);
    [DllImport(LIB)] static extern int  GetSpectrumBands([Out] float[] outBands, int maxOut);

    public static void Configure(int bands, float minHz, float maxHz, Scale scale, float a4Hz = 440f)
        => SetSpectrumConfig(bands, minHz, maxHz, (int)scale, a4Hz);

    static float[] _buf;
    public static int ReadBands(ref float[] dst)
    {
        if (_buf == null || _buf.Length < 256) _buf = new float[256];
        int n = GetSpectrumBands(_buf, _buf.Length);
        if (n <= 0) return 0;
        if (dst == null || dst.Length != n) dst = new float[n];
        System.Array.Copy(_buf, dst, n);
        return n;
    }
}