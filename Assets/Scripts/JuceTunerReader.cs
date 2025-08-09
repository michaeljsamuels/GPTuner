using UnityEngine;
using UnityEngine.UI;

public class JuceTunerReader : MonoBehaviour
{
    public Text uiText;   // or use TMP_Text if you prefer
    public float confThreshold = 0.5f;

    void Start()
    {
        // iOS/macOS: ensure mic permission string is set in Info.plist / Player Settings
        if (!TunerNative.StartTuner())
            Debug.LogError("Failed to start JUCE tuner (no input device?)");

        TunerNative.SetA4Hz(440f);
        TunerNative.SetMinMaxFreq(60f, 1200f);
    }

    void OnDestroy()
    {
        TunerNative.StopTuner();
    }

    void Update()
    {
        float hz = TunerNative.GetLatestPitchHz();
        float conf = TunerNative.GetConfidence();
        float cents = TunerNative.GetCentsOffset();

       
            if (conf >= confThreshold && hz > 0f)
                Debug.Log( $"Pitch: {hz:F2} Hz  (Δ {cents:+0.0;-0.0;0}¢)  conf: {conf:F2}");
            else
               Debug.Log( "Listening…");
        

        // Your visualization logic can use hz/conf/cents here
        // e.g. animate needle, color bands, particle effects, etc.
    }
}
