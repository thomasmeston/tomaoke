namespace KaraokeVox;

public sealed class NoiseGate
{
    readonly float _attack;
    readonly float _release;
    float _env;
    float _gain = 1f;
    float _lastDb = float.NaN;
    float _openLevel = 0.01f;
    float _closeLevel = 0.005f;
    bool _open = true;

    public NoiseGate(int sampleRate)
    {
        _attack = Coeff(sampleRate, 0.004f);
        _release = Coeff(sampleRate, 0.040f);
    }

    public float Process(float x, float thresholdDb)
    {
        if (thresholdDb != _lastDb)
        {
            _lastDb = thresholdDb;
            _openLevel = MathF.Pow(10f, thresholdDb / 20f);
            _closeLevel = _openLevel * 0.5f;
        }

        float level = MathF.Abs(x);
        _env += (level - _env) * (level > _env ? _attack : _release);
        if (_open)
        {
            if (_env < _closeLevel)
                _open = false;
        }
        else if (_env >= _openLevel)
        {
            _open = true;
        }

        float target = _open ? 1f : 0f;
        _gain += (target - _gain) * (_open ? _attack : _release);
        return x * _gain;
    }

    static float Coeff(int sampleRate, float seconds) =>
        1f - MathF.Exp(-1f / (sampleRate * seconds));
}
