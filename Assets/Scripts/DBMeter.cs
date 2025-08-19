using UnityEngine;
using UnityEngine.UI;
#if TMP_PRESENT || TEXTMESHPRO
using TMPro;
#endif

public class DbMeter : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("If true, use AudioFeed.LevelDb (recommended). If false, compute from spectrum bands.")]
    public bool useAudioFeed = true;

    [Tooltip("When computing from bands, call SpectrumNative.ReadBands each frame.")]
    public bool pullFromSpectrum = false;  // ignored if useAudioFeed = true

    [Header("Meter Range (dBFS)")]
    public float minDb = -60f;
    public float maxDb = 0f;

    [Header("Smoothing / Peak Hold")]
    [Range(0f, 0.99f)] public float riseSmooth = 0.6f;  // higher = smoother
    [Range(0f, 0.99f)] public float fallSmooth = 0.85f; // higher = slower fall
    public bool   showPeakHold = true;
    public float  peakHoldFallDbPerSec = 10f;

    [Header("UI")]
    public Image bar;              // set Image.type = Filled, Fill Method = Horizontal

    public Image barMirror;
#if TMP_PRESENT || TEXTMESHPRO
    public TextMeshProUGUI label;  // optional text readout
#else
    public Text label;             // fallback
#endif

    // runtime
    float _smDb = -80f;
    float _peakDb = -80f;
    float[] _bands;

    void Start()
    {
        bar.type = Image.Type.Filled;
        bar.fillMethod = Image.FillMethod.Horizontal;

        barMirror.type = Image.Type.Filled;
        bar.fillMethod = barMirror.fillMethod = Image.FillMethod.Horizontal;

       barMirror.fillOrigin = (int)Image.OriginHorizontal.Right;
        // bar.fillOrigin = barMirror.fillOrigin = Image.OriginHorizontal;
        //   bar.fillOrigin = Image.O

    }
    void LateUpdate()
    {
        float db = useAudioFeed ? GetFromAudioFeed() : GetFromBands();

        // smooth: asymmetric rise/fall
        if (db > _smDb) _smDb = Mathf.Lerp(_smDb, db, 1f - riseSmooth);
        else            _smDb = Mathf.Lerp(_smDb, db, 1f - fallSmooth);

        // peak hold
        if (showPeakHold)
        {
            _peakDb = Mathf.Max(_peakDb, _smDb);
            _peakDb -= peakHoldFallDbPerSec * Time.deltaTime;
            _peakDb = Mathf.Max(_peakDb, minDb - 3f);
        }

        // UI
        float t = Mathf.InverseLerp(minDb, maxDb, _smDb);
        if (bar)
        {
            bar.fillAmount = barMirror.fillAmount=  Mathf.Clamp01(t);
            // optional: color shift green->red
            bar.color = barMirror.color = Color.Lerp(new Color(0.2f,0.8f,0.3f,1f), new Color(0.9f,0.25f,0.25f,1f), 1f - t);
        }

        if (label)
        {
            // show live + peak
            if (showPeakHold)
                label.text = $"{_smDb:0.#} dB  (pk {_peakDb:0.#})";
            else
                label.text = $"{_smDb:0.#} dB";
        }
    }

    float GetFromAudioFeed()
    {
        
        return Mathf.Clamp(AudioFeed.LevelDb, minDb - 20f, maxDb + 6f);
    }

    float GetFromBands()
    {
        if (!pullFromSpectrum)
            return _smDb; // no update

        int n = SpectrumNative.ReadBands(ref _bands);
        if (n <= 0 || _bands == null) return _smDb;

        // Assume bands[] are energy-like. Convert to RMS magnitude.
        double sum = 0.0;
        for (int i = 0; i < n; i++)
            sum += _bands[i];
        // Convert to RMS in [0..1] range and then to dBFS.
        double rms = System.Math.Sqrt(System.Math.Max(1e-12, sum));
        float db = 20f * Mathf.Log10((float)rms);

        return Mathf.Clamp(db, minDb - 40f, maxDb + 6f);
    }
}
