using UnityEngine;
using UnityEngine.UI;

// If you prefer TextMeshPro, swap Text -> TMP_Text and add "using TMPro;"
public class TunerNeedleUI : MonoBehaviour
{
    [Header("UI")]
    public Text pitchText;          // e.g. "Pitch: 440.00 Hz"
    public Text centsText;          // e.g. "+3.2 ¢"
    public RectTransform needle;    // an Image child you rotate or slide
    public Image needleFill;        // optional: color changes by accuracy (can be the same as needle)

    [Header("Needle Behavior")]
    [Tooltip("How many cents from center equals the max needle travel.")]
    public float needleRangeCents = 50f;

    [Tooltip("Lerp factor for smoothing (0 = instant, 1 = no movement). Try ~0.8.")]
    [Range(0f, 0.99f)] public float smoothing = 0.85f;

    [Tooltip("Confidence below which UI dims/fades.")]
    [Range(0f, 1f)] public float confidenceThreshold = 0.5f;

    [Header("Needle Mode")]
    public bool rotateNeedle = false;   // If true, rotates Z instead of sliding X
    public float maxRotationDeg = 25f;  // Max rotation when at needleRangeCents

    private float _smoothedCents = 0f;

    void Update()
    {
        float hz   = TunerNative.GetLatestPitchHz();
        float conf = TunerNative.GetConfidence();
        float cents = TunerNative.GetCentsOffset();

        
        print("UI MEASURE: " + hz);
        // Update texts
        if (pitchText) pitchText.text = (hz > 0f && conf >= 0.01f) ? $"{hz:F2} Hz" : "— Hz";
        if (centsText) centsText.text = $"{cents:+0.0;-0.0;0} ¢";

        // Smooth the needle
        _smoothedCents = Mathf.Lerp(_smoothedCents, cents, 1f - smoothing);

        // Clamp to visual range
        float clamped = Mathf.Clamp(_smoothedCents, -needleRangeCents, needleRangeCents);
        float norm = clamped / needleRangeCents; // -1 .. +1

        // Drive needle either by rotation or horizontal translation
        if (needle)
        {
            if (rotateNeedle)
            {
                float z = norm * maxRotationDeg;
                needle.localRotation = Quaternion.Euler(0f, 0f, -z); // -z so positive cents tilts right
            }
            else
            {
                // Slide on X within its parent width; assumes the parent anchors are centered.
                // Tune this number to taste (pixels at full deflection):
                float maxPixels = 140f;
                needle.anchoredPosition = new Vector2(norm * maxPixels, needle.anchoredPosition.y);
            }
        }

        // Optional: color feedback — greener when near 0¢ and confident
        if (needleFill)
        {
            float inTune = Mathf.InverseLerp(needleRangeCents, 0f, Mathf.Abs(_smoothedCents)); // 0 far, 1 centered
            float alpha  = Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(0f, confidenceThreshold, conf));
            Color c = Color.Lerp(Color.red, Color.green, inTune);
            c.a = alpha;
            needleFill.color = c;
        }
    }
}
