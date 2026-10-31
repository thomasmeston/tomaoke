using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace KaraokeVox.App.Services;

public sealed class YouTubePicture
{
    const string Host = "karaoke-vox.local";
    const double DriftSec = 0.22;

    readonly WebView2 _view;
    bool _hostReady;
    bool _playerReady;
    string? _loadedId;
    int _generation;

    public YouTubePicture(WebView2 view) => _view = view;

    public static void PrepareLoader()
    {
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => "win-x64"
        };
        string folder = Path.Combine(AppContext.BaseDirectory, "runtimes", arch, "native");
        if (!File.Exists(Path.Combine(folder, "WebView2Loader.dll")))
            return;
        try
        {
            CoreWebView2Environment.SetLoaderDllFolderPath(folder);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public bool IsLoaded => _playerReady && _loadedId != null;

    public async Task LoadAsync(string videoId)
    {
        int generation = ++_generation;
        _playerReady = false;
        await EnsureHostAsync();
        if (generation != _generation)
            return;
        _loadedId = videoId;
        _view.Source = new Uri($"https://{Host}/player.html?v={Uri.EscapeDataString(videoId)}");
    }

    public async Task SyncAsync(double heardSec, bool playing)
    {
        if (!_playerReady || _view.CoreWebView2 == null)
            return;
        string target = Math.Max(0, heardSec).ToString("0.###", CultureInfo.InvariantCulture);
        string want = playing ? "true" : "false";
        string script = $$"""
            (function(){
              if (!window.player || !player.getCurrentTime) return "wait";
              player.mute();
              var t = player.getCurrentTime();
              var target = {{target}};
              if (Math.abs(t - target) > {{DriftSec.ToString(CultureInfo.InvariantCulture)}})
                player.seekTo(target, true);
              var state = player.getPlayerState();
              if ({{want}} && state !== 1) player.playVideo();
              if (!{{want}} && state === 1) player.pauseVideo();
              return "ok";
            })()
            """;
        await _view.ExecuteScriptAsync(script);
    }

    public void Stop()
    {
        _generation++;
        _playerReady = false;
        _loadedId = null;
    }

    async Task EnsureHostAsync()
    {
        if (_hostReady)
            return;
        string dir = Path.Combine(Path.GetTempPath(), "KaraokeVoxPlayer");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "player.html"), PlayerHtml);
        string userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KaraokeVox",
            "WebView2");
        Directory.CreateDirectory(userData);
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userData);
        await _view.EnsureCoreWebView2Async(environment);
        _view.CoreWebView2.SetVirtualHostNameToFolderMapping(Host, dir, CoreWebView2HostResourceAccessKind.Allow);
        _view.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            if (e.TryGetWebMessageAsString() == "ready")
                _playerReady = true;
        };
        _view.DefaultBackgroundColor = System.Drawing.Color.Black;
        _hostReady = true;
    }

    const string PlayerHtml = """
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8" />
        <style>
          html, body { margin: 0; height: 100%; background: #000; overflow: hidden; }
          #player { width: 100%; height: 100%; }
        </style>
        </head>
        <body>
        <div id="player"></div>
        <script>
          var params = new URLSearchParams(location.search);
          var videoId = params.get("v");
          var tag = document.createElement("script");
          tag.src = "https://www.youtube.com/iframe_api";
          document.body.appendChild(tag);
          var player;
          function onYouTubeIframeAPIReady() {
            player = new YT.Player("player", {
              width: "100%",
              height: "100%",
              videoId: videoId,
              playerVars: {
                autoplay: 0,
                controls: 0,
                disablekb: 1,
                fs: 0,
                modestbranding: 1,
                rel: 0,
                playsinline: 1,
                origin: "https://karaoke-vox.local"
              },
              events: {
                onReady: function () {
                  player.mute();
                  window.chrome.webview.postMessage("ready");
                }
              }
            });
          }
        </script>
        </body>
        </html>
        """;
}
