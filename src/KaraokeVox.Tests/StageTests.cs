namespace KaraokeVox.Tests;

public class StageTests
{
    [Fact]
    public void Party_TurnsOnHarmonyAndDouble()
    {
        ControlSnapshot party = ControlSnapshot.Party();
        Assert.True(party.HarmonyMix > 0.4f);
        Assert.True(party.DoubleMix > 0.2f);
        Assert.True(party.Strength > 0.7f);
    }

    [Fact]
    public void RobotStage_SnapsRetune()
    {
        ControlSnapshot robot = ControlSnapshot.RobotStage();
        Assert.Equal(VoicePreset.Robot, robot.Voice);
        Assert.Equal(0f, robot.RetuneMs);
        Assert.Equal(0f, robot.HarmonyMix);
    }

    [Fact]
    public void TrackPitch_LeavesZeroShiftOnTheFileClock()
    {
        float[] audio = new float[400];
        audio[20] = 1f;
        var pitch = new TrackPitch();
        TrackFrame frame = pitch.Read(audio, null, 10, 0);
        Assert.Equal(1f, frame.Left);
        Assert.Equal(0f, frame.Right);
    }

    [Fact]
    public void TrackPitch_RaisesPitchWithoutSkippingAheadInTime()
    {
        float[] audio = new float[8000];
        for (int frame = 108; frame <= 120; frame++)
        {
            audio[frame * 2] = 1f;
            audio[frame * 2 + 1] = 1f;
        }
        var pitch = new TrackPitch();
        TrackFrame heard = default;
        for (int i = 0; i <= 100; i++)
            heard = pitch.Read(audio, null, i, 2);
        Assert.True(heard.Left > 0.5f);
    }

    [Fact]
    public void TightDouble_DelaysTheVoice()
    {
        var dub = new TightDouble(48000);
        float early = 0f;
        float later = 0f;
        for (int i = 0; i < 2400; i++)
        {
            float sample = dub.Process(i == 0 ? 1f : 0f);
            if (i < 500)
                early = MathF.Max(early, MathF.Abs(sample));
            else
                later = MathF.Max(later, MathF.Abs(sample));
        }
        Assert.True(early < 0.05f);
        Assert.True(later > 0.2f);
    }

    [Fact]
    public void SyncTap_MeasuresTheClickToMicLag()
    {
        var tap = new SyncTap();
        tap.Start(48000);
        int click = -1;
        float left = 0f;
        float right = 0f;
        for (int i = 0; i < 20000 && click < 0; i++)
        {
            left = 0f;
            right = 0f;
            tap.Tick(0f, ref left, ref right);
            if (MathF.Abs(left) > 0.05f)
                click = i;
        }
        Assert.True(click >= 0);
        for (int i = 0; i < 4800; i++)
            tap.Tick(0f, ref left, ref right);
        tap.Tick(0.8f, ref left, ref right);
        for (int i = 0; i < 20000; i++)
            tap.Tick(0f, ref left, ref right);
        Assert.True(tap.Hits >= 1);
        Assert.InRange(tap.SuggestedTrimMs, -130f, -70f);
    }
}
