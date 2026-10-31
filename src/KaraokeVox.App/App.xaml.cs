using System.Windows;
using KaraokeVox.App.Services;

namespace KaraokeVox.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        YouTubePicture.PrepareLoader();
        base.OnStartup(e);
    }
}

