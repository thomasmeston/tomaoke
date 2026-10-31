namespace KaraokeVox;

public sealed class PitchShifter
{
    readonly int _sampleRate;
    readonly float[] _buffer;
    readonly float[] _analysis;
    readonly float[] _half;
    readonly float[] _cmnd;
    readonly int _mask;
    readonly int _hop;
    long _write;
    double _read;
    double _semis;
    double _fadeFrom;
    double _fadeTo;
    int _fadePos;
    int _fadeLen;
    bool _fading;
    int _sinceHop;
    int _period;
    int _detectedBits;
    int _targetBits;
    int _voicedBits;
    int _melodyTargetBits;

    public PitchShifter(int sampleRate)
    {
        _sampleRate = sampleRate;
        _buffer = new float[16384];
        _mask = _buffer.Length - 1;
        _analysis = new float[2048];
        _half = new float[_analysis.Length / 2];
        _cmnd = new float[_half.Length];
        _hop = Math.Max(1, sampleRate / 100);
        _period = Math.Max(32, sampleRate / 200);
        int min = MinDelay(_period);
        _read = -((min + min + _period) * 0.5);
    }

    public float DetectedHz
    {
        get => BitConverter.Int32BitsToSingle(Volatile.Read(ref _detectedBits));
        private set => Volatile.Write(ref _detectedBits, BitConverter.SingleToInt32Bits(value));
    }

    public float TargetHz
    {
        get => BitConverter.Int32BitsToSingle(Volatile.Read(ref _targetBits));
        private set => Volatile.Write(ref _targetBits, BitConverter.SingleToInt32Bits(value));
    }

    public bool Voiced
    {
        get => Volatile.Read(ref _voicedBits) != 0;
        private set => Volatile.Write(ref _voicedBits, value ? 1 : 0);
    }

    public bool MelodyTargetActive => Volatile.Read(ref _melodyTargetBits) != 0;

    public void Reset()
    {
        Array.Clear(_buffer);
        _write = 0;
        _sinceHop = 0;
        _fading = false;
        _semis = 0;
        _pendingSemis = 0;
        _period = Math.Max(32, _sampleRate / 200);
        int min = MinDelay(_period);
        _read = -((min + min + _period) * 0.5);
        DetectedHz = 0;
        TargetHz = 0;
        Voiced = false;
        Volatile.Write(ref _melodyTargetBits, 0);
    }

    public void Process(
        ReadOnlySpan<float> input,
        Span<float> output,
        in ControlSnapshot controls,
        MelodyContour? contour,
        double songTimeSec,
        double latencySec)
    {
        float retuneMs = controls.Voice == VoicePreset.Robot ? 0f : Math.Max(0f, controls.RetuneMs);
        Render(input, output, controls, contour, songTimeSec, latencySec, retuneMs, 0f);
    }

    public void ProcessToward(ReadOnlySpan<float> input, Span<float> output, float targetHz, float retuneMs)
    {
        Render(input, output, default, null, -1, 0, Math.Max(0f, retuneMs), targetHz);
    }

    void Render(
        ReadOnlySpan<float> input,
        Span<float> output,
        in ControlSnapshot controls,
        MelodyContour? contour,
        double songTimeSec,
        double latencySec,
        float retuneMs,
        float fixedTargetHz)
    {
        for (int i = 0; i < input.Length; i++)
        {
            _buffer[(int)(_write & _mask)] = input[i];
            _write++;
            _sinceHop++;

            if (_sinceHop >= _hop)
            {
                _sinceHop = 0;
                if (fixedTargetHz > 0f)
                {
                    bool voiced = Analyze();
                    if (voiced && DetectedHz > 20f)
                    {
                        _pendingSemis = (float)TuneResolver.Fold(12.0 * Math.Log(fixedTargetHz / DetectedHz, 2));
                        TargetHz = fixedTargetHz;
                        _blockRetuneMs = retuneMs;
                    }
                    else
                    {
                        _pendingSemis = 0f;
                        _blockRetuneMs = 12f;
                    }
                }
                else
                {
                    bool voiced = Analyze();
                    double heard = songTimeSec < 0
                        ? -1
                        : songTimeSec + i / (double)_sampleRate - latencySec + controls.SyncTrimMs / 1000.0;
                    TuneDecision decision = TuneResolver.Decide(controls, contour, heard, DetectedHz, voiced);
                    _pendingSemis = voiced ? decision.Semitones : 0f;
                    TargetHz = decision.TargetHz;
                    _blockRetuneMs = voiced ? RetuneForStrength(retuneMs, controls.Strength) : 12f;
                    bool melodyTarget = controls.Mode == TuneMode.MelodyLock
                        && contour != null
                        && heard >= 0
                        && voiced
                        && decision.Active
                        && decision.TargetHz > 20f;
                    Volatile.Write(ref _melodyTargetBits, melodyTarget ? 1 : 0);
                }
            }

            output[i] = Pull();
        }
    }

    float Pull()
    {
        float tauMs = _blockRetuneMs;
        double alpha = tauMs <= 0.5f
            ? 1.0
            : 1.0 - Math.Exp(-1.0 / (_sampleRate * (tauMs / 1000.0)));
        _semis += (_pendingSemis - _semis) * alpha;
        double ratio = Math.Pow(2.0, _semis / 12.0);
        if (double.IsNaN(ratio) || ratio < 0.5)
            ratio = 0.5;
        if (ratio > 2.0)
            ratio = 2.0;

        float sample;
        if (_fading)
        {
            float mix = _fadeLen <= 1 ? 1f : _fadePos / (float)_fadeLen;
            float from = ReadAt(_fadeFrom);
            float to = ReadAt(_fadeTo);
            sample = from + (to - from) * mix;
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
            MaybeJump();
        }
        return sample;
    }

    float _pendingSemis;
    float _blockRetuneMs = 40f;

    bool Analyze()
    {
        if (_write < _analysis.Length)
        {
            DetectedHz = 0;
            Voiced = false;
            return false;
        }

        long start = _write - _analysis.Length;
        float mean = 0f;
        for (int i = 0; i < _analysis.Length; i++)
        {
            _analysis[i] = _buffer[(int)((start + i) & _mask)];
            mean += _analysis[i];
        }
        mean /= _analysis.Length;
        double energy = 0;
        for (int i = 0; i < _analysis.Length; i++)
        {
            _analysis[i] -= mean;
            energy += _analysis[i] * _analysis[i];
        }
        float rms = (float)Math.Sqrt(energy / _analysis.Length);
        if (rms < 0.004f)
        {
            DetectedHz = 0;
            Voiced = false;
            return false;
        }

        for (int i = 0; i < _half.Length; i++)
            _half[i] = 0.5f * (_analysis[i * 2] + _analysis[i * 2 + 1]);

        int halfRate = _sampleRate / 2;
        int minLag = Math.Max(2, halfRate / 1000);
        int maxLag = Math.Min(_half.Length - 2, halfRate / 70);
        Difference(_half, _cmnd, maxLag);

        int best = -1;
        for (int tau = minLag; tau <= maxLag; tau++)
        {
            if (_cmnd[tau] >= 0.15f)
                continue;
            while (tau + 1 <= maxLag && _cmnd[tau + 1] < _cmnd[tau])
                tau++;
            best = tau;
            break;
        }

        if (best < 0 || _cmnd[best] > 0.2f)
        {
            DetectedHz = 0;
            Voiced = false;
            return false;
        }

        float refinedHalf = Parabolic(best, maxLag);
        int estimate = Math.Clamp((int)Math.Round(refinedHalf * 2.0), _sampleRate / 1000, _sampleRate / 70);
        int full = RefineFull(estimate);
        if (full < 8)
        {
            DetectedHz = 0;
            Voiced = false;
            return false;
        }

        _period = full;
        DetectedHz = _sampleRate / (float)full;
        Voiced = true;
        return true;
    }

    int RefineFull(int estimate)
    {
        int min = Math.Max(8, _sampleRate / 1000);
        int max = Math.Min(_analysis.Length / 2, _sampleRate / 70);
        int startLag = Math.Max(min, estimate - 6);
        int endLag = Math.Min(max, estimate + 6);
        int win = Math.Min(1024, _analysis.Length);
        int best = estimate;
        float bestScore = float.MaxValue;
        for (int tau = startLag; tau <= endLag; tau++)
        {
            float score = 0f;
            int len = win - tau;
            int origin = _analysis.Length - win;
            for (int i = 0; i < len; i += 2)
            {
                float d = _analysis[origin + i] - _analysis[origin + i + tau];
                score += d * d;
            }
            if (score < bestScore)
            {
                bestScore = score;
                best = tau;
            }
        }
        return best;
    }

    float Parabolic(int best, int maxLag)
    {
        if (best <= 0 || best >= maxLag)
            return best;
        float s0 = _cmnd[best - 1];
        float s1 = _cmnd[best];
        float s2 = _cmnd[best + 1];
        float denom = 2f * (s0 - 2f * s1 + s2);
        if (MathF.Abs(denom) < 1e-8f)
            return best;
        return best + (s0 - s2) / denom;
    }

    static void Difference(ReadOnlySpan<float> x, Span<float> cmnd, int maxLag)
    {
        double running = 0;
        cmnd[0] = 1f;
        for (int tau = 1; tau <= maxLag; tau++)
        {
            double sum = 0;
            int len = x.Length - tau;
            for (int i = 0; i < len; i++)
            {
                float d = x[i] - x[i + tau];
                sum += d * d;
            }
            running += sum;
            cmnd[tau] = running <= 1e-12 ? 1f : (float)(sum * tau / running);
        }
    }

    void MaybeJump()
    {
        int min = MinDelay(_period);
        int max = min + Math.Max(_period, 1);
        double delay = _write - _read;
        double target = (min + max) * 0.5;
        if (delay < min && _write > _period * 4)
        {
            int jumps = Math.Max(1, (int)Math.Round((target - delay) / _period));
            BeginFade(_read, _read - jumps * _period);
        }
        else if (delay > max)
        {
            int jumps = Math.Max(1, (int)Math.Round((delay - target) / _period));
            BeginFade(_read, _read + jumps * _period);
        }
    }

    void BeginFade(double from, double to)
    {
        _fadeFrom = from;
        _fadeTo = to;
        _fadePos = 0;
        _fadeLen = Math.Clamp(_period / 2, 16, 384);
        _fading = true;
    }

    float ReadAt(double position)
    {
        if (position < 1 || position >= _write - 1)
            return 0f;
        int index = (int)position;
        float frac = (float)(position - index);
        float a = _buffer[index & _mask];
        float b = _buffer[(index + 1) & _mask];
        return a + (b - a) * frac;
    }

    static int MinDelay(int period) => Math.Max(64, (int)(period * 1.2f));

    static float RetuneForStrength(float retuneMs, float strength)
    {
        strength = Math.Clamp(strength, 0f, 1f);
        if (strength >= 0.995f)
            return 0f;
        return retuneMs * (1f - strength);
    }
}
