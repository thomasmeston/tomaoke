namespace KaraokeVox.Tests;

public class PitchRibbonTests
{
    [Fact]
    public void Ring_KeepsPointsInHeardRange()
    {
        var ring = new PitchRibbonRing();
        ring.Push(1.0, 440f, 440f, true);
        ring.Push(2.0, 450f, 440f, true);
        ring.Push(5.0, 430f, 440f, true);

        Span<PitchRibbonPoint> buffer = stackalloc PitchRibbonPoint[8];
        int count = ring.CopyRange(1.5, 4.5, buffer);
        Assert.Equal(1, count);
        Assert.Equal(2.0, buffer[0].HeardSec);
        Assert.Equal(450f, buffer[0].DetectedHz);
    }
}
