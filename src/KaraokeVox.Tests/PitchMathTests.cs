namespace KaraokeVox.Tests;

public class PitchMathTests
{
    [Fact]
    public void ScaleSnap_MovesAbTowardGInCMajor()
    {
        float ab = PitchMath.MidiToHz(56);
        float snapped = PitchMath.SnapToScale(ab, 0, ScaleKind.Major);
        Assert.InRange(PitchMath.HzToMidi(snapped), 54.9, 55.1);
    }

    [Fact]
    public void NearestPitchClass_StaysInTheSungOctave()
    {
        float a3 = PitchMath.MidiToHz(57);
        float target = PitchMath.NearestHzWithPitchClass(a3, PitchMath.PitchClass(PitchMath.MidiToHz(69)));
        Assert.InRange(target, a3 - 1f, a3 + 1f);
    }

    [Fact]
    public void DiatonicThird_StepsUpTheScale()
    {
        float c4 = PitchMath.MidiToHz(60);
        float third = PitchMath.DiatonicStep(c4, 0, ScaleKind.Major, 2);
        Assert.InRange(PitchMath.HzToMidi(third), 63.9, 64.1);

        float b4 = PitchMath.MidiToHz(71);
        float next = PitchMath.DiatonicStep(b4, 0, ScaleKind.Major, 2);
        Assert.InRange(PitchMath.HzToMidi(next), 73.9, 74.1);
    }

    [Fact]
    public void Fold_KeepsASingleOctave()
    {
        Assert.Equal(0, TuneResolver.Fold(24), 3);
        Assert.Equal(0, TuneResolver.Fold(12), 3);
        Assert.Equal(0, TuneResolver.Fold(-12), 3);
        Assert.Equal(1, TuneResolver.Fold(-23), 3);
        Assert.Equal(-5, TuneResolver.Fold(7), 3);
    }
}
