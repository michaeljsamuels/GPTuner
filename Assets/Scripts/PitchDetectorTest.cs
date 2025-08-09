using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple test component to verify the pitch detector is working
/// This is a minimal implementation for testing - use GuitarTunerVisualizer for full features
/// </summary>
public class PitchDetectorTest : MonoBehaviour
{
    [Header("UI References (Optional)")]
    [SerializeField] private Text displayText;
    [SerializeField] private Slider tuningSlider;
    [SerializeField] private Image tuningIndicator;
    
    [Header("Settings")]
    [SerializeField] private bool useConsoleOutput = true;
    [SerializeField] private float inTuneTolerance = 5f; // cents
    
    [Header("Colors")]
    [SerializeField] private Color inTuneColor = Color.green;
    [SerializeField] private Color tooLowColor = Color.red;
    [SerializeField] private Color tooHighColor = Color.yellow;
    [SerializeField] private Color noSignalColor = Color.gray;
    
    private PitchDetector pitchDetector;
    
    void Start()
    {
        // Get or add PitchDetector component
        pitchDetector = GetComponent<PitchDetector>();
        if (pitchDetector == null)
        {
            pitchDetector = gameObject.AddComponent<PitchDetector>();
        }
        
        // Subscribe to events
        pitchDetector.OnPitchDetected += OnPitchDetected;
        pitchDetector.OnPitchLost += OnPitchLost;
        pitchDetector.OnNoteChanged += OnNoteChanged;
        
        Debug.Log("PitchDetectorTest: Started. Play a note on your guitar!");
        
        // Initialize UI if references are set
        if (tuningSlider != null)
        {
            tuningSlider.minValue = -50f;
            tuningSlider.maxValue = 50f;
            tuningSlider.value = 0f;
        }
    }
    
    void Update()
    {
        if (pitchDetector == null) return;
        
        // Update UI if references are set
        if (displayText != null)
        {
            if (pitchDetector.PitchDetected)
            {
                string noteInfo = $"{pitchDetector.NoteName}{pitchDetector.Octave}";
                string freqInfo = $"{pitchDetector.CurrentPitch:F2} Hz";
                string centsInfo = pitchDetector.CentsOffset >= 0 
                    ? $"+{pitchDetector.CentsOffset:F1}" 
                    : $"{pitchDetector.CentsOffset:F1}";
                string confidenceInfo = $"{(pitchDetector.Confidence * 100):F0}%";
                
                displayText.text = $"Note: {noteInfo}\n" +
                                  $"Frequency: {freqInfo}\n" +
                                  $"Cents: {centsInfo}\n" +
                                  $"Confidence: {confidenceInfo}";
                
                // Color based on tuning
                if (pitchDetector.IsInTune(inTuneTolerance))
                {
                    displayText.color = inTuneColor;
                }
                else if (pitchDetector.CentsOffset < 0)
                {
                    displayText.color = tooLowColor;
                }
                else
                {
                    displayText.color = tooHighColor;
                }
            }
            else
            {
                displayText.text = "No signal detected\nPlay a note!";
                displayText.color = noSignalColor;
            }
        }
        
        // Update tuning slider
        if (tuningSlider != null)
        {
            if (pitchDetector.PitchDetected)
            {
                tuningSlider.value = pitchDetector.CentsOffset;
                
                // Update slider color
                if (tuningSlider.fillRect != null)
                {
                    var image = tuningSlider.fillRect.GetComponent<Image>();
                    if (image != null)
                    {
                        if (pitchDetector.IsInTune(inTuneTolerance))
                            image.color = inTuneColor;
                        else if (pitchDetector.CentsOffset < 0)
                            image.color = tooLowColor;
                        else
                            image.color = tooHighColor;
                    }
                }
            }
            else
            {
                tuningSlider.value = 0f;
            }
        }
        
        // Update tuning indicator
        if (tuningIndicator != null)
        {
            if (pitchDetector.PitchDetected)
            {
                if (pitchDetector.IsInTune(inTuneTolerance))
                    tuningIndicator.color = inTuneColor;
                else if (pitchDetector.CentsOffset < 0)
                    tuningIndicator.color = tooLowColor;
                else
                    tuningIndicator.color = tooHighColor;
            }
            else
            {
                tuningIndicator.color = noSignalColor;
            }
        }
    }
    
    void OnPitchDetected(float frequency)
    {
        if (useConsoleOutput)
        {
            Debug.Log($"🎸 Pitch detected: {frequency:F2} Hz");
        }
    }
    
    void OnPitchLost()
    {
        if (useConsoleOutput)
        {
            Debug.Log("🔇 Pitch signal lost");
        }
    }
    
    void OnNoteChanged(string noteName, int octave)
    {
        if (useConsoleOutput)
        {
            Debug.Log($"🎵 Note changed to: {noteName}{octave}");
            
            // Check which guitar string this might be
            int stringIndex = pitchDetector.GetClosestGuitarString();
            if (stringIndex >= 0)
            {
                string stringName = PitchDetector.GetGuitarStringName(stringIndex);
                Debug.Log($"   Closest guitar string: {stringName}");
            }
        }
    }
    
    void OnDestroy()
    {
        // Unsubscribe from events
        if (pitchDetector != null)
        {
            pitchDetector.OnPitchDetected -= OnPitchDetected;
            pitchDetector.OnPitchLost -= OnPitchLost;
            pitchDetector.OnNoteChanged -= OnNoteChanged;
        }
    }
    
    #region Public Methods for UI Buttons
    
    public void SetReferenceA440()
    {
        if (pitchDetector != null)
        {
            pitchDetector.ReferenceA4 = 440f;
            Debug.Log("Reference A4 set to 440 Hz");
        }
    }
    
    public void SetReferenceA432()
    {
        if (pitchDetector != null)
        {
            pitchDetector.ReferenceA4 = 432f;
            Debug.Log("Reference A4 set to 432 Hz");
        }
    }
    
    public void ToggleDebugMode()
    {
        useConsoleOutput = !useConsoleOutput;
        Debug.Log($"Console output: {(useConsoleOutput ? "ON" : "OFF")}");
    }
    
    #endregion
}