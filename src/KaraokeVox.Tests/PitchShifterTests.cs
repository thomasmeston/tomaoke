namespace KaraokeVox.Tests;

public class PitchShifterTests
{
    const int Rate = 48000;

    [Fact]
    public void StrengthZero_KeepsTheSungPitch()
    {
        var (hz, _, _) = MeasureChain(207.65f, strength: 0f);
        Assert.InRange(hz, 198f, 218f);
    }

    [Fact]
    public void FullStrength_SnapsFToEInMajorPentatonic()
    {
        float source = PitchMath.MidiToHz(65);
        var (hz, detected, target) = Measure(source, strength: 1f, ScaleKind.MajorPentatonic);
        Assert.True(hz is > 310f and < 345f, $"out={hz:0.0} detected={detected:0.0} target={target:0.0}");
    }

    static (float Hz, float Detected, float Target) Measure(float sourceHz, float strength, ScaleKind scale = ScaleKind.Major)
    {
        var shifter = new PitchShifter(Rate);
        var controls = ControlSnapshot.Default;
        controls.Strength = strength;
        controls.RetuneMs = 0f;
        controls.KeyRoot = 0;
        controls.Scale = scale;
        controls.Mode = TuneMode.Scale;
        int total = Rate;
        var input = new float[2048];
        var output = new float[total];
        int written = 0;
        double phase = 0;
        double step = 2 * Math.PI * sourceHz / Rate;
        while (written < total)
        {
            int n = Math.Min(input.Length, total - written);
            for (int i = 0; i < n; i++)
            {
                input[i] = 0.4f * (float)Math.Sin(phase);
                phase += step;
            }
            shifter.Process(input.AsSpan(0, n), output.AsSpan(written, n), controls, null, -1, 0);
            written += n;
        }

        int start = (int)(0.35 * Rate);
        int end = (int)(0.9 * Rate);
        int crossings = 0;
        for (int i = start + 1; i < end; i++)
        {
            if (output[i - 1] <= 0f && output[i] > 0f)
                crossings++;
        }
        float hz = crossings / ((end - start) / (float)Rate);
        return (hz, shifter.DetectedHz, shifter.TargetHz);
    }

    static (float Hz, float Detected, float Target) MeasureChain(float sourceHz, float strength)
    {
        var chain = new DspChain(Rate);
        var controls = ControlSnapshot.Default;
        controls.Strength = strength;
        controls.RetuneMs = 0f;
        int total = Rate;
        var input = new float[2048];
        var output = new float[total];
        int written = 0;
        double phase = 0;
        double step = 2 * Math.PI * sourceHz / Rate;
        while (written < total)
        {
            int n = Math.Min(input.Length, total - written);
            for (int i = 0; i < n; i++)
            {
                input[i] = 0.4f * (float)Math.Sin(phase);
                phase += step;
            }
            chain.Process(input.AsSpan(0, n), output.AsSpan(written, n), controls, null, -1, 0, 0);
            written += n;
        }

        int start = (int)(0.35 * Rate);
        int end = (int)(0.9 * Rate);
        int crossings = 0;
        for (int i = start + 1; i < end; i++)
        {
            if (output[i - 1] <= 0f && output[i] > 0f)
                crossings++;
        }
        float hz = crossings / ((end - start) / (float)Rate);
        return (hz, chain.Pitch.DetectedHz, chain.Pitch.TargetHz);
    }
}
