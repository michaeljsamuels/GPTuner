using UnityEngine;
using UnityEngine.UI;

public class SpectrumBarsNative : MonoBehaviour
{
    [Header("UI")]
    public RectTransform container;
    public Image barPrefab;
    public int   bands = 64;

    [Header("Layout")]
    public bool  verticalRight = true;
    public float paddingX = 8f, paddingY = 8f, spacing = 2f;
    [Range(0.1f, 1f)] public float horizontalFill = 0.9f;
    [Range(0.1f, 1f)] public float verticalFill   = 0.9f;

    [Header("Style")]
    [Tooltip("Fallback color if gradientShader is missing.")]
    public Color barColor = new Color(1,1,1,0.28f);
    public float gain = 1.0f;                  // amplitude → length scale
    [Range(0f,0.99f)] public float smooth = 0.7f;
    public float fallPerSec = 2.0f;

    [Header("Gradient Look")]
    public Shader gradientShader;              // assign: UI/GradientBar (Horizontal)
    [Range(0f,1f)] public float hueStart = 0.0f;   // low freq hue (0=red)
    [Range(0f,1f)] public float hueEnd   = 0.83f;  // high freq hue (~purple)
    [Range(0f,1f)] public float satLeft  = 0.9f;
    [Range(0f,1f)] public float valLeft  = 0.7f;
    [Range(0f,1f)] public float satRight = 0.9f;
    [Range(0f,1f)] public float valRight = 1.0f;
    public float alphaMin = 0.08f;            // min bar alpha at very low amp
    public float alphaMax = 0.95f;            // max bar alpha at high amp
    public float alphaGain = 1.2f;            // additional boost for alpha

    [Header("Center Soften (optional)")]
    public bool softenUnderNote = true;
    public float centerSoftenRadiusPx = 120f;   // width of the soft notch around center
    public float centerSoftenFalloffPx = 80f;   // feather
    public float centerSoftenAmount = 0.5f;     // 0.5 = 50% dim

    
    
    [Header("Spectrum Config (Native)")]
    public SpectrumNative.Scale scale = SpectrumNative.Scale.Log;
    public float minHz = 50f, maxHz = 4000f, a4Hz = 440f;

    RectTransform _root;
    Image[] _bars;
    Material[] _barMats;          // per-bar material (for gradient + alpha)
    float[] _display, _bands;

    float _barH, _barW;

    void OnEnable()
    {
        if (!container) container = GetComponent<RectTransform>();
        EnsureRoot();
        BuildBars();
        SpectrumNative.Configure(bands, minHz, maxHz, scale, a4Hz);
    }

    void OnValidate()
    {
        bands = Mathf.Clamp(bands, 8, 256);
        if (isActiveAndEnabled)
        {
            BuildBars();
            SpectrumNative.Configure(bands, minHz, maxHz, scale, a4Hz);
        }
    }

    void EnsureRoot()
    {
        if (_root) return;
        var t = container ? container.Find("__SpectrumBarsNative") : null;
        if (t) _root = (RectTransform)t;
        else
        {
            var go = new GameObject("__SpectrumBarsNative", typeof(RectTransform));
            go.transform.SetParent(container, false);
            _root = go.GetComponent<RectTransform>();
            _root.anchorMin = new Vector2(0,0);
            _root.anchorMax = new Vector2(1,1);
            _root.pivot     = new Vector2(0.5f,0.5f);
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;
        }
    }

    void BuildBars()
    {
        EnsureRoot();
        bool rebuild = (_bars == null || _bars.Length != bands);
        if (rebuild)
        {
            _bars    = new Image[bands];
            _display = new float[bands];
            _barMats = new Material[bands];
        }

        int child = _root.childCount;
        for (int i = 0; i < bands; i++)
        {
            Image img; RectTransform rt;
            if (i < child)
            {
                var go = _root.GetChild(i).gameObject;
                go.SetActive(true);
                img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
                rt  = img.rectTransform;
            }
            else
            {
                img = barPrefab ? Instantiate(barPrefab, _root) : CreateBar(_root);
                rt  = img.rectTransform;
            }
            _bars[i] = img;

            // Assign gradient material instance per bar (or fallback color)
            if (gradientShader)
            {
                if (_barMats[i] == null || _barMats[i].shader != gradientShader)
                    _barMats[i] = new Material(gradientShader);
                img.material = _barMats[i];

                // Per-bar hue from low→high
                float t = (bands > 1) ? (float)i / (bands - 1) : 0f;
                float h = Mathf.Lerp(hueStart, hueEnd, t);

                Color left  = Color.HSVToRGB(h, satLeft,  valLeft);
                Color right = Color.HSVToRGB(h, satRight, valRight);

                _barMats[i].SetColor("_ColorLeft",  left);
                _barMats[i].SetColor("_ColorRight", right);
                _barMats[i].SetFloat("_Alpha", alphaMin); // init faint
            }
            else
            {
                img.material = null;
                img.color = barColor;
            }
        }
        for (int i = bands; i < child; i++) _root.GetChild(i).gameObject.SetActive(false);

        Reflow();
    }

    Image CreateBar(RectTransform parent)
    {
        var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.raycastTarget = false;
        // Make sure Image UVs span 0..1 across width for gradient
        var rt = img.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0,0);
        return img;
    }

    void Reflow()
    {
        float W = container.rect.width;
        float H = container.rect.height;

        if (verticalRight)
        {
            float usableH = Mathf.Max(1f, H - 2f * paddingY);
            _barH = Mathf.Max(1f, (usableH - (bands - 1) * spacing) / Mathf.Max(1, bands));

            for (int i = 0; i < bands; i++)
            {
                var rt = _bars[i].rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(1,0);
                rt.pivot = new Vector2(1,0);
                float y = paddingY + i * (_barH + spacing);
                rt.anchoredPosition = new Vector2(-paddingX, y);
                rt.sizeDelta = new Vector2(1, _barH); // width set in LateUpdate
            }
        }
        else
        {
            float usableW = Mathf.Max(1f, W - 2f * paddingX);
            _barW = Mathf.Max(1f, (usableW - (bands - 1) * spacing) / Mathf.Max(1, bands));

            for (int i = 0; i < bands; i++)
            {
                var rt = _bars[i].rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0,0);
                float x = paddingX + i * (_barW + spacing);
                rt.anchoredPosition = new Vector2(x, 0);
                rt.sizeDelta = new Vector2(_barW, 1); // height set in LateUpdate
            }
        }
    }

    void LateUpdate()
    {
        if (container) Reflow(); // handle rotation/resize cheaply

        // Pull fresh bands from native
        int n = SpectrumNative.ReadBands(ref _bands);
        if (n <= 0 || _bars == null) return;
        if (_bars.Length != n) { bands = n; BuildBars(); }

        // Smooth & draw
        float dt = Time.deltaTime;
        float rise = 1f - smooth;
        float fall = fallPerSec * dt;

        float maxW = Mathf.Max(1f, container.rect.width  * horizontalFill);
        float maxH = Mathf.Max(1f, container.rect.height * verticalFill);

        for (int i = 0; i < n; i++)
        {
            float target = Mathf.Sqrt(Mathf.Max(0,_bands[i])) * gain;
            float cur = _display[i];
            cur = (target > cur) ? Mathf.Lerp(cur, target, rise) : Mathf.Max(0, cur - fall);
            _display[i] = cur;

            var rt = _bars[i].rectTransform;
            if (verticalRight)
            {
                float w = Mathf.Clamp(cur * maxW, 1f, maxW);
                var sd = rt.sizeDelta; sd.x = w; sd.y = _barH; rt.sizeDelta = sd;
            }
            else
            {
                float h = Mathf.Clamp(cur * maxH, 1f, maxH);
                var sd = rt.sizeDelta; sd.y = h; sd.x = _barW; rt.sizeDelta = sd;
            }

            // Update per-bar alpha by amplitude
            if (gradientShader && _barMats != null && _barMats[i] != null)
            {
                float a = Mathf.Clamp01(alphaMin + (alphaMax - alphaMin) * Mathf.Clamp01(cur * alphaGain));
                _barMats[i].SetFloat("_Alpha", a);
            }
            else
            {
                var c = _bars[i].color;
                c.a = Mathf.Clamp01(alphaMin + (alphaMax - alphaMin) * Mathf.Clamp01(cur * alphaGain));
               
                if (softenUnderNote)
                {
                    float centerX = container.rect.width * 0.5f;
                    // For verticalRight layout bars extend from the right edge; we just dim by how close the bar's CURRENT end is to the screen center
                    float barEndX = verticalRight
                        ? container.rect.width - paddingX - _bars[i].rectTransform.sizeDelta.x
                        : paddingX + _bars[i].rectTransform.sizeDelta.x;

                    float dx = Mathf.Abs(barEndX - centerX);
                    float edge0 = Mathf.Max(0f, centerSoftenRadiusPx - centerSoftenFalloffPx);
                    float edge1 = centerSoftenRadiusPx;
                    float t = Mathf.Clamp01(1f - Mathf.SmoothStep(edge0, edge1, dx));
                    float notch = 1f - t * centerSoftenAmount;   // 1 at far, (1-amount) at center
                    c.a *= notch;
                }

                
                _bars[i].color = c;
            }
            
            
        }
    }
}
