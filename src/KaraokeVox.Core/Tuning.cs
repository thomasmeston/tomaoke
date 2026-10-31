namespace KaraokeVox;

public enum TuneMode
{
    Scale = 0,
    MelodyLock = 1,
    NearestNote = 2,
    Phrase = 3
}

public enum ScaleKind
{
    Major = 0,
    NaturalMinor = 1,
    MajorPentatonic = 2,
    MinorPentatonic = 3,
    Chromatic = 4
}

public enum VoicePreset
{
    Normal = 0,
    Lower = 1,
    Higher = 2,
    Robot = 3
}

public struct ControlSnapshot
{
    public float MicGain;
    public float MusicGain;
    public float GuideGain;
    public float MasterGain;
    public bool TuneBypass;
    public float GateThresholdDb;
    public float EchoDelayMs;
    public float EchoFeedback;
    public float EchoMix;
    public float ReverbSize;
    public float ReverbMix;
    public float Strength;
    public float RetuneMs;
    public VoicePreset Voice;
    public TuneMode Mode;
    public int KeyRoot;
    public ScaleKind Scale;
    public float SyncTrimMs;
    public float HarmonyMix;
    public float DoubleMix;
    public int SongShiftSemitones;

    public static ControlSnapshot Natural()
    {
        ControlSnapshot controls = Default;
        controls.Strength = 0.35f;
        controls.RetuneMs = 80f;
        controls.ReverbMix = 0.12f;
        controls.EchoMix = 0f;
        controls.HarmonyMix = 0f;
        controls.DoubleMix = 0f;
        return controls;
    }

    public static ControlSnapshot Party()
    {
        ControlSnapshot controls = Default;
        controls.Strength = 0.85f;
        controls.RetuneMs = 25f;
        controls.ReverbMix = 0.28f;
        controls.EchoMix = 0.22f;
        controls.EchoDelayMs = 180f;
        controls.EchoFeedback = 0.3f;
        controls.HarmonyMix = 0.55f;
        controls.DoubleMix = 0.4f;
        return controls;
    }

    public static ControlSnapshot RobotStage()
    {
        ControlSnapshot controls = Default;
        controls.Strength = 1f;
        controls.RetuneMs = 0f;
        controls.Voice = VoicePreset.Robot;
        controls.ReverbMix = 0.1f;
        controls.EchoMix = 0f;
        controls.HarmonyMix = 0f;
        controls.DoubleMix = 0f;
        return controls;
    }

    public static ControlSnapshot Default => new()
    {
        MicGain = 1f,
        MusicGain = 0.85f,
        GuideGain = 0f,
        MasterGain = 0.9f,
        GateThresholdDb = -46f,
        EchoDelayMs = 180f,
        EchoFeedback = 0.3f,
        EchoMix = 0f,
        ReverbSize = 0.45f,
        ReverbMix = 0.18f,
        Strength = 0f,
        RetuneMs = 40f,
        Voice = VoicePreset.Normal,
        Mode = TuneMode.Scale,
        KeyRoot = 0,
        Scale = ScaleKind.Major,
        SyncTrimMs = 0f
    };
}
