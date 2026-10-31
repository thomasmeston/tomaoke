namespace KaraokeVox;

public sealed class EchoDelay
{
    readonly float[] _buffer;
    int _write;

    public EchoDelay(int sampleRate, int maxDelayMs = 1000)
    {
        _buffer = new float[Math.Max(8, sampleRate * maxDelayMs / 1000)];
    }

    public float Process(float input, int delaySamples, float feedback, float mix)
    {
        if (delaySamples < 1)
            delaySamples = 1;
        if (delaySamples >= _buffer.Length)
            delaySamples = _buffer.Length - 1;
        feedback = Math.Clamp(feedback, 0f, 0.85f);
        mix = Math.Clamp(mix, 0f, 1f);

        int read = _write - delaySamples;
        if (read < 0)
            read += _buffer.Length;
        float delayed = _buffer[read];
        _buffer[_write] = input + delayed * feedback;
        _write++;
        if (_write >= _buffer.Length)
            _write = 0;
        return input + delayed * mix;
    }
}
