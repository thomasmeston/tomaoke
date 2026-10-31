namespace KaraokeVox;

public readonly struct TrackFrame
{
    public float Left { get; init; }
    public float Right { get; init; }
    public float GuideLeft { get; init; }
    public float GuideRight { get; init; }
}

public sealed class TrackPitch
{
    const int Grain = 2048;
    const int Fade = 384;
    double _read;
    int _shift = int.MinValue;
    bool _fading;
    int _fadePos;
    double _fadeFrom;
    double _fadeTo;

    public void Reset(long frame)
    {
        _read = frame;
        _shift = int.MinValue;
        _fading = false;
    }

    public TrackFrame Read(float[] instrumental, float[]? guide, long frame, int shiftSemitones)
    {
        shiftSemitones = Math.Clamp(shiftSemitones, -3, 3);
        if (shiftSemitones == 0)
        {
            _read = frame;
            _shift = 0;
            _fading = false;
            return At(instrumental, guide, frame);
        }

        if (_shift != shiftSemitones)
        {
            _shift = shiftSemitones;
            _read = frame;
            _fading = false;
        }

        double ratio = Math.Pow(2.0, shiftSemitones / 12.0);
        TrackFrame sample;
        if (_fading)
        {
            float mix = _fadePos / (float)Fade;
            TrackFrame from = At(instrumental, guide, _fadeFrom);
            TrackFrame to = At(instrumental, guide, _fadeTo);
            sample = new TrackFrame
            {
                Left = from.Left + (to.Left - from.Left) * mix,
                Right = from.Right + (to.Right - from.Right) * mix,
                GuideLeft = from.GuideLeft + (to.GuideLeft - from.GuideLeft) * mix,
                GuideRight = from.GuideRight + (to.GuideRight - from.GuideRight) * mix
            };
            _fadeFrom += ratio;
            _fadeTo += ratio;
            _fadePos++;
            if (_fadePos >= Fade)
            {
                _fading = false;
                _read = _fadeTo;
            }
        }
        else
        {
            sample = At(instrumental, guide, _read);
            _read += ratio;
            double ahead = _read - frame;
            if (ahead > Grain)
                BeginFade(_read, _read - Grain);
            else if (ahead < -Grain)
                BeginFade(_read, _read + Grain);
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

    static TrackFrame At(float[] instrumental, float[]? guide, double frame)
    {
        return new TrackFrame
        {
            Left = Sample(instrumental, frame, 0),
            Right = Sample(instrumental, frame, 1),
            GuideLeft = guide == null ? 0f : Sample(guide, frame, 0),
            GuideRight = guide == null ? 0f : Sample(guide, frame, 1)
        };
    }

    static float Sample(float[] interleaved, double frame, int channel)
    {
        int frames = interleaved.Length / 2;
        if (frame < 0 || frame >= frames)
            return 0f;
        int index = (int)frame;
        if (index >= frames - 1)
            return interleaved[(frames - 1) * 2 + channel];
        float frac = (float)(frame - index);
        float a = interleaved[index * 2 + channel];
        float b = interleaved[(index + 1) * 2 + channel];
        return a + (b - a) * frac;
    }
}
