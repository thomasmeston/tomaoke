namespace KaraokeVox.Tests;

public class EffectTests
{
    [Fact]
    public void HighPass_RemovesDc()
    {
        var filter = new BiquadHighPass(48000, 90f);
        float last = 1f;
        for (int i = 0; i < 8000; i++)
            last = filter.Process(1f);
        Assert.InRange(last, -0.05f, 0.05f);
    }

    [Fact]
    public void Echo_LandsOnTheDelay()
    {
        var echo = new EchoDelay(48000);
        float[] y = new float[6000];
        for (int i = 0; i < y.Length; i++)
            y[i] = echo.Process(i == 0 ? 1f : 0f, 4800, 0f, 1f);
        Assert.InRange(y[0], 0.99f, 1.01f);
        Assert.InRange(y[4800], 0.9f, 1.1f);
    }

    [Fact]
    public void Gate_ClosesOnQuietInput()
    {
        var gate = new NoiseGate(48000);
        float quiet = 0f;
        for (int i = 0; i < 4000; i++)
            quiet = gate.Process(0.001f, -20f);
        Assert.InRange(MathF.Abs(quiet), 0f, 0.0002f);

        var open = new NoiseGate(48000);
        float loud = 0f;
        for (int i = 0; i < 4000; i++)
            loud = open.Process(0.4f, -20f);
        Assert.InRange(loud, 0.35f, 0.45f);
    }

    [Fact]
    public void Reverb_StaysFinite()
    {
        var reverb = new SchroederReverb(48000);
        reverb.SetSize(0.8f);
        float peak = 0f;
        for (int i = 0; i < 12000; i++)
        {
            float y = reverb.Process(i == 0 ? 1f : 0f);
            Assert.True(float.IsFinite(y));
            peak = MathF.Max(peak, MathF.Abs(y));
        }
        Assert.InRange(peak, 0.001f, 2f);
    }
}
