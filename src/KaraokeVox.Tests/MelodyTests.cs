using System.Text.Json;

namespace KaraokeVox.Tests;

public class MelodyTests
{
    [Fact]
    public void MelodyLock_AimsAtTheLeadNote()
    {
        var contour = Steady(440f);
        var controls = ControlSnapshot.Default;
        controls.Mode = TuneMode.MelodyLock;
        controls.Strength = 1f;
        TuneDecision decision = TuneResolver.Decide(controls, contour, 0.2, 400f, true);
        double expected = 12.0 * Math.Log(440.0 / 400.0, 2);
        Assert.InRange(decision.Semitones, expected - 0.05, expected + 0.05);
        Assert.True(decision.Active);
    }

    [Fact]
    public void SongShift_MovesTheMelodyWithTheTrack()
    {
        var contour = Steady(440f);
        var controls = ControlSnapshot.Default;
        controls.Mode = TuneMode.MelodyLock;
        controls.Strength = 1f;
        controls.SongShiftSemitones = 2;
        TuneDecision decision = TuneResolver.Decide(controls, contour, 0.2, 400f, true);
        float shifted = 440f * MathF.Pow(2f, 2f / 12f);
        double expected = 12.0 * Math.Log(shifted / 400.0, 2);
        Assert.InRange(decision.Semitones, expected - 0.05, expected + 0.05);
    }

    [Fact]
    public void MelodyLock_CorrectsAnOctaveBelowTheLead()
    {
        var contour = Steady(440f);
        var controls = ControlSnapshot.Default;
        controls.Mode = TuneMode.MelodyLock;
        controls.Strength = 1f;
        TuneDecision decision = TuneResolver.Decide(controls, contour, 0.2, 220f, true);
        Assert.InRange(decision.Semitones, 11.8f, 12.2f);
    }

    [Fact]
    public void NearestNote_DoesNotJumpAnOctave()
    {
        var contour = Steady(440f);
        var controls = ControlSnapshot.Default;
        controls.Mode = TuneMode.NearestNote;
        controls.Strength = 1f;
        TuneDecision decision = TuneResolver.Decide(controls, contour, 0.2, 220f, true);
        Assert.InRange(decision.Semitones, -0.2f, 0.2f);
    }

    [Fact]
    public void Phrase_PicksTheClosestAllowedNote()
    {
        float c4 = PitchMath.MidiToHz(60);
        float e4 = PitchMath.MidiToHz(64);
        var center = new float[300];
        var conf = new float[300];
        var voiced = new int[300];
        for (int i = 0; i < center.Length; i++)
        {
            center[i] = i % 2 == 0 ? c4 : e4;
            conf[i] = 1f;
            voiced[i] = 1;
        }
        var contour = new MelodyContour(0.01, 0, ScaleKind.Major, center, conf, voiced);
        Assert.True(contour.TryPhrase(0.5, PitchMath.MidiToHz(63), out float target));
        Assert.InRange(PitchMath.HzToMidi(target), 63.9, 64.1);
    }

    [Fact]
    public void MelodyLock_FindsNearestVoicedFrame()
    {
        var center = new float[100];
        var conf = new float[100];
        var flags = new int[100];
        for (int i = 0; i < center.Length; i++)
        {
            center[i] = i == 50 ? 440f : 0f;
            conf[i] = i == 50 ? 0.9f : 0f;
            flags[i] = i == 50 ? 1 : 0;
        }
        var contour = new MelodyContour(0.01, 0, ScaleKind.Major, center, conf, flags);
        Assert.True(contour.TryNearestVoiced(0.48, 0.35, out float hz, out _));
        Assert.Equal(440f, hz);
    }

    [Fact]
    public void UnvoicedReference_BypassesMelodyLock()
    {
        var contour = Steady(0f, voiced: 0);
        var controls = ControlSnapshot.Default;
        controls.Mode = TuneMode.MelodyLock;
        controls.Strength = 1f;
        TuneDecision decision = TuneResolver.Decide(controls, contour, 0.2, 440f, true);
        Assert.InRange(decision.Semitones, -0.01f, 0.01f);
    }

    [Fact]
    public void ContourFile_RoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), "karaoke-vox-contour-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var payload = new
            {
                hopSec = 0.01,
                keyRoot = 9,
                scale = "minor",
                centerHz = new[] { 0f, 440f },
                conf = new[] { 0f, 0.9f },
                voiced = new[] { 0, 1 }
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload));
            MelodyContour contour = MelodyContour.Load(path);
            Assert.Equal(9, contour.KeyRoot);
            Assert.Equal(ScaleKind.NaturalMinor, contour.Scale);
            Assert.True(contour.TryCenter(0.01, out float hz, out _));
            Assert.Equal(440f, hz);
            Assert.False(contour.TryCenter(0.0, out _, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    static MelodyContour Steady(float hz, int voiced = 1)
    {
        var center = new float[100];
        var conf = new float[100];
        var flags = new int[100];
        for (int i = 0; i < center.Length; i++)
        {
            center[i] = hz;
            conf[i] = 1f;
            flags[i] = voiced;
        }
        return new MelodyContour(0.01, 0, ScaleKind.Major, center, conf, flags);
    }
}
