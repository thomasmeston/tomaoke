namespace KaraokeVox;

public sealed class DspChain
{
    readonly int _sampleRate;
    readonly BiquadHighPass _highPass;
    readonly NoiseGate _gate;
    readonly PitchShifter _pitch;
    readonly PitchShifter _harmony;
    readonly TightDouble _tight;
    readonly EchoDelay _echo;
    readonly SchroederReverb _reverb;
    readonly float[] _scratch;
    readonly float[] _harm;
    readonly PitchRibbonRing? _ribbon;
    bool _harmonyLive;
    float _harmonyMix;
    float _doubleMix;
    float _micGain = 1f;
    float _heldIn;
    float _heldOut;
    int _inBits;
    int _outBits;

    public DspChain(int sampleRate, PitchRibbonRing? ribbon = null)
    {
        _sampleRate = sampleRate;
        _ribbon = ribbon;
        _highPass = new BiquadHighPass(sampleRate, 90f);
        _gate = new NoiseGate(sampleRate);
        _pitch = new PitchShifter(sampleRate);
        _harmony = new PitchShifter(sampleRate);
        _tight = new TightDouble(sampleRate);
        _echo = new EchoDelay(sampleRate);
        _reverb = new SchroederReverb(sampleRate);
        _scratch = new float[16384];
        _harm = new float[16384];
    }

    public PitchShifter Pitch => _pitch;

    public float InputPeak => BitConverter.Int32BitsToSingle(Volatile.Read(ref _inBits));

    public float OutputPeak => BitConverter.Int32BitsToSingle(Volatile.Read(ref _outBits));

    public void Process(
        ReadOnlySpan<float> input,
        Span<float> output,
        in ControlSnapshot controls,
        MelodyContour? contour,
        double songTimeSec,
        double latencySec,
        double liveRibbonSec)
    {
        if (input.Length > _scratch.Length)
        {
            int offset = 0;
            while (offset < input.Length)
            {
                int n = Math.Min(_scratch.Length, input.Length - offset);
                double time = songTimeSec < 0 ? -1 : songTimeSec + offset / (double)_sampleRate;
                Process(input.Slice(offset, n), output.Slice(offset, n), controls, contour, time, latencySec, liveRibbonSec);
                offset += n;
            }
            return;
        }

        int count = input.Length;
        float blockPeak = 0f;
        float gainAlpha = 1f - MathF.Exp(-1f / (_sampleRate * 0.02f));
        for (int i = 0; i < count; i++)
        {
            _micGain += (controls.MicGain - _micGain) * gainAlpha;
            float x = _highPass.Process(input[i] * _micGain);
            x = _gate.Process(x, controls.GateThresholdDb);
            _scratch[i] = x;
            float ax = MathF.Abs(x);
            if (ax > blockPeak)
                blockPeak = ax;
        }

        if (controls.TuneBypass)
        {
            _scratch.AsSpan(0, count).CopyTo(output[..count]);
        }
        else
        {
            _pitch.Process(_scratch.AsSpan(0, count), output[..count], controls, contour, songTimeSec, latencySec);
            float blend = Math.Clamp(controls.Strength, 0f, 1f);
            if (blend <= 0.001f)
                _scratch.AsSpan(0, count).CopyTo(output[..count]);
            else if (blend < 0.999f)
            {
                for (int i = 0; i < count; i++)
                    output[i] = _scratch[i] * (1f - blend) + output[i] * blend;
            }
        }

        MixVoices(count, output[..count], controls);
        _reverb.SetSize(controls.ReverbSize);
        int delay = Math.Clamp((int)(controls.EchoDelayMs * _sampleRate / 1000f), 1, _sampleRate);
        float outPeak = 0f;
        for (int i = 0; i < count; i++)
        {
            float y = _echo.Process(output[i], delay, controls.EchoFeedback, controls.EchoMix);
            y += _reverb.Process(y) * Math.Clamp(controls.ReverbMix, 0f, 1f);
            output[i] = y;
            float ay = MathF.Abs(y);
            if (ay > outPeak)
                outPeak = ay;
        }

        float decay = MathF.Exp(-(count / (float)_sampleRate) / 0.15f);
        _heldIn = MathF.Max(blockPeak, _heldIn * decay);
        _heldOut = MathF.Max(outPeak, _heldOut * decay);
        Volatile.Write(ref _inBits, BitConverter.SingleToInt32Bits(_heldIn));
        Volatile.Write(ref _outBits, BitConverter.SingleToInt32Bits(_heldOut));

        PitchRibbonRing? ribbon = _ribbon;
        if (ribbon != null)
        {
            double heard = songTimeSec < 0
                ? liveRibbonSec
                : songTimeSec + count / (double)_sampleRate - latencySec + controls.SyncTrimMs / 1000.0;
            ribbon.Push(heard, _pitch.DetectedHz, _pitch.TargetHz, _pitch.Voiced);
        }
    }

    void MixVoices(int count, Span<float> lead, in ControlSnapshot controls)
    {
        float mixAlpha = 1f - MathF.Exp(-1f / (_sampleRate * 0.03f));
        bool wantHarmony = controls.HarmonyMix > 0.01f && _pitch.Voiced && _pitch.TargetHz > 20f;
        if (wantHarmony)
        {
            if (!_harmonyLive)
            {
                _harmony.Reset();
                _harmonyLive = true;
            }
            float retune = controls.Voice == VoicePreset.Robot ? 0f : Math.Max(0f, controls.RetuneMs);
            float third = PitchMath.DiatonicStep(_pitch.TargetHz, controls.KeyRoot, controls.Scale, 2);
            _harmony.ProcessToward(_scratch.AsSpan(0, count), _harm.AsSpan(0, count), third, retune);
        }
        else
        {
            _harmonyLive = false;
            _harm.AsSpan(0, count).Clear();
        }

        for (int i = 0; i < count; i++)
        {
            _harmonyMix += (controls.HarmonyMix - _harmonyMix) * mixAlpha;
            _doubleMix += (controls.DoubleMix - _doubleMix) * mixAlpha;
            float doubled = _tight.Process(lead[i]);
            lead[i] += _harm[i] * _harmonyMix * 0.65f + doubled * _doubleMix * 0.55f;
        }
    }
}
