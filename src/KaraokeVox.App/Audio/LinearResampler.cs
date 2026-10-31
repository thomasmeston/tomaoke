namespace KaraokeVox.App.Audio;

sealed class LinearResampler
{
    readonly float[] _ring = new float[8192];
    readonly int _mask;
    readonly double _step;
    long _written;
    double _read;

    public LinearResampler(int inputRate, int outputRate)
    {
        _mask = _ring.Length - 1;
        _step = inputRate <= 0 || outputRate <= 0 ? 1 : inputRate / (double)outputRate;
    }

    public void Reset()
    {
        Array.Clear(_ring);
        _written = 0;
        _read = 0;
    }

    public void Push(ReadOnlySpan<float> input)
    {
        long write = _written;
        for (int i = 0; i < input.Length; i++)
            _ring[(int)((write + i) & _mask)] = input[i];
        _written = write + input.Length;
    }

    public int Pull(Span<float> output)
    {
        int count = 0;
        while (count < output.Length && _read + 1 < _written)
        {
            long index = (long)_read;
            float fraction = (float)(_read - index);
            float a = _ring[(int)(index & _mask)];
            float b = _ring[(int)((index + 1) & _mask)];
            output[count++] = a + (b - a) * fraction;
            _read += _step;
        }
        return count;
    }
}
