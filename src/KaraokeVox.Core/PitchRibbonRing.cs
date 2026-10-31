namespace KaraokeVox;

public readonly struct PitchRibbonPoint
{
    public double HeardSec { get; init; }
    public float DetectedHz { get; init; }
    public float TargetHz { get; init; }
    public bool Voiced { get; init; }
}

public sealed class PitchRibbonRing
{
    public const int Capacity = 512;

    readonly PitchRibbonPoint[] _buffer = new PitchRibbonPoint[Capacity];
    long _writeIndex;

    public void Push(double heardSec, float detectedHz, float targetHz, bool voiced)
    {
        long index = Interlocked.Increment(ref _writeIndex) - 1;
        int slot = (int)(index & (Capacity - 1));
        _buffer[slot] = new PitchRibbonPoint
        {
            HeardSec = heardSec,
            DetectedHz = detectedHz,
            TargetHz = targetHz,
            Voiced = voiced
        };
    }

    public void Reset() => Interlocked.Exchange(ref _writeIndex, 0);

    public int CopyRange(double minHeard, double maxHeard, Span<PitchRibbonPoint> dest)
    {
        long end = Volatile.Read(ref _writeIndex);
        if (end <= 0 || dest.Length == 0)
            return 0;

        long start = Math.Max(0, end - Capacity);
        int count = 0;
        for (long i = start; i < end; i++)
        {
            ref readonly PitchRibbonPoint point = ref _buffer[(int)(i & (Capacity - 1))];
            if (point.HeardSec < minHeard || point.HeardSec > maxHeard)
                continue;
            if (count >= dest.Length)
                break;
            dest[count++] = point;
        }
        return count;
    }
}
