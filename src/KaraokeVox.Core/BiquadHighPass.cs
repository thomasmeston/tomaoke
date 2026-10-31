namespace KaraokeVox;

public sealed class BiquadHighPass
{
    float _b0, _b1, _b2, _a1, _a2;
    float _x1, _x2, _y1, _y2;

    public BiquadHighPass(int sampleRate, float frequencyHz = 90f)
    {
        Set(sampleRate, frequencyHz);
    }

    public void Set(int sampleRate, float frequencyHz)
    {
        double w0 = 2 * Math.PI * frequencyHz / sampleRate;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2 * 0.7071067811865476);
        double b0 = (1 + cos) / 2;
        double b1 = -(1 + cos);
        double b2 = (1 + cos) / 2;
        double a0 = 1 + alpha;
        double a1 = -2 * cos;
        double a2 = 1 - alpha;
        _b0 = (float)(b0 / a0);
        _b1 = (float)(b1 / a0);
        _b2 = (float)(b2 / a0);
        _a1 = (float)(a1 / a0);
        _a2 = (float)(a2 / a0);
    }

    public float Process(float x)
    {
        float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return y;
    }
}
