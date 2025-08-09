using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Collections.Concurrent;

public class GuitarTunerThreaded : MonoBehaviour
{
    public int sampleRate = 44100;
    public int fftSize = 4096;
    public bool showDebugInfo = false;
    
    [Header("Performance Settings")]
    [Range(10, 60)]
    public int analysisUpdateRate = 30;
    
    [Header("Pitch Detection")]
    public float minFrequency = 80f;
    public float maxFrequency = 2000f;
    public int harmonicCount = 5;
    public float noiseThreshold = 0.001f;
    
    [Header("Tuning Display")]
    public bool showTuningMeter = true;
    public float tuningTolerance = 5f; // cents
    public Color inTuneColor = Color.green;
    public Color slightlyOffColor = Color.yellow;
    public Color veryOffColor = Color.red;
    
    [Header("Visualizer Settings")]
    public int numBars = 64;
    public float barWidth = 10f;
    public float barSpacing = 2f;
    public float maxBarHeight = 200f;
    public float smoothing = 0.8f;
    public Color barColor = Color.green;
    public Color dominantFreqColor = Color.red;
    
    // Audio components
    private AudioClip micClip;
    private string micDevice;
    private AudioSource audioSource;
    
    // Threading for pitch detection
    private Thread pitchDetectionThread;
    private volatile bool isProcessing = false;
    private ConcurrentQueue<float[]> spectrumQueue = new ConcurrentQueue<float[]>();
    private readonly object resultLock = new object();
    
    // Results from background thread
    private float currentFrequency = 0f;
    private string currentNote = "--";
    private float currentCentsDeviation = 0f;
    
    // Main thread data
    private float[] currentSpectrum;
    private float[] barMagnitudes;
    private float lastAnalysisTime = 0f;
    
    // UI Components
    private Canvas canvas;
    private List<Image> bars = new List<Image>();
    private Text frequencyText;
    private Text noteText;
    private Text centsText;
    private Text performanceText;
    private RectTransform visualizerPanel;
    private Image tuningMeter;
    private Image tuningNeedle;
    private float[] barHeights;
    
    // Performance monitoring
    private int frameCount = 0;
    private float lastFrameTime = 0f;
    
    // Note detection
    private string[] noteNames = {"C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"};
    
    void Start()
    {
        InitializeUI();
        StartCoroutine(InitializeMicrophone());
    }
    
    void InitializeUI()
    {
        // Create Canvas
        GameObject canvasObj = new GameObject("VisualizerCanvas");
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        
        // Create tuning meter if enabled
        if (showTuningMeter)
        {
            CreateTuningMeter();
        }
        
        // Create main panel for visualizer
        GameObject panelObj = new GameObject("VisualizerPanel");
        panelObj.transform.SetParent(canvas.transform);
        visualizerPanel = panelObj.AddComponent<RectTransform>();
        visualizerPanel.anchorMin = new Vector2(0.1f, 0.1f);
        visualizerPanel.anchorMax = new Vector2(0.9f, 0.6f);
        visualizerPanel.offsetMin = Vector2.zero;
        visualizerPanel.offsetMax = Vector2.zero;
        
        // Create frequency text
        GameObject textObj = new GameObject("FrequencyText");
        textObj.transform.SetParent(canvas.transform);
        frequencyText = textObj.AddComponent<Text>();
        frequencyText.text = "Frequency: 0.00 Hz";
        frequencyText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        frequencyText.fontSize = 20;
        frequencyText.color = Color.white;
        frequencyText.alignment = TextAnchor.MiddleCenter;
        
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.1f, 0.65f);
        textRect.anchorMax = new Vector2(0.9f, 0.72f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        
        // Create note text
        GameObject noteObj = new GameObject("NoteText");
        noteObj.transform.SetParent(canvas.transform);
        noteText = noteObj.AddComponent<Text>();
        noteText.text = "Note: --";
        noteText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        noteText.fontSize = 28;
        noteText.color = Color.yellow;
        noteText.alignment = TextAnchor.MiddleCenter;
        noteText.fontStyle = FontStyle.Bold;
        
        RectTransform noteRect = noteObj.GetComponent<RectTransform>();
        noteRect.anchorMin = new Vector2(0.1f, 0.72f);
        noteRect.anchorMax = new Vector2(0.9f, 0.8f);
        noteRect.offsetMin = Vector2.zero;
        noteRect.offsetMax = Vector2.zero;
        
        // Create performance text
        GameObject perfObj = new GameObject("PerformanceText");
        perfObj.transform.SetParent(canvas.transform);
        performanceText = perfObj.AddComponent<Text>();
        performanceText.text = "FPS: 0";
        performanceText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        performanceText.fontSize = 14;
        performanceText.color = Color.gray;
        performanceText.alignment = TextAnchor.MiddleRight;
        
        RectTransform perfRect = perfObj.GetComponent<RectTransform>();
        perfRect.anchorMin = new Vector2(0.75f, 0.95f);
        perfRect.anchorMax = new Vector2(0.98f, 1.0f);
        perfRect.offsetMin = Vector2.zero;
        perfRect.offsetMax = Vector2.zero;
        
        // Create frequency bars
        CreateFrequencyBars();
        
        barHeights = new float[numBars];
        barMagnitudes = new float[numBars];
    }
    
    void CreateTuningMeter()
    {
        // Cents deviation text
        GameObject centsObj = new GameObject("CentsText");
        centsObj.transform.SetParent(canvas.transform);
        centsText = centsObj.AddComponent<Text>();
        centsText.text = "0 cents";
        centsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        centsText.fontSize = 22;
        centsText.color = inTuneColor;
        centsText.alignment = TextAnchor.MiddleCenter;
        centsText.fontStyle = FontStyle.Bold;
        
        RectTransform centsRect = centsObj.GetComponent<RectTransform>();
        centsRect.anchorMin = new Vector2(0.3f, 0.85f);
        centsRect.anchorMax = new Vector2(0.7f, 0.92f);
        centsRect.offsetMin = Vector2.zero;
        centsRect.offsetMax = Vector2.zero;
        
        // Tuning meter background
        GameObject meterBgObj = new GameObject("TuningMeterBG");
        meterBgObj.transform.SetParent(canvas.transform);
        tuningMeter = meterBgObj.AddComponent<Image>();
        tuningMeter.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        
        RectTransform meterBgRect = meterBgObj.GetComponent<RectTransform>();
        meterBgRect.anchorMin = new Vector2(0.2f, 0.92f);
        meterBgRect.anchorMax = new Vector2(0.8f, 0.97f);
        meterBgRect.offsetMin = Vector2.zero;
        meterBgRect.offsetMax = Vector2.zero;
        
        // Center line (perfect tuning indicator)
        GameObject centerObj = new GameObject("CenterLine");
        centerObj.transform.SetParent(meterBgObj.transform);
        Image centerLine = centerObj.AddComponent<Image>();
        centerLine.color = inTuneColor;
        
        RectTransform centerRect = centerObj.GetComponent<RectTransform>();
        centerRect.anchorMin = new Vector2(0.498f, 0.1f);
        centerRect.anchorMax = new Vector2(0.502f, 0.9f);
        centerRect.offsetMin = Vector2.zero;
        centerRect.offsetMax = Vector2.zero;
        
        // Tuning needle
        GameObject needleObj = new GameObject("TuningNeedle");
        needleObj.transform.SetParent(meterBgObj.transform);
        tuningNeedle = needleObj.AddComponent<Image>();
        tuningNeedle.color = Color.white;
        
        RectTransform needleRect = needleObj.GetComponent<RectTransform>();
        needleRect.anchorMin = new Vector2(0.495f, 0.2f);
        needleRect.anchorMax = new Vector2(0.505f, 0.8f);
        needleRect.offsetMin = Vector2.zero;
        needleRect.offsetMax = Vector2.zero;
        
        // Flat/Sharp labels
        GameObject flatLabel = new GameObject("FlatLabel");
        flatLabel.transform.SetParent(meterBgObj.transform);
        Text flatText = flatLabel.AddComponent<Text>();
        flatText.text = "♭";
        flatText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        flatText.fontSize = 16;
        flatText.color = veryOffColor;
        flatText.alignment = TextAnchor.MiddleCenter;
        flatText.fontStyle = FontStyle.Bold;
        
        RectTransform flatRect = flatLabel.GetComponent<RectTransform>();
        flatRect.anchorMin = new Vector2(0.05f, 0f);
        flatRect.anchorMax = new Vector2(0.15f, 1f);
        flatRect.offsetMin = Vector2.zero;
        flatRect.offsetMax = Vector2.zero;
        
        GameObject sharpLabel = new GameObject("SharpLabel");
        sharpLabel.transform.SetParent(meterBgObj.transform);
        Text sharpText = sharpLabel.AddComponent<Text>();
        sharpText.text = "♯";
        sharpText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        sharpText.fontSize = 16;
        sharpText.color = veryOffColor;
        sharpText.alignment = TextAnchor.MiddleCenter;
        sharpText.fontStyle = FontStyle.Bold;
        
        RectTransform sharpRect = sharpLabel.GetComponent<RectTransform>();
        sharpRect.anchorMin = new Vector2(0.85f, 0f);
        sharpRect.anchorMax = new Vector2(0.95f, 1f);
        sharpRect.offsetMin = Vector2.zero;
        sharpRect.offsetMax = Vector2.zero;
    }
    
    void CreateFrequencyBars()
    {
        float totalWidth = numBars * (barWidth + barSpacing) - barSpacing;
        float startX = -totalWidth / 2f;
        
        for (int i = 0; i < numBars; i++)
        {
            GameObject barObj = new GameObject($"Bar_{i}");
            barObj.transform.SetParent(visualizerPanel);
            
            Image barImage = barObj.AddComponent<Image>();
            barImage.color = barColor;
            
            RectTransform barRect = barObj.GetComponent<RectTransform>();
            
            float xPos = startX + i * (barWidth + barSpacing);
            barRect.anchoredPosition = new Vector2(xPos, 0);
            barRect.sizeDelta = new Vector2(barWidth, 0);
            barRect.anchorMin = new Vector2(0.5f, 0);
            barRect.anchorMax = new Vector2(0.5f, 0);
            barRect.pivot = new Vector2(0.5f, 0);
            
            bars.Add(barImage);
        }
    }
    
    IEnumerator InitializeMicrophone()
    {
        // Ensure FFT size is power of 2 for Cooley-Tukey algorithm
        if (!IsPowerOfTwo(fftSize))
        {
            Debug.LogWarning($"FFT size {fftSize} is not a power of 2. Adjusting to nearest power of 2.");
            fftSize = NextPowerOfTwo(fftSize);
        }
        
        // Initialize spectrum array with correct size
        currentSpectrum = new float[fftSize / 2];
        
        if (Microphone.devices.Length > 0)
        {
            micDevice = Microphone.devices[0];
            Debug.Log("Using microphone: " + micDevice);
            
            // Create AudioSource for microphone input
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.loop = true;
            audioSource.mute = true; // Mute to avoid feedback
            
            // Start microphone with longer buffer
            micClip = Microphone.Start(micDevice, true, 10, sampleRate);
            
            // Wait for microphone to actually start recording
            while (Microphone.GetPosition(micDevice) <= 0)
            {
                yield return null;
            }
            Debug.Log("Microphone position: " + Microphone.GetPosition(micDevice));
            
            // Wait a bit more for buffer to fill
            yield return new WaitForSeconds(0.5f);
            
            // Assign clip and play
            audioSource.clip = micClip;
            audioSource.Play();
            
            Debug.Log("AudioSource playing: " + audioSource.isPlaying);
            Debug.Log("AudioSource time: " + audioSource.time);
            Debug.Log($"Microphone initialized. FFT Size: {fftSize}, Update Rate: {analysisUpdateRate} Hz");
            
            // Start the pitch detection thread after microphone is ready
            StartPitchDetectionThread();
        }
        else
        {
            Debug.LogError("No microphone devices found.");
        }
    }
    
    bool IsPowerOfTwo(int x)
    {
        return (x & (x - 1)) == 0 && x != 0;
    }
    
    int NextPowerOfTwo(int x)
    {
        int power = 1;
        while (power < x)
            power *= 2;
        return power;
    }
    
    void StartPitchDetectionThread()
    {
        isProcessing = true;
        pitchDetectionThread = new Thread(PitchDetectionLoop);
        pitchDetectionThread.Start();
    }
    
    void PitchDetectionLoop()
    {
        float[] hpsSpectrum = new float[fftSize / 2];
        
        while (isProcessing)
        {
            try
            {
                // Process any queued spectrum data
                if (spectrumQueue.TryDequeue(out float[] spectrumData))
                {
                    float frequency = CalculateFundamentalFrequency(spectrumData, hpsSpectrum);
                    string note = GetNoteFromFrequency(frequency);
                    float centsOff = CalculateCentsDeviation(frequency);
                    
                    // Thread-safe update of results
                    lock (resultLock)
                    {
                        currentFrequency = frequency;
                        currentNote = note;
                        currentCentsDeviation = centsOff;
                    }
                }
                else
                {
                    // No data to process, sleep briefly
                    Thread.Sleep(10);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error in pitch detection thread: " + e.Message);
                Thread.Sleep(100);
            }
        }
    }
    
    float CalculateFundamentalFrequency(float[] spectrumData, float[] hpsSpectrum)
    {
        // Check signal strength
        float maxMagnitude = 0f;
        for (int i = 0; i < spectrumData.Length; i++)
        {
            if (spectrumData[i] > maxMagnitude)
                maxMagnitude = spectrumData[i];
        }
        
        if (maxMagnitude < noiseThreshold)
            return 0f;
        
        // Initialize HPS spectrum
        for (int i = 0; i < hpsSpectrum.Length; i++)
        {
            hpsSpectrum[i] = spectrumData[i];
        }
        
        // Apply HPS - this is the expensive part that benefits from threading
        for (int harmonic = 2; harmonic <= harmonicCount; harmonic++)
        {
            for (int i = 0; i < hpsSpectrum.Length / harmonic; i++)
            {
                hpsSpectrum[i] *= spectrumData[i * harmonic];
            }
        }
        
        // Find peak in musical range
        float freqResolution = (float)sampleRate / fftSize;
        int minBin = Mathf.RoundToInt(minFrequency / freqResolution);
        int maxBin = Mathf.RoundToInt(maxFrequency / freqResolution);
        maxBin = Mathf.Min(maxBin, hpsSpectrum.Length - 1);
        
        int bestBin = 0;
        float bestValue = 0f;
        
        for (int i = minBin; i <= maxBin; i++)
        {
            if (hpsSpectrum[i] > bestValue)
            {
                bestValue = hpsSpectrum[i];
                bestBin = i;
            }
        }
        
        return bestBin * freqResolution;
    }
    
    void Update()
    {
        UpdatePerformanceStats();
        GetSpectrumData();
        UpdateUI();
    }
    
    void GetSpectrumData()
    {
        if (audioSource == null || !audioSource.isPlaying) return;
        
        // Check if it's time for a new analysis
        float timeSinceLastAnalysis = Time.time - lastAnalysisTime;
        float analysisInterval = 1f / analysisUpdateRate;
        
        if (timeSinceLastAnalysis >= analysisInterval)
        {
            // Read microphone data directly and perform FFT (like the working version)
            int micPosition = Microphone.GetPosition(micDevice);
            if (micPosition < fftSize) return;
            
            // Calculate read position
            int readPosition = micPosition - fftSize;
            if (readPosition < 0)
                readPosition = micClip.samples + readPosition;
            
            // Read raw audio data
            float[] audioData = new float[fftSize];
            micClip.GetData(audioData, readPosition);
            
            // Apply windowing
            ApplyWindow(audioData);
            
            // Perform FFT to get spectrum
            PerformFFT(audioData, currentSpectrum);
            
            // Debug the data
            if (showDebugInfo)
            {
                float rms = CalculateRMS(audioData);
                float maxSpectrum = 0f;
                for (int i = 0; i < currentSpectrum.Length; i++)
                {
                    if (currentSpectrum[i] > maxSpectrum)
                        maxSpectrum = currentSpectrum[i];
                }
                Debug.Log($"Audio RMS: {rms:F4}, Max Spectrum: {maxSpectrum:F6}, Mic Pos: {micPosition}");
            }
            
            // Send copy to background thread for processing (if queue isn't full)
            if (spectrumQueue.Count < 2)
            {
                float[] spectrumCopy = new float[currentSpectrum.Length];
                Array.Copy(currentSpectrum, spectrumCopy, currentSpectrum.Length);
                spectrumQueue.Enqueue(spectrumCopy);
            }
            
            lastAnalysisTime = Time.time;
        }
        
        // Calculate bar magnitudes on main thread
        CalculateBarMagnitudes(currentSpectrum, barMagnitudes);
    }
    
    void ApplyWindow(float[] data)
    {
        // Apply Hanning window to reduce spectral leakage
        for (int i = 0; i < data.Length; i++)
        {
            float window = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (data.Length - 1)));
            data[i] *= window;
        }
    }
    
    void PerformFFT(float[] audioData, float[] spectrum)
    {
        // Use fast Cooley-Tukey FFT algorithm O(N log N) instead of naive O(N²)
        int N = audioData.Length;
        
        // Convert to complex numbers for FFT
        Complex[] complexData = new Complex[N];
        for (int i = 0; i < N; i++)
        {
            complexData[i] = new Complex(audioData[i], 0);
        }
        
        // Perform FFT
        CooleyTukeyFFT(complexData);
        
        // Convert back to magnitude spectrum (only first half due to symmetry)
        for (int i = 0; i < spectrum.Length; i++)
        {
            spectrum[i] = (float)complexData[i].Magnitude / N;
        }
    }
    
    void CooleyTukeyFFT(Complex[] data)
    {
        int N = data.Length;
        if (N <= 1) return;
        
        // Divide
        Complex[] even = new Complex[N / 2];
        Complex[] odd = new Complex[N / 2];
        
        for (int i = 0; i < N / 2; i++)
        {
            even[i] = data[i * 2];
            odd[i] = data[i * 2 + 1];
        }
        
        // Conquer
        CooleyTukeyFFT(even);
        CooleyTukeyFFT(odd);
        
        // Combine
        for (int i = 0; i < N / 2; i++)
        {
            double angle = -2.0 * Math.PI * i / N;
            Complex t = new Complex(Math.Cos(angle), Math.Sin(angle)) * odd[i];
            data[i] = even[i] + t;
            data[i + N / 2] = even[i] - t;
        }
    }
    
    // Simple Complex number struct for FFT
    public struct Complex
    {
        public double Real;
        public double Imaginary;
        
        public Complex(double real, double imaginary)
        {
            Real = real;
            Imaginary = imaginary;
        }
        
        public double Magnitude => Math.Sqrt(Real * Real + Imaginary * Imaginary);
        
        public static Complex operator +(Complex a, Complex b)
        {
            return new Complex(a.Real + b.Real, a.Imaginary + b.Imaginary);
        }
        
        public static Complex operator -(Complex a, Complex b)
        {
            return new Complex(a.Real - b.Real, a.Imaginary - b.Imaginary);
        }
        
        public static Complex operator *(Complex a, Complex b)
        {
            return new Complex(
                a.Real * b.Real - a.Imaginary * b.Imaginary,
                a.Real * b.Imaginary + a.Imaginary * b.Real
            );
        }
    }
    
    float CalculateRMS(float[] data)
    {
        float sum = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i] * data[i];
        }
        return Mathf.Sqrt(sum / data.Length);
    }
    
    void CalculateBarMagnitudes(float[] spectrumData, float[] barData)
    {
        int spectrumBinPerBar = spectrumData.Length / numBars;
        
        for (int i = 0; i < numBars; i++)
        {
            float avgMagnitude = 0f;
            int startBin = i * spectrumBinPerBar;
            int endBin = Mathf.Min(startBin + spectrumBinPerBar, spectrumData.Length);
            
            for (int j = startBin; j < endBin; j++)
            {
                avgMagnitude += spectrumData[j];
            }
            avgMagnitude /= (endBin - startBin);
            barData[i] = avgMagnitude;
        }
    }
    
    void UpdatePerformanceStats()
    {
        frameCount++;
        if (Time.time - lastFrameTime >= 1f)
        {
            float fps = frameCount / (Time.time - lastFrameTime);
            int queueSize = spectrumQueue.Count;
            if (performanceText != null)
            {
                performanceText.text = $"FPS: {fps:F0} | FFT: {fftSize} | Q: {queueSize}";
            }
            frameCount = 0;
            lastFrameTime = Time.time;
        }
    }
    
    void UpdateUI()
    {
        // Thread-safe read of results
        float displayFreq;
        string displayNote;
        float displayCents;
        
        lock (resultLock)
        {
            displayFreq = currentFrequency;
            displayNote = currentNote;
            displayCents = currentCentsDeviation;
        }
        
        // Update text displays
        if (frequencyText != null)
            frequencyText.text = $"Frequency: {displayFreq:F2} Hz";
        if (noteText != null)
            noteText.text = $"Note: {displayNote}";
        
        // Update tuning meter if enabled
        if (showTuningMeter && centsText != null)
        {
            UpdateTuningMeter(displayCents);
        }
        
        // Update visualizer bars
        for (int i = 0; i < numBars; i++)
        {
            float targetHeight = barMagnitudes[i] * maxBarHeight * 1000f;
            barHeights[i] = Mathf.Lerp(barHeights[i], targetHeight, 1f - smoothing);
            
            RectTransform barRect = bars[i].rectTransform;
            barRect.sizeDelta = new Vector2(barWidth, barHeights[i]);
            
            // Highlight dominant frequency
            float barFreq = GetFrequencyForBar(i);
            if (displayFreq > 0 && Mathf.Abs(barFreq - displayFreq) < 100f)
            {
                bars[i].color = dominantFreqColor;
            }
            else
            {
                bars[i].color = barColor;
            }
        }
    }
    
    void UpdateTuningMeter(float cents)
    {
        // Update cents text with color coding
        if (centsText != null)
        {
            string centsSign = cents >= 0 ? "+" : "";
            centsText.text = $"{centsSign}{cents:F0} cents";
            
            // Color coding based on tuning accuracy
            if (Mathf.Abs(cents) <= tuningTolerance)
            {
                centsText.color = inTuneColor;
            }
            else if (Mathf.Abs(cents) <= tuningTolerance * 3)
            {
                centsText.color = slightlyOffColor;
            }
            else
            {
                centsText.color = veryOffColor;
            }
        }
        
        // Update tuning needle position and color
        if (tuningNeedle != null)
        {
            // Clamp cents to reasonable display range (-50 to +50 cents)
            float clampedCents = Mathf.Clamp(cents, -50f, 50f);
            
            // Convert cents to needle position (0.0 = far left, 0.5 = center, 1.0 = far right)
            float needlePos = 0.5f + (clampedCents / 100f); // 100 cents = full scale
            needlePos = Mathf.Clamp01(needlePos);
            
            // Update needle position
            RectTransform needleRect = tuningNeedle.rectTransform;
            needleRect.anchorMin = new Vector2(needlePos - 0.005f, 0.2f);
            needleRect.anchorMax = new Vector2(needlePos + 0.005f, 0.8f);
            
            // Update needle color based on tuning accuracy
            if (Mathf.Abs(cents) <= tuningTolerance)
            {
                tuningNeedle.color = inTuneColor;
            }
            else if (Mathf.Abs(cents) <= tuningTolerance * 3)
            {
                tuningNeedle.color = slightlyOffColor;
            }
            else
            {
                tuningNeedle.color = veryOffColor;
            }
        }
    }
    
    string GetNoteFromFrequency(float frequency)
    {
        if (frequency <= 0) return "--";
        
        float A4 = 440f;
        float C0 = A4 * Mathf.Pow(2f, -4.75f);
        
        if (frequency > C0)
        {
            float h = Mathf.Round(12f * Mathf.Log(frequency / C0) / Mathf.Log(2f));
            int octave = Mathf.FloorToInt(h / 12f);
            int noteIndex = Mathf.FloorToInt(h % 12f);
            
            return noteNames[noteIndex] + octave.ToString();
        }
        
        return "--";
    }
    
    float CalculateCentsDeviation(float frequency)
    {
        if (frequency <= 0) return 0f;
        
        // Find the closest note
        float A4 = 440f;
        float C0 = A4 * Mathf.Pow(2f, -4.75f); // C0 reference frequency
        
        // Calculate which note this frequency is closest to
        float h = 12f * Mathf.Log(frequency / C0) / Mathf.Log(2f);
        int nearestNoteIndex = Mathf.RoundToInt(h);
        
        // Calculate the target frequency for that note
        float targetFreq = C0 * Mathf.Pow(2f, nearestNoteIndex / 12f);
        
        // Calculate how many cents off we are
        float centsOff = 1200f * Mathf.Log(frequency / targetFreq) / Mathf.Log(2f);
        
        return centsOff;
    }
    
    float GetFrequencyForBar(int barIndex)
    {
        float freqResolution = (float)sampleRate / fftSize;
        int spectrumBinPerBar = (fftSize / 2) / numBars;
        return (barIndex * spectrumBinPerBar) * freqResolution;
    }
    
    void OnDestroy()
    {
        // Stop pitch detection thread
        isProcessing = false;
        if (pitchDetectionThread != null && pitchDetectionThread.IsAlive)
        {
            pitchDetectionThread.Join(1000); // Wait up to 1 second for thread to stop
        }
        
        // Stop microphone
        if (micDevice != null && Microphone.IsRecording(micDevice))
        {
            Microphone.End(micDevice);
        }
    }
    
    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            isProcessing = false;
        }
        else
        {
            if (pitchDetectionThread == null || !pitchDetectionThread.IsAlive)
            {
                StartPitchDetectionThread();
            }
        }
    }
}