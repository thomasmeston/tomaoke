namespace KaraokeVox;

public sealed class SyncTap
{
    readonly float[] _lags = new float[6];
    int _sampleRate;
    int _interval;
    int _untilClick;
    int _sinceClick;
    int _window;
    bool _waiting;
    bool _running;
    float _phase;
    float _envelope;
    float _best;
    int _bestAt;
    int _hits;

    public bool Running => _running;
    public int Hits => _hits;

    public float SuggestedTrimMs
    {
        get
        {
            if (_hits <= 0)
                return 0f;
            int count = Math.Min(_hits, _lags.Length);
            float[] copy = new float[count];
            Array.Copy(_lags, copy, count);
            Array.Sort(copy);
            return -copy[count / 2];
        }
    }

    public void Start(int sampleRate)
    {
        _sampleRate = Math.Max(1, sampleRate);
        _interval = (int)(_sampleRate * 0.8);
        _window = (int)(_sampleRate * 0.45);
        _untilClick = _sampleRate / 10;
        _sinceClick = 0;
        _waiting = false;
        _hits = 0;
        _phase = 0;
        _envelope = 0;
        _running = true;
        Array.Clear(_lags);
    }

    public void Stop() => _running = false;

    public void Tick(float mic, ref float left, ref float right)
    {
        if (!_running)
            return;
        if (_envelope > 0f)
        {
            _phase += 1000f / _sampleRate;
            float click = MathF.Sin(2f * MathF.PI * _phase) * _envelope * 0.45f;
            left += click;
            right += click;
            _envelope *= 0.9985f;
            if (_envelope < 0.001f)
                _envelope = 0f;
        }

        if (_waiting)
        {
            _sinceClick++;
            float level = MathF.Abs(mic);
            int ignore = _sampleRate / 80;
            if (_sinceClick > ignore && _sinceClick < _window && level > _best)
            {
                _best = level;
                _bestAt = _sinceClick;
            }
            if (_sinceClick >= _window)
            {
                if (_best > 0.05f)
                {
                    _lags[_hits % _lags.Length] = _bestAt * 1000f / _sampleRate;
                    _hits++;
                }
                _waiting = false;
            }
        }

        _untilClick--;
        if (_untilClick <= 0)
        {
            _untilClick = _interval;
            _envelope = 1f;
            _phase = 0f;
            _waiting = true;
            _sinceClick = 0;
            _best = 0f;
            _bestAt = 0;
        }
    }
}
