using System.Text.Json;
using System.Text.Json.Serialization;

namespace KaraokeVox;

public sealed class MelodyContour
{
    public const float ConfidenceThreshold = 0.5f;
    public const double PhraseWindowSec = 1.5;

    public double HopSec { get; }
    public int KeyRoot { get; }
    public ScaleKind Scale { get; }
    public float[] CenterHz { get; }
    public float[] Confidence { get; }
    public int[] Voiced { get; }

    public MelodyContour(double hopSec, int keyRoot, ScaleKind scale, float[] centerHz, float[] confidence, int[] voiced)
    {
        if (hopSec <= 0)
            throw new ArgumentOutOfRangeException(nameof(hopSec));
        if (centerHz.Length != confidence.Length || centerHz.Length != voiced.Length)
            throw new ArgumentException("Contour arrays must be the same length.");
        HopSec = hopSec;
        KeyRoot = Math.Clamp(keyRoot, 0, 11);
        Scale = scale;
        CenterHz = centerHz;
        Confidence = confidence;
        Voiced = voiced;
    }

    public int IndexAt(double timeSec)
    {
        if (timeSec < 0 || CenterHz.Length == 0)
            return -1;
        int index = (int)(timeSec / HopSec);
        if ((uint)index >= (uint)CenterHz.Length)
            return -1;
        return index;
    }

    public bool TryNearestVoiced(double timeSec, double windowSec, out float centerHz, out float confidence)
    {
        int index = IndexAt(timeSec);
        if (index < 0)
        {
            centerHz = 0;
            confidence = 0;
            return false;
        }

        int radius = Math.Max(1, (int)Math.Ceiling(windowSec / HopSec));
        int bestIndex = -1;
        float bestConfidence = 0;
        int bestDistance = int.MaxValue;
        for (int delta = 0; delta <= radius; delta++)
        {
            Consider(index + delta, delta);
            if (delta > 0)
                Consider(index - delta, delta);
        }

        if (bestIndex < 0)
        {
            centerHz = 0;
            confidence = 0;
            return false;
        }

        centerHz = CenterHz[bestIndex];
        confidence = Confidence[bestIndex];
        return true;

        void Consider(int candidate, int distance)
        {
            if ((uint)candidate >= (uint)CenterHz.Length)
                return;
            if (Voiced[candidate] == 0 || CenterHz[candidate] <= 0)
                return;
            float conf = Confidence[candidate];
            if (conf < 0.35f)
                return;
            if (distance > bestDistance || (distance == bestDistance && conf <= bestConfidence))
                return;
            bestDistance = distance;
            bestConfidence = conf;
            bestIndex = candidate;
        }
    }

    public bool TryCenter(double timeSec, out float centerHz, out float confidence)
    {
        int index = IndexAt(timeSec);
        if (index < 0 || Voiced[index] == 0 || Confidence[index] < ConfidenceThreshold || CenterHz[index] <= 0)
        {
            centerHz = 0;
            confidence = 0;
            return false;
        }
        centerHz = CenterHz[index];
        confidence = Confidence[index];
        return true;
    }

    public bool TryPhrase(double timeSec, float detectedHz, out float targetHz)
    {
        targetHz = 0;
        int center = IndexAt(timeSec);
        if (center < 0 || detectedHz <= 0)
            return false;

        int radius = Math.Max(1, (int)Math.Round(PhraseWindowSec / HopSec));
        Span<byte> mask = stackalloc byte[12];
        int start = Math.Max(0, center - radius);
        int end = Math.Min(CenterHz.Length - 1, center + radius);
        bool any = false;
        for (int i = start; i <= end; i++)
        {
            if (Voiced[i] == 0 || Confidence[i] < ConfidenceThreshold || CenterHz[i] <= 0)
                continue;
            mask[PitchMath.PitchClass(CenterHz[i])] = 1;
            any = true;
        }
        if (!any)
            return false;

        double midi = PitchMath.HzToMidi(detectedHz);
        int bestPc = 0;
        double bestDist = 99;
        for (int pc = 0; pc < 12; pc++)
        {
            if (mask[pc] == 0)
                continue;
            float hz = PitchMath.NearestHzWithPitchClass(detectedHz, pc);
            double dist = Math.Abs(PitchMath.HzToMidi(hz) - midi);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPc = pc;
            }
        }
        targetHz = PitchMath.NearestHzWithPitchClass(detectedHz, bestPc);
        return true;
    }

    public static MelodyContour Load(string path)
    {
        string json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<ContourFile>(json, JsonOptions)
            ?? throw new InvalidDataException("Contour file was empty.");
        if (dto.CenterHz == null || dto.Conf == null || dto.Voiced == null)
            throw new InvalidDataException("Contour file is missing pitch data.");
        return new MelodyContour(dto.HopSec, dto.KeyRoot, ParseScale(dto.Scale), dto.CenterHz, dto.Conf, dto.Voiced);
    }

    public static ScaleKind ParseScale(string? name) => name?.ToLowerInvariant() switch
    {
        "minor" or "naturalminor" => ScaleKind.NaturalMinor,
        "pentmajor" or "majorpentatonic" => ScaleKind.MajorPentatonic,
        "pentminor" or "minorpentatonic" => ScaleKind.MinorPentatonic,
        "chromatic" => ScaleKind.Chromatic,
        _ => ScaleKind.Major
    };

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    sealed class ContourFile
    {
        public double HopSec { get; set; }
        public int KeyRoot { get; set; }
        public string? Scale { get; set; }
        public float[]? CenterHz { get; set; }
        public float[]? Conf { get; set; }
        public int[]? Voiced { get; set; }

        [JsonIgnore]
        public float[]? Hz { get; set; }
    }
}
