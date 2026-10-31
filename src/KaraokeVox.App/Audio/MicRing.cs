namespace KaraokeVox.App.Audio;

sealed class MicRing
{
    readonly float[] _data;
    readonly int _mask;
    long _write;
    long _read;

    public MicRing(int capacityPowerOfTwo = 65536)
    {
        _data = new float[capacityPowerOfTwo];
        _mask = capacityPowerOfTwo - 1;
    }

    public int MaxHoldSamples { get; set; } = 8192;

    public long Available => Volatile.Read(ref _write) - Volatile.Read(ref _read);

    public void Write(ReadOnlySpan<float> source)
    {
        long write = _write;
        for (int i = 0; i < source.Length; i++)
            _data[(int)((write + i) & _mask)] = source[i];
        Volatile.Write(ref _write, write + source.Length);
    }

    public int Read(Span<float> destination)
    {
        long write = Volatile.Read(ref _write);
        long read = _read;
        long available = write - read;
        if (available > MaxHoldSamples)
        {
            read = write - MaxHoldSamples;
            available = MaxHoldSamples;
        }
        int count = (int)Math.Min(available, destination.Length);
        for (int i = 0; i < count; i++)
            destination[i] = _data[(int)((read + i) & _mask)];
        if (count < destination.Length)
            destination[count..].Clear();
        Volatile.Write(ref _read, read + count);
        return count;
    }

    public void Reset()
    {
        Array.Clear(_data);
        _write = 0;
        _read = 0;
    }
}
