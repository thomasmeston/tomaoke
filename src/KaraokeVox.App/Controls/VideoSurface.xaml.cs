using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace KaraokeVox.App.Controls;

public partial class VideoSurface : UserControl
{
    public VideoSurface()
    {
        InitializeComponent();
        PopButton.Click += (_, _) => PopRequested?.Invoke(this, EventArgs.Empty);
        FullButton.Click += (_, _) => FullScreenRequested?.Invoke(this, EventArgs.Empty);
    }

    public WebView2 View => Player;

    public event EventHandler? PopRequested;

    public event EventHandler? FullScreenRequested;

    public void SetChrome(bool awayFromStage, bool fullscreen)
    {
        PopButton.Content = awayFromStage ? "Dock" : "Pop out";
        FullButton.Content = fullscreen ? "Exit full screen" : "Full screen";
        ResizeHint.Visibility = awayFromStage ? Visibility.Collapsed : Visibility.Visible;
    }
}
