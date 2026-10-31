namespace KaraokeVox;

public sealed class TightDouble
{
    readonly float[] _buffer;
    readonly int _mask;
    readonly double _delay;
    readonly double _window;
    long _write;
    double _read;
    bool _fading;
    int _fadePos;
    int _fadeLen;
    double _fadeFrom;
    double _fadeTo;

    public TightDouble(int sampleRate)
    {
        int length = 1;
        int need = Math.Max(64, sampleRate / 5);
        while (length < need)
            length *= 2;
        _buffer = new float[length];
        _mask = length - 1;
        _delay = sampleRate * 0.028;
        _window = sampleRate * 0.012;
        _read = -_delay;
        _fadeLen = Math.Clamp(sampleRate / 200, 16, 256);
    }

    public float Process(float input)
    {
        _buffer[(int)(_write & _mask)] = input;
        _write++;
        const double ratio = 1.0069;
        float sample;
        if (_fading)
        {
            float mix = _fadePos / (float)_fadeLen;
            sample = ReadAt(_fadeFrom) + (ReadAt(_fadeTo) - ReadAt(_fadeFrom)) * mix;
            _fadeFrom += ratio;
            _fadeTo += ratio;
            _fadePos++;
            if (_fadePos >= _fadeLen)
            {
                _fading = false;
                _read = _fadeTo;
            }
        }
        else
        {
            sample = ReadAt(_read);
            _read += ratio;
            double lag = _write - _read;
            if (lag < _delay - _window)
                BeginFade(_read, _read - (_delay - lag));
            else if (lag > _delay + _window)
                BeginFade(_read, _read + (lag - _delay));
        }
        return sample;
    }

    void BeginFade(double from, double to)
    {
        _fadeFrom = from;
        _fadeTo = to;
        _fadePos = 0;
        _fading = true;
    }

    float ReadAt(double position)
    {
        if (position < 0 || position >= _write - 1)
            return 0f;
        int index = (int)position;
        float frac = (float)(position - index);
        float a = _buffer[index & _mask];
        float b = _buffer[(index + 1) & _mask];
        return a + (b - a) * frac;
    }
}
