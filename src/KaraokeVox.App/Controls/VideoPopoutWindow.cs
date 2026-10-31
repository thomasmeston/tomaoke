using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace KaraokeVox.App.Controls;

public sealed class VideoPopoutWindow : Window
{
    bool _allowClose;
    bool _fullscreen;
    Rect _restore = new(80, 80, 960, 600);

    public VideoPopoutWindow()
    {
        Title = "Tomaoke video";
        Width = 960;
        Height = 600;
        MinWidth = 480;
        MinHeight = 320;
        Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x16));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        Content = Host;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _fullscreen)
            {
                EscapePressed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.Key == Key.F11)
            {
                FullScreenKeyPressed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        };
    }

    public Grid Host { get; } = new() { Background = Brushes.Black };

    public event EventHandler? DockRequested;

    public event EventHandler? EscapePressed;

    public event EventHandler? FullScreenKeyPressed;

    public Rect RestorableBounds => _fullscreen ? _restore : new Rect(Left, Top, Width, Height);

    public void EnterFullScreen()
    {
        if (_fullscreen)
            return;
        _restore = new Rect(Left, Top, Width, Height);
        _fullscreen = true;
        Rect bounds = MonitorDipBounds();
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        Topmost = true;
    }

    public void ExitFullScreen()
    {
        if (!_fullscreen)
            return;
        _fullscreen = false;
        Topmost = false;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        WindowState = WindowState.Normal;
        Left = _restore.Left;
        Top = _restore.Top;
        Width = Math.Max(MinWidth, _restore.Width);
        Height = Math.Max(MinHeight, _restore.Height);
    }

    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(() => DockRequested?.Invoke(this, EventArgs.Empty));
            return;
        }

        base.OnClosing(e);
    }

    Rect MonitorDipBounds()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);

        nint monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);

        double scaleX = 1;
        double scaleY = 1;
        PresentationSource? source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            scaleX = source.CompositionTarget.TransformToDevice.M11;
            scaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        if (scaleX <= 0)
            scaleX = 1;
        if (scaleY <= 0)
            scaleY = 1;

        RectPixels pixels = info.rcMonitor;
        return new Rect(
            pixels.Left / scaleX,
            pixels.Top / scaleY,
            (pixels.Right - pixels.Left) / scaleX,
            (pixels.Bottom - pixels.Top) / scaleY);
    }

    [DllImport("user32.dll")]
    static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    struct RectPixels
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct MonitorInfo
    {
        public int cbSize;
        public RectPixels rcMonitor;
        public RectPixels rcWork;
        public uint dwFlags;
    }
}
