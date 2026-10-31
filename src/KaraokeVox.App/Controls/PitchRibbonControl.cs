using System.Windows;
using System.Windows.Media;
using KaraokeVox;

namespace KaraokeVox.App.Controls;

public sealed class PitchRibbonControl : FrameworkElement
{
    public const double WindowSec = 4.0;
    public const double RangeCents = 700.0;

    static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(40, 168, 176, 194)), 1));
    static readonly Pen LeadPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)), 2.5));
    static readonly Pen OnPitchPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xF2, 0xC1, 0x4E)), 2.5));
    static readonly Pen FlatPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x5B, 0x8B, 0xD4)), 2.5));
    static readonly Pen SharpPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0x6A, 0x6A)), 2.5));
    static readonly Pen GapOnPitchPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(140, 0xF2, 0xC1, 0x4E)), 1.5));
    static readonly Pen GapFlatPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(140, 0x5B, 0x8B, 0xD4)), 1.5));
    static readonly Pen GapSharpPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(140, 0xE8, 0x6A, 0x6A)), 1.5));

    readonly PitchRibbonPoint[] _scratch = new PitchRibbonPoint[PitchRibbonRing.Capacity];
    readonly Point[] _leadPoints = new Point[128];
    readonly Point[] _sungPoints = new Point[PitchRibbonRing.Capacity];
    readonly double[] _sungLeadY = new double[PitchRibbonRing.Capacity];
    readonly byte[] _sungKind = new byte[PitchRibbonRing.Capacity];
    readonly StreamGeometry _leadGeometry = new();

    double _endHeard;
    float _centerHz;
    MelodyContour? _contour;
    int _songShift;
    PitchRibbonRing? _ring;
    bool _useSongClock;

    public void SetSource(PitchRibbonRing? ring) => _ring = ring;

    public void Update(double endHeard, float centerHz, MelodyContour? contour, int songShiftSemitones, bool useSongClock)
    {
        _endHeard = endHeard;
        _centerHz = centerHz;
        _contour = contour;
        _songShift = songShiftSemitones;
        _useSongClock = useSongClock;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Rect bounds = new(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width < 8 || bounds.Height < 8)
            return;

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x22)), null, bounds);
        if (_centerHz < 20f)
            return;

        double minHeard = _endHeard - WindowSec;
        DrawGrid(dc, bounds);

        int leadCount = BuildLeadLine(minHeard, bounds);
        if (leadCount >= 2)
        {
            _leadGeometry.Clear();
            using StreamGeometryContext ctx = _leadGeometry.Open();
            ctx.BeginFigure(_leadPoints[0], false, false);
            Point[] tail = new Point[leadCount - 1];
            for (int i = 1; i < leadCount; i++)
                tail[i - 1] = _leadPoints[i];
            ctx.PolyLineTo(tail, true, false);
            dc.DrawGeometry(null, LeadPen, _leadGeometry);
        }

        int sungCount = BuildSungLine(minHeard, bounds);
        for (int i = 0; i < sungCount; i++)
        {
            double leadY = _sungLeadY[i];
            if (!double.IsNaN(leadY))
                dc.DrawLine(GapPenForKind(_sungKind[i]), new Point(_sungPoints[i].X, leadY), _sungPoints[i]);
        }

        for (int i = 1; i < sungCount; i++)
            dc.DrawLine(PenForKind(_sungKind[i - 1]), _sungPoints[i - 1], _sungPoints[i]);
        for (int i = 0; i < sungCount; i++)
            dc.DrawEllipse(BrushForKind(_sungKind[i]), null, _sungPoints[i], 3.5, 3.5);
    }

    static Pen PenForKind(byte kind) => kind switch
    {
        1 => OnPitchPen,
        2 => SharpPen,
        _ => FlatPen
    };

    static Brush BrushForKind(byte kind) => (PenForKind(kind).Brush as SolidColorBrush)!;

    static byte KindFor(float detectedHz, float leadHz)
    {
        if (detectedHz < 20f || leadHz < 20f)
            return 0;
        double cents = 1200.0 * Math.Log(detectedHz / leadHz, 2);
        if (Math.Abs(cents) <= 30.0)
            return 1;
        return cents < 0 ? (byte)0 : (byte)2;
    }

    void DrawGrid(DrawingContext dc, Rect bounds)
    {
        for (int cents = -600; cents <= 600; cents += 200)
        {
            double y = YForCents(cents, bounds);
            dc.DrawLine(GridPen, new Point(0, y), new Point(bounds.Width, y));
        }
    }

    static Pen GapPenForKind(byte kind) => kind switch
    {
        1 => GapOnPitchPen,
        2 => GapSharpPen,
        _ => GapFlatPen
    };

    float LeadHzAt(double heardSec)
    {
        if (_contour == null || !_useSongClock || !_contour.TryCenter(heardSec, out float hz, out _))
            return 0;
        if (_songShift == 0)
            return hz;
        return hz * MathF.Pow(2f, _songShift / 12f);
    }

    int BuildLeadLine(double minHeard, Rect bounds)
    {
        if (_contour == null || !_useSongClock)
            return 0;

        int count = 0;
        for (int i = 0; i < _leadPoints.Length; i++)
        {
            double t = minHeard + WindowSec * i / (_leadPoints.Length - 1.0);
            float hz = LeadHzAt(t);
            if (hz < 20f)
                continue;
            _leadPoints[count++] = new Point(XFor(t, bounds), YForHz(hz, bounds));
        }
        return count;
    }

    int BuildSungLine(double minHeard, Rect bounds)
    {
        PitchRibbonRing? ring = _ring;
        if (ring == null)
            return 0;

        int count = ring.CopyRange(minHeard, _endHeard, _scratch);
        int points = 0;
        for (int i = 0; i < count; i++)
        {
            ref readonly PitchRibbonPoint point = ref _scratch[i];
            if (!point.Voiced || point.DetectedHz < 20f)
                continue;

            float leadHz = LeadHzAt(point.HeardSec);
            double leadY = leadHz > 20f ? YForHz(leadHz, bounds) : double.NaN;
            _sungLeadY[points] = leadY;
            _sungKind[points] = KindFor(point.DetectedHz, leadHz);
            _sungPoints[points++] = new Point(XFor(point.HeardSec, bounds), YForHz(point.DetectedHz, bounds));
        }
        return points;
    }

    double XFor(double heard, Rect bounds) =>
        (heard - (_endHeard - WindowSec)) / WindowSec * bounds.Width;

    double YForHz(float hz, Rect bounds)
    {
        if (hz < 20f)
            return bounds.Height * 0.5;
        double cents = 1200.0 * Math.Log(hz / _centerHz, 2);
        return YForCents(cents, bounds);
    }

    double YForCents(double cents, Rect bounds)
    {
        double clamped = Math.Clamp(cents, -RangeCents, RangeCents);
        double mix = (RangeCents - clamped) / (2.0 * RangeCents);
        return mix * bounds.Height;
    }

    static T Freeze<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze)
            freezable.Freeze();
        return freezable;
    }
}
