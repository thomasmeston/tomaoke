namespace KaraokeVox;

public static class PitchMath
{
    static readonly int[] Major = [0, 2, 4, 5, 7, 9, 11];
    static readonly int[] NaturalMinor = [0, 2, 3, 5, 7, 8, 10];
    static readonly int[] MajorPentatonic = [0, 2, 4, 7, 9];
    static readonly int[] MinorPentatonic = [0, 3, 5, 7, 10];
    static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public static double HzToMidi(float hz) => 69.0 + 12.0 * Math.Log(hz / 440.0, 2);

    public static float MidiToHz(double midi) => (float)(440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0));

    public static int PitchClass(float hz)
    {
        int midi = (int)Math.Round(HzToMidi(hz), MidpointRounding.AwayFromZero);
        return Mod(midi, 12);
    }

    public static bool InScale(int pitchClass, int keyRoot, ScaleKind scale)
    {
        if (scale == ScaleKind.Chromatic)
            return true;
        int rel = Mod(pitchClass - keyRoot, 12);
        int[] degrees = scale switch
        {
            ScaleKind.NaturalMinor => NaturalMinor,
            ScaleKind.MajorPentatonic => MajorPentatonic,
            ScaleKind.MinorPentatonic => MinorPentatonic,
            _ => Major
        };
        for (int i = 0; i < degrees.Length; i++)
        {
            if (degrees[i] == rel)
                return true;
        }
        return false;
    }

    public static float SnapToScale(float hz, int keyRoot, ScaleKind scale)
    {
        if (hz <= 0)
            return 0;
        double midi = HzToMidi(hz);
        int center = (int)Math.Round(midi, MidpointRounding.AwayFromZero);
        int best = center;
        double bestDist = 99;
        for (int n = center - 6; n <= center + 6; n++)
        {
            if (!InScale(Mod(n, 12), keyRoot, scale))
                continue;
            double dist = Math.Abs(n - midi);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = n;
            }
        }
        return MidiToHz(best);
    }

    public static float DiatonicStep(float hz, int keyRoot, ScaleKind scale, int steps)
    {
        if (hz <= 0 || steps == 0)
            return hz;
        double midi = HzToMidi(hz);
        int center = (int)Math.Round(midi, MidpointRounding.AwayFromZero);
        if (scale == ScaleKind.Chromatic)
            return MidiToHz(center + steps * 2);

        int[] degrees = scale switch
        {
            ScaleKind.NaturalMinor => NaturalMinor,
            ScaleKind.MajorPentatonic => MajorPentatonic,
            ScaleKind.MinorPentatonic => MinorPentatonic,
            _ => Major
        };
        int bestIndex = 0;
        int bestDist = 99;
        for (int i = 0; i < degrees.Length; i++)
        {
            int candidate = center - Mod(center - keyRoot, 12) + degrees[i];
            int dist = Math.Abs(candidate - center);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
            }
        }

        int index = bestIndex + steps;
        int octave = 0;
        int length = degrees.Length;
        while (index < 0)
        {
            index += length;
            octave -= 12;
        }
        octave += 12 * (index / length);
        index %= length;
        int baseMidi = center - Mod(center - keyRoot, 12);
        return MidiToHz(baseMidi + degrees[index] + octave);
    }

    public static float NearestHzWithPitchClass(float detectedHz, int pitchClass)
    {
        double midi = HzToMidi(detectedHz);
        int rounded = (int)Math.Round(midi, MidpointRounding.AwayFromZero);
        int delta = pitchClass - Mod(rounded, 12);
        if (delta > 6)
            delta -= 12;
        if (delta < -6)
            delta += 12;
        return MidiToHz(rounded + delta);
    }

    /// <summary>Shift from detected to desired pitch for melody lock, up to one octave each way.</summary>
    public static double MelodyShiftSemitones(float detectedHz, float desiredHz)
    {
        if (detectedHz < 20f || desiredHz < 20f)
            return 0;
        double semis = 12.0 * Math.Log(desiredHz / detectedHz, 2);
        return Math.Clamp(semis, -12.0, 12.0);
    }

    public static string Format(float hz)
    {
        if (hz < 20 || float.IsNaN(hz))
            return "—";
        int midi = (int)Math.Round(HzToMidi(hz), MidpointRounding.AwayFromZero);
        int pc = Mod(midi, 12);
        int octave = midi / 12 - 1;
        return $"{Names[pc]}{octave}  {hz:0} Hz";
    }

    public static int Mod(int value, int modulo)
    {
        int r = value % modulo;
        return r < 0 ? r + modulo : r;
    }
}
