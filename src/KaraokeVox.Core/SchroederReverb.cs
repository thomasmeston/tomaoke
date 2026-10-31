namespace KaraokeVox;

public sealed class SchroederReverb
{
    readonly Comb[] _combs;
    readonly Allpass[] _allpass;
    float _feedback = 0.6f;
    float _damp = 0.3f;

    public SchroederReverb(int sampleRate)
    {
        ReadOnlySpan<int> combTuning = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
        ReadOnlySpan<int> allpassTuning = [556, 441, 341, 225];
        double scale = sampleRate / 44100.0;
        _combs = new Comb[combTuning.Length];
        for (int i = 0; i < combTuning.Length; i++)
            _combs[i] = new Comb(Math.Max(8, (int)(combTuning[i] * scale)));
        _allpass = new Allpass[allpassTuning.Length];
        for (int i = 0; i < allpassTuning.Length; i++)
            _allpass[i] = new Allpass(Math.Max(8, (int)(allpassTuning[i] * scale)));
        SetSize(0.45f);
    }

    public void SetSize(float size)
    {
        size = Math.Clamp(size, 0f, 1f);
        _feedback = 0.35f + size * 0.55f;
        _damp = 0.55f - size * 0.4f;
    }

    public float Process(float input)
    {
        float mixed = 0f;
        float x = input * 0.12f;
        for (int i = 0; i < _combs.Length; i++)
            mixed += _combs[i].Process(x, _feedback, _damp);
        for (int i = 0; i < _allpass.Length; i++)
            mixed = _allpass[i].Process(mixed);
        if (mixed > 2f)
            return 2f;
        if (mixed < -2f)
            return -2f;
        return mixed;
    }

    sealed class Comb
    {
        readonly float[] _buffer;
        int _index;
        float _filter;

        public Comb(int length) => _buffer = new float[length];

        public float Process(float input, float feedback, float damp)
        {
            float output = _buffer[_index];
            _filter = output * (1f - damp) + _filter * damp;
            _buffer[_index] = input + _filter * feedback;
            _index++;
            if (_index >= _buffer.Length)
                _index = 0;
            return output;
        }
    }

    sealed class Allpass
    {
        readonly float[] _buffer;
        int _index;

        public Allpass(int length) => _buffer = new float[length];

        public float Process(float input)
        {
            float buffered = _buffer[_index];
            float output = -input + buffered;
            _buffer[_index] = input + buffered * 0.5f;
            _index++;
            if (_index >= _buffer.Length)
                _index = 0;
            return output;
        }
    }
}
