using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Unity interface for the JUCE pitch detection plugin
/// </summary>
public class PitchDetector : MonoBehaviour
{
    #region Native Plugin Imports
    
    // Plugin lifecycle
    [DllImport("PitchDetectorPlugin")]
    private static extern bool InitializePitchDetector();
    
    [DllImport("PitchDetectorPlugin")]
    private static extern void ShutdownPitchDetector();
    
    // Pitch detection
    [DllImport("PitchDetectorPlugin")]
    private static extern float GetCurrentPitch();
    
    [DllImport("PitchDetectorPlugin")]
    private static extern float GetConfidence();
    
    [DllImport("PitchDetectorPlugin")]
    private static extern bool IsPitchDetected();
    
    [DllImport("PitchDetectorPlugin")]
    private static extern int GetMidiNote();
    
    [DllImport("PitchDetectorPlugin")]
    private static extern float GetCentsOffset();
    
    [DllImport("PitchDetectorPlugin")]
    private static extern IntPtr GetNoteName();
    
    [DllImport("PitchDetectorPlugin")]
    private static extern int GetOctave();
    
    // Audio device management
    [DllImport("PitchDetectorPlugin")]
    private static extern int GetNumInputDevices();
    
    [DllImport("PitchDetectorPlugin")]
    private static extern bool SetInputDevice(int deviceIndex);
    
    [DllImport("PitchDetectorPlugin")]
    private static extern double GetSampleRate();
    
    // Calibration
    [DllImport("PitchDetectorPlugin")]
    private static extern void SetReferenceA4(float frequency);
    
    [DllImport("PitchDetectorPlugin")]
    private static extern float GetReferenceA4();
    
    #endregion
    
    #region Public Properties
    
    /// <summary>
    /// Current detected pitch in Hz
    /// </summary>
    public float CurrentPitch { get; private set; }
    
    /// <summary>
    /// Confidence of pitch detection (0.0 to 1.0)
    /// </summary>
    public float Confidence { get; private set; }
    
    /// <summary>
    /// Whether a pitch is currently detected
    /// </summary>
    public bool PitchDetected { get; private set; }
    
    /// <summary>
    /// MIDI note number (0-127)
    /// </summary>
    public int MidiNote { get; private set; }
    
    /// <summary>
    /// Cents offset from nearest note (-50 to +50)
    /// </summary>
    public float CentsOffset { get; private set; }
    
    /// <summary>
    /// Note name (e.g., "A", "C#", etc.)
    /// </summary>
    public string NoteName { get; private set; }
    
    /// <summary>
    /// Octave number
    /// </summary>
    public int Octave { get; private set; }
    
    /// <summary>
    /// Reference frequency for A4 (default 440 Hz)
    /// </summary>
    public float ReferenceA4
    {
        get => GetReferenceA4();
        set => SetReferenceA4(value);
    }
    
    #endregion
    
    #region Events
    
    /// <summary>
    /// Event fired when a new pitch is detected
    /// </summary>
    public event Action<float> OnPitchDetected;
    
    /// <summary>
    /// Event fired when pitch detection is lost
    /// </summary>
    public event Action OnPitchLost;
    
    /// <summary>
    /// Event fired when note changes
    /// </summary>
    public event Action<string, int> OnNoteChanged;
    
    #endregion
    
    #region Private Fields
    
    private bool isInitialized = false;
    private string lastNoteName = "";
    private int lastOctave = -1;
    private bool wasDetected = false;
    
    // Update rate limiting
    [SerializeField] private float updateInterval = 0.016f; // ~60 FPS
    private float lastUpdateTime = 0f;
    
    // Debug options
    [Header("Debug")]
    [SerializeField] private bool debugMode = false;
    
    #endregion
    
    #region Unity Lifecycle
    
    void Awake()
    {
        // Initialize the native plugin
        try
        {
            if (!InitializePitchDetector())
            {
                Debug.LogError("Failed to initialize pitch detector plugin!");
                enabled = false;
                return;
            }
            
            isInitialized = true;
            Debug.Log($"Pitch detector initialized. Sample rate: {GetSampleRate()} Hz");
            
            if (debugMode)
            {
                Debug.Log($"Number of input devices: {GetNumInputDevices()}");
            }
        }
        catch (DllNotFoundException e)
        {
            Debug.LogError($"PitchDetectorPlugin.bundle not found! Make sure it's in Assets/Plugins/\n{e.Message}");
            enabled = false;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to initialize pitch detector: {e.Message}");
            enabled = false;
        }
    }
    
    void Update()
    {
        if (!isInitialized) return;
        
        // Rate limit updates
        if (Time.time - lastUpdateTime < updateInterval) return;
        lastUpdateTime = Time.time;
        
        // Update all properties from native plugin
        UpdatePitchData();
        
        // Check for pitch detection changes
        if (PitchDetected && !wasDetected)
        {
            OnPitchDetected?.Invoke(CurrentPitch);
            wasDetected = true;
            
            if (debugMode)
            {
                Debug.Log($"Pitch detected: {CurrentPitch:F2} Hz");
            }
        }
        else if (!PitchDetected && wasDetected)
        {
            OnPitchLost?.Invoke();
            wasDetected = false;
            
            if (debugMode)
            {
                Debug.Log("Pitch lost");
            }
        }
        
        // Check for note changes
        if (PitchDetected && (NoteName != lastNoteName || Octave != lastOctave))
        {
            OnNoteChanged?.Invoke(NoteName, Octave);
            lastNoteName = NoteName;
            lastOctave = Octave;
            
            if (debugMode)
            {
                Debug.Log($"Note changed: {NoteName}{Octave}");
            }
        }
    }
    
    void OnDestroy()
    {
        if (isInitialized)
        {
            try
            {
                ShutdownPitchDetector();
                isInitialized = false;
                Debug.Log("Pitch detector shutdown");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error shutting down pitch detector: {e.Message}");
            }
        }
    }
    
    void OnApplicationPause(bool pauseStatus)
    {
        // Handle mobile app pause/resume
        if (isInitialized)
        {
            if (pauseStatus)
            {
                ShutdownPitchDetector();
                isInitialized = false;
            }
            else
            {
                isInitialized = InitializePitchDetector();
            }
        }
    }
    
    void OnApplicationFocus(bool hasFocus)
    {
        // Additional handling for focus changes
        if (!hasFocus && isInitialized)
        {
            // Optionally pause processing when app loses focus
        }
    }
    
    #endregion
    
    #region Private Methods
    
    private void UpdatePitchData()
    {
        try
        {
            CurrentPitch = GetCurrentPitch();
            Confidence = GetConfidence();
            PitchDetected = IsPitchDetected();
            MidiNote = GetMidiNote();
            CentsOffset = GetCentsOffset();
            Octave = GetOctave();
            
            // Convert IntPtr to string for note name
            IntPtr noteNamePtr = GetNoteName();
            NoteName = Marshal.PtrToStringAnsi(noteNamePtr) ?? "";
        }
        catch (Exception e)
        {
            if (debugMode)
            {
                Debug.LogError($"Error updating pitch data: {e.Message}");
            }
        }
    }
    
    #endregion
    
    #region Public Methods
    
    /// <summary>
    /// Get the frequency of a specific MIDI note
    /// </summary>
    public static float MidiNoteToFrequency(int midiNote)
    {
        return 440f * Mathf.Pow(2f, (midiNote - 69f) / 12f);
    }
    
    /// <summary>
    /// Convert frequency to MIDI note
    /// </summary>
    public static int FrequencyToMidiNote(float frequency)
    {
        if (frequency <= 0) return 0;
        return Mathf.RoundToInt(69f + 12f * Mathf.Log(frequency / 440f, 2f));
    }
    
    /// <summary>
    /// Get standard guitar string frequencies
    /// </summary>
    public static float[] GetGuitarStringFrequencies()
    {
        return new float[]
        {
            82.41f,  // E2
            110.00f, // A2
            146.83f, // D3
            196.00f, // G3
            246.94f, // B3
            329.63f  // E4
        };
    }
    
    /// <summary>
    /// Get standard bass guitar string frequencies
    /// </summary>
    public static float[] GetBassStringFrequencies()
    {
        return new float[]
        {
            41.20f,  // E1
            55.00f,  // A1
            73.42f,  // D2
            98.00f   // G2
        };
    }
    
    /// <summary>
    /// Get the name of a guitar string by index (0-5)
    /// </summary>
    public static string GetGuitarStringName(int stringIndex)
    {
        string[] stringNames = { "E (Low)", "A", "D", "G", "B", "E (High)" };
        if (stringIndex >= 0 && stringIndex < stringNames.Length)
            return stringNames[stringIndex];
        return "";
    }
    
    /// <summary>
    /// Check if the current pitch is close to being in tune
    /// </summary>
    public bool IsInTune(float toleranceCents = 5f)
    {
        return PitchDetected && Mathf.Abs(CentsOffset) <= toleranceCents;
    }
    
    /// <summary>
    /// Get tuning direction indicator
    /// </summary>
    public TuningDirection GetTuningDirection(float deadZoneCents = 2f)
    {
        if (!PitchDetected) return TuningDirection.None;
        
        if (Mathf.Abs(CentsOffset) <= deadZoneCents)
            return TuningDirection.InTune;
        else if (CentsOffset < 0)
            return TuningDirection.TooLow;
        else
            return TuningDirection.TooHigh;
    }
    
    /// <summary>
    /// Find the closest guitar string to the current pitch
    /// </summary>
    public int GetClosestGuitarString(float toleranceHz = 10f)
    {
        if (!PitchDetected) return -1;
        
        float[] stringFreqs = GetGuitarStringFrequencies();
        float minDistance = float.MaxValue;
        int closestString = -1;
        
        for (int i = 0; i < stringFreqs.Length; i++)
        {
            // Check multiple octaves
            for (int octaveOffset = -1; octaveOffset <= 2; octaveOffset++)
            {
                float targetFreq = stringFreqs[i] * Mathf.Pow(2f, octaveOffset);
                float distance = Mathf.Abs(CurrentPitch - targetFreq);
                
                if (distance < minDistance && distance < toleranceHz)
                {
                    minDistance = distance;
                    closestString = i;
                }
            }
        }
        
        return closestString;
    }
    
    /// <summary>
    /// Get a formatted string representation of the current pitch
    /// </summary>
    public string GetPitchString()
    {
        if (!PitchDetected)
            return "No signal";
        
        string noteStr = $"{NoteName}{Octave}";
        string freqStr = $"{CurrentPitch:F2} Hz";
        string centsStr = CentsOffset >= 0 ? $"+{CentsOffset:F1}" : $"{CentsOffset:F1}";
        
        return $"{noteStr} ({freqStr}) {centsStr} cents";
    }
    
    /// <summary>
    /// Reset the pitch detector
    /// </summary>
    public void Reset()
    {
        if (isInitialized)
        {
            ShutdownPitchDetector();
            isInitialized = InitializePitchDetector();
        }
    }
    
    #endregion
    
    #region Enums
    
    public enum TuningDirection
    {
        None,
        TooLow,
        InTune,
        TooHigh
    }
    
    #endregion
    
    #region GUI Debug Display
    
#if UNITY_EDITOR
    void OnGUI()
    {
        if (!debugMode || !isInitialized) return;
        
        // Create debug display
        GUI.Box(new Rect(10, 10, 250, 150), "Pitch Detector Debug");
        
        int y = 35;
        GUI.Label(new Rect(20, y, 230, 20), $"Pitch: {CurrentPitch:F2} Hz");
        y += 20;
        GUI.Label(new Rect(20, y, 230, 20), $"Note: {NoteName}{Octave}");
        y += 20;
        GUI.Label(new Rect(20, y, 230, 20), $"Cents: {(CentsOffset >= 0 ? "+" : "")}{CentsOffset:F1}");
        y += 20;
        GUI.Label(new Rect(20, y, 230, 20), $"Confidence: {(Confidence * 100):F0}%");
        y += 20;
        GUI.Label(new Rect(20, y, 230, 20), $"Status: {(PitchDetected ? "Detecting" : "No Signal")}");
    }
#endif
    
    #endregion
}