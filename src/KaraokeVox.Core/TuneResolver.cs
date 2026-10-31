namespace KaraokeVox;

public readonly struct TuneDecision
{
    public float Semitones { get; init; }
    public float TargetHz { get; init; }
    public bool Active { get; init; }
}

public static class TuneResolver
{
    public static TuneDecision Decide(in ControlSnapshot controls, MelodyContour? contour, double heardTimeSec, float detectedHz, bool voiced)
    {
        if (!voiced || detectedHz < 50f || controls.TuneBypass)
            return default;

        float voice = controls.Voice switch
        {
            VoicePreset.Lower => -5f,
            VoicePreset.Higher => 5f,
            _ => 0f
        };
        bool hasDesired = TryDesired(controls, contour, heardTimeSec, detectedHz, out float desired);
        if (hasDesired && desired > 0f && controls.SongShiftSemitones != 0)
            desired *= MathF.Pow(2f, controls.SongShiftSemitones / 12f);
        double correction = 0;
        if (hasDesired && desired > 0f)
        {
            correction = controls.Mode == TuneMode.MelodyLock
                ? PitchMath.MelodyShiftSemitones(detectedHz, desired)
                : Fold(12.0 * Math.Log(desired / detectedHz, 2));
        }

        float applied = voice + (float)correction;
        float shown = voice + (float)correction;
        return new TuneDecision
        {
            Semitones = applied,
            TargetHz = detectedHz * MathF.Pow(2f, shown / 12f),
            Active = true
        };
    }

    static bool TryDesired(in ControlSnapshot controls, MelodyContour? contour, double heardTimeSec, float detectedHz, out float desiredHz)
    {
        TuneMode mode = controls.Mode;
        bool melody = contour != null && heardTimeSec >= 0;
        if (!melody)
            mode = TuneMode.Scale;

        switch (mode)
        {
            case TuneMode.MelodyLock:
                if (contour!.TryCenter(heardTimeSec, out desiredHz, out _))
                    return true;
                return contour.TryNearestVoiced(heardTimeSec, 0.35, out desiredHz, out _);
            case TuneMode.NearestNote:
                if (contour!.TryCenter(heardTimeSec, out float center, out _))
                {
                    desiredHz = PitchMath.NearestHzWithPitchClass(detectedHz, PitchMath.PitchClass(center));
                    return true;
                }
                desiredHz = 0;
                return false;
            case TuneMode.Phrase:
                if (contour!.TryPhrase(heardTimeSec, detectedHz, out desiredHz))
                    return true;
                desiredHz = PitchMath.SnapToScale(detectedHz, controls.KeyRoot, controls.Scale);
                return desiredHz > 0;
            default:
                desiredHz = PitchMath.SnapToScale(detectedHz, controls.KeyRoot, controls.Scale);
                return desiredHz > 0;
        }
    }

    public static double Fold(double semitones)
    {
        double wrapped = semitones % 12.0;
        if (wrapped >= 6)
            wrapped -= 12;
        if (wrapped < -6)
            wrapped += 12;
        return wrapped;
    }
}
