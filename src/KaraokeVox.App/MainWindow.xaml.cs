using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KaraokeVox;
using KaraokeVox.App.Audio;
using KaraokeVox.App.Controls;
using KaraokeVox.App.Services;
using Microsoft.Win32;

namespace KaraokeVox.App;

public partial class MainWindow : Window
{
    readonly AudioEngine _engine = new();
    readonly MelodyPrep _prep = new();
    readonly DispatcherTimer _timer;
    static readonly SolidColorBrush LyricSung = Freeze(Color.FromRgb(0xF4, 0xF1, 0xEA));
    static readonly SolidColorBrush LyricActive = Freeze(Color.FromRgb(0xF2, 0xC1, 0x4E));
    static readonly SolidColorBrush LyricWaiting = Freeze(Color.FromRgb(0x8B, 0x93, 0xA7));
    static readonly SolidColorBrush LyricQuiet = Freeze(Color.FromRgb(0xA8, 0xB0, 0xC2));
    static readonly SolidColorBrush PlaylistIdle = Freeze(Color.FromRgb(0x2A, 0x31, 0x44));
    static readonly SolidColorBrush PlaylistHot = Freeze(Color.FromRgb(0xF2, 0xC1, 0x4E));

    PreparedSong? _prepared;
    YouTubePicture? _picture;
    VideoPopoutWindow? _popout;
    VideoPlace _videoPlace = VideoPlace.Hidden;
    bool _returnFullToPop;
    bool _shuttingDown;
    double _videoHeight = 260;
    Rect? _popBounds;
    int _videoTick;
    bool _videoSyncing;
    LyricTrack? _lyrics;
    LyricLine? _shownPrevious;
    LyricLine? _shownCurrent;
    LyricLine? _shownNext;
    int _shownWord = int.MinValue;
    readonly ObservableCollection<PlaylistItem> _playlist = new();
    readonly SemaphoreSlim _prepGate = new(1, 1);
    CancellationTokenSource _prepCts = new();
    CancellationTokenSource _queueCts = new();
    bool _queuePaused;
    int _queuePump;
    bool _songEndedArmed;
    bool _advancing;
    bool _holdForNext;
    PlaylistItem? _playingItem;
    PlaylistItem? _playWhenReady;
    bool _suppress;
    bool _updatingSeek;
    int _loadGeneration;

    public MainWindow()
    {
        InitializeComponent();
        FillCombos();
        Wire();
        _engine.Failed += message => Dispatcher.BeginInvoke(() => StatusText.Text = message);
        PitchRibbon.SetSource(_engine.PitchRibbon);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => RefreshMeters();
        Loaded += (_, _) => StartUp();
        VideoSurface.PopRequested += (_, _) =>
        {
            if (_videoPlace is VideoPlace.Popped or VideoPlace.Full)
                DockVideo();
            else
                PopOutVideo();
        };
        VideoSurface.FullScreenRequested += (_, _) =>
        {
            if (_videoPlace == VideoPlace.Full)
                ExitFullScreen();
            else
                EnterFullScreen();
        };
        DockVideoButton.Click += (_, _) => DockVideo();
        PreviewKeyDown += OnVideoKey;
        Closed += (_, _) =>
        {
            _shuttingDown = true;
            _prepCts.Cancel();
            _queueCts.Cancel();
            _timer.Stop();
            _picture?.Stop();
            PlaceVideo(VideoHost);
            _popout?.CloseForReal();
            _popout = null;
            _engine.Dispose();
            VideoSurface.View.Dispose();
        };
    }

    void FillCombos()
    {
        ModeBox.ItemsSource = new[] { "Scale", "Melody lock", "Nearest note", "Phrase palette" };
        KeyBox.ItemsSource = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        ScaleBox.ItemsSource = new[] { "Major", "Natural minor", "Major pentatonic", "Minor pentatonic", "Chromatic" };
        VoiceBox.ItemsSource = new[] { "Normal", "Lower", "Higher", "Robot (instant retune)" };
        PresetBox.ItemsSource = new[] { "Custom", "Natural", "Party", "Robot" };
        ModeBox.SelectedIndex = 0;
        KeyBox.SelectedIndex = 0;
        ScaleBox.SelectedIndex = 0;
        VoiceBox.SelectedIndex = 0;
        PresetBox.SelectedIndex = 0;
    }

    void Wire()
    {
        foreach (Slider slider in new Slider[]
                 {
                     Strength, Retune, Gate, EchoDelay, EchoFeedback, EchoMix,
                     ReverbSize, ReverbMix, Harmony, DoubleMix
                 })
        {
            slider.ValueChanged += (_, _) =>
            {
                MarkCustom();
                UpdateLabels();
                Publish();
            };
        }
        foreach (Slider slider in new Slider[] { SyncTrim, MicGain, MusicGain, GuideGain, MasterGain, SongShift })
        {
            slider.ValueChanged += (_, _) =>
            {
                UpdateLabels();
                Publish();
            };
        }
        ModeBox.SelectionChanged += (_, _) => Publish();
        KeyBox.SelectionChanged += (_, _) => Publish();
        ScaleBox.SelectionChanged += (_, _) => Publish();
        VoiceBox.SelectionChanged += (_, _) =>
        {
            MarkCustom();
            Publish();
        };
        PresetBox.SelectionChanged += (_, _) => ApplyPreset();
        SyncTapButton.Click += (_, _) => ToggleSyncTap();
        TuneBypass.Checked += (_, _) => Publish();
        TuneBypass.Unchecked += (_, _) => Publish();
        InputDevice.SelectionChanged += (_, _) =>
        {
            if (!_suppress)
                Restart();
        };
        OutputDevice.SelectionChanged += (_, _) =>
        {
            if (_suppress)
                return;
            if (OutputDevice.SelectedItem is DeviceChoice device)
                SyncTrim.Value = device.IsBluetooth ? -180 : 0;
            Restart();
        };
        MonitorButton.Click += (_, _) =>
        {
            if (_engine.IsRunning)
            {
                _engine.Stop();
                MonitorButton.Content = "Start";
                StatusText.Text = "Monitoring stopped.";
            }
            else
            {
                Restart();
            }
        };
        LoadFileButton.Click += async (_, _) => await LoadFile();
        FetchButton.Click += async (_, _) => await FetchYouTube();
        LoadRecentYouTubeButton.Click += async (_, _) => await LoadSelectedYouTube();
        RecentYouTubeList.SelectionChanged += (_, _) =>
            LoadRecentYouTubeButton.IsEnabled = RecentYouTubeList.SelectedItem is YouTubeCacheEntry;
        RecentYouTubeList.MouseDoubleClick += async (_, _) => await LoadSelectedYouTube();
        PlayButton.Click += (_, _) => TogglePlay();
        PlaylistList.ItemsSource = _playlist;
        _playlist.CollectionChanged += (_, _) =>
            PlaylistEmpty.Visibility = _playlist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PlaylistDrop.PreviewDragOver += PlaylistDragOver;
        PlaylistDrop.PreviewDrop += PlaylistDropUrls;
        PlaylistDrop.DragLeave += PlaylistDragLeave;
        PlaylistList.PreviewDragOver += PlaylistDragOver;
        PlaylistList.PreviewDrop += PlaylistDropUrls;
        PlaylistPlayButton.Click += async (_, _) => await PlaySelectedAsync();
        PlaylistRemoveButton.Click += (_, _) => RemoveSelected();
        PlaylistClearButton.Click += (_, _) => ClearPlaylist();
        PlaylistList.MouseDoubleClick += async (_, _) => await PlaySelectedAsync();
        Seek.ValueChanged += (_, _) =>
        {
            if (!_updatingSeek)
                _engine.SeekSeconds(Seek.Value);
        };
        UpdateLabels();
    }

    void StartUp()
    {
        _timer.Start();
        RefreshRecentYouTubeList();
        LoadDevices();
    }

    void LoadDevices()
    {
        _suppress = true;
        try
        {
            var (inputs, outputs, defaultIn, defaultOut) = DeviceList.Query();
            FillDeviceBox(InputDevice, inputs, defaultIn);
            FillDeviceBox(OutputDevice, outputs, defaultOut);
            if (OutputDevice.SelectedItem is DeviceChoice output && output.IsBluetooth)
                SyncTrim.Value = -180;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            _suppress = false;
        }
        Restart();
    }

    static void FillDeviceBox(ComboBox box, IReadOnlyList<DeviceChoice> devices, string? preferredId)
    {
        box.Items.Clear();
        foreach (DeviceChoice device in devices)
            box.Items.Add(device);
        int index = 0;
        for (int i = 0; i < devices.Count; i++)
        {
            if (devices[i].Id == preferredId)
                index = i;
        }
        if (box.Items.Count > 0)
            box.SelectedIndex = index;
    }

    void Restart()
    {
        try
        {
            if (InputDevice.SelectedItem is not DeviceChoice input || OutputDevice.SelectedItem is not DeviceChoice output)
            {
                StatusText.Text = "No audio device is available.";
                MonitorButton.Content = "Start";
                return;
            }
            _engine.Start(input.Id, output.Id, output.IsBluetooth);
            Publish();
            MonitorButton.Content = "Stop";
            StatusText.Text = output.IsHandsFree
                ? "This output looks like a Bluetooth hands-free device. Pick the Stereo endpoint for music quality."
                : output.IsBluetooth
                    ? "Monitoring through Bluetooth. Your voice will feel late. The headphone jack is the low-latency path."
                    : "Monitoring. Use the headphone jack when you want to hear yourself in time.";
            if (_prepared != null)
                _ = UsePrepared(_prepared, applyMelodyDefaults: false);
        }
        catch (Exception ex)
        {
            try
            {
                _engine.Stop();
            }
            catch
            {
            }
            MonitorButton.Content = "Start";
            StatusText.Text = ex.Message;
        }
    }

    async Task LoadFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Audio|*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma|All files|*.*"
        };
        if (dialog.ShowDialog() != true)
            return;
        string path = dialog.FileName;
        await RunPrep((progress, token) => _prep.PrepareFileAsync(path, progress, token));
    }

    async Task FetchYouTube()
    {
        string url = YouTubeBox.Text.Trim();
        if (url.Length == 0)
        {
            PrepText.Text = "Paste a YouTube URL first.";
            return;
        }
        await RunPrep((progress, token) => _prep.PrepareYouTubeAsync(url, progress, token));
    }

    async Task LoadSelectedYouTube()
    {
        if (RecentYouTubeList.SelectedItem is not YouTubeCacheEntry entry)
        {
            PrepText.Text = "Pick a recent YouTube song first.";
            return;
        }
        YouTubeBox.Text = entry.Url;
        await RunPrep(
            (progress, token) => _prep.PrepareYouTubeAsync(entry.Url, progress, token),
            applyMelodyDefaults: false);
    }

    void RefreshRecentYouTubeList()
    {
        RecentYouTubeList.ItemsSource = _prep.ListYouTubeCache();
        LoadRecentYouTubeButton.IsEnabled = RecentYouTubeList.SelectedItem is YouTubeCacheEntry;
    }

    async Task RunPrep(Func<IProgress<string>, CancellationToken, Task<PreparedSong>> work, bool applyMelodyDefaults = true)
    {
        _queuePaused = true;
        _queueCts.Cancel();
        _queueCts = new CancellationTokenSource();
        _prepCts.Cancel();
        _prepCts = new CancellationTokenSource();
        CancellationToken token = _prepCts.Token;
        SetBusy(true);
        bool held = false;
        try
        {
            await _prepGate.WaitAsync(token);
            held = true;
            var progress = new Progress<string>(text => PrepText.Text = text);
            PreparedSong song = await work(progress, token);
            await UsePrepared(song, applyMelodyDefaults);
            RefreshRecentYouTubeList();
        }
        catch (OperationCanceledException)
        {
            PrepText.Text = "Prep cancelled.";
        }
        catch (Exception ex)
        {
            PrepText.Text = ex.Message;
        }
        finally
        {
            if (held)
                _prepGate.Release();
            _queuePaused = false;
            SetBusy(false);
            KickQueue();
        }
    }

    async Task UsePrepared(PreparedSong song, bool applyMelodyDefaults)
    {
        int generation = ++_loadGeneration;
        _prepared = song;
        SongTitle.Text = song.Title;
        if (_engine.SampleRate <= 0)
        {
            PrepText.Text = "Start monitoring, then the song will load.";
            return;
        }
        PrepText.Text = "Loading song…";
        int rate = _engine.SampleRate;
        SongAudio audio = await Task.Run(() => SongLoader.Load(song.InstrumentalPath, song.GuidePath, song.ContourPath, song.Title, rate));
        if (generation != _loadGeneration)
            return;
        _engine.SetSong(audio);
        double length = audio.Frames / (double)rate;
        _updatingSeek = true;
        Seek.Maximum = Math.Max(0.1, length);
        Seek.Value = 0;
        _updatingSeek = false;
        PlayButton.IsEnabled = true;
        PlayButton.Content = "Play";
        if (audio.Contour != null)
        {
            ModeBox.SelectedIndex = (int)TuneMode.MelodyLock;
            if (applyMelodyDefaults)
            {
                KeyBox.SelectedIndex = audio.Contour.KeyRoot;
                ScaleBox.SelectedIndex = (int)audio.Contour.Scale;
                Strength.Value = 85;
                Retune.Value = 25;
            }
        }
        Publish();
        ShowLyrics(song);
        ShowVideo(song);
        AttachPrepared(song);
        PrepText.Text = audio.Contour == null
            ? "Song loaded without a melody. Scale mode is available."
            : "Melody ready. Strength sets how far your voice moves toward the lead.";
    }

    void ShowVideo(PreparedSong song)
    {
        if (string.IsNullOrEmpty(song.YouTubeVideoId))
        {
            _picture?.Stop();
            HideVideo();
            return;
        }

        if (_videoPlace == VideoPlace.Hidden)
        {
            _videoPlace = VideoPlace.Docked;
            ApplyVideoLayout();
        }

        _picture ??= new YouTubePicture(VideoSurface.View);
        _ = LoadVideoAsync(song.YouTubeVideoId);
    }

    async Task LoadVideoAsync(string videoId)
    {
        try
        {
            if (_picture == null)
                return;
            await _picture.LoadAsync(videoId);
        }
        catch (Exception ex)
        {
            HideVideo();
            StatusText.Text = ex is Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException
                ? "The video panel needs the Microsoft Edge WebView2 runtime. " + ex.Message
                : "The video panel could not start. " + ex.Message;
        }
    }

    async Task SyncVideoAsync()
    {
        if (_videoSyncing || _picture is not { IsLoaded: true } || _videoPlace == VideoPlace.Hidden)
            return;
        _videoSyncing = true;
        try
        {
            double playhead = Seek.IsMouseCaptureWithin ? Seek.Value : _engine.PositionSeconds;
            double heard = playhead - _engine.LatencySeconds + SyncTrim.Value / 1000.0;
            await _picture.SyncAsync(heard, _engine.IsPlaying);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            _videoSyncing = false;
        }
    }

    void ShowLyrics(PreparedSong song)
    {
        _shownPrevious = null;
        _shownCurrent = null;
        _shownNext = null;
        _shownWord = int.MinValue;
        if (!song.FromYouTube)
        {
            _lyrics = null;
            LyricCard.Visibility = Visibility.Collapsed;
            UpdateStage();
            return;
        }
        LyricCard.Visibility = Visibility.Visible;
        UpdateStage();
        try
        {
            _lyrics = song.LyricsPath != null && File.Exists(song.LyricsPath)
                ? LyricTrack.Load(song.LyricsPath)
                : LyricTrack.Empty;
        }
        catch
        {
            _lyrics = LyricTrack.Empty;
        }
        UpdateLyrics();
    }

    void TogglePlay()
    {
        if (_engine.IsPlaying)
        {
            _engine.Pause();
            PlayButton.Content = "Play";
            _songEndedArmed = false;
            return;
        }
        if (_holdForNext || _playWhenReady != null)
        {
            _ = AdvanceAsync();
            return;
        }
        _engine.Play();
        PlayButton.Content = "Pause";
        _songEndedArmed = true;
        NotePlaylistPlayback();
    }

    void RefreshMeters()
    {
        MicMeter.Value = Math.Clamp(_engine.InputPeak * 100.0, 0, 100);
        OutMeter.Value = Math.Clamp(_engine.OutputPeak * 100.0, 0, 100);
        if (!_engine.PitchVoiced)
        {
            PitchText.Text = "Pitch  —  (sing or hum louder if the mic meter is quiet)";
        }
        else
        {
            PitchText.Text = $"{PitchMath.Format(_engine.DetectedHz)}   →   {PitchMath.Format(_engine.TargetHz)}";
            if (_engine.SongContour != null
                && ModeBox.SelectedIndex == (int)TuneMode.MelodyLock
                && !_engine.MelodyTargetActive)
            {
                PitchText.Text += "  ·  no lead target here (instrumental, sync, or pause?)";
            }
        }
        double lookback = _engine.LatencySeconds * 1000.0 - SyncTrim.Value;
        SyncHint.Text = _engine.SyncTapRunning
            ? $"Tap sync {_engine.SyncTapHits}/4. Tap the mic, or the table in front of it, on each click."
            : $"Melody looks back {lookback:0} ms. Nudge sync if notes feel early or late.";
        if (_engine.SyncTapRunning && _engine.SyncTapHits >= 4)
        {
            _suppress = true;
            SyncTrim.Value = Math.Clamp(_engine.SyncTapTrimMs, SyncTrim.Minimum, SyncTrim.Maximum);
            _suppress = false;
            _engine.StopSyncTap();
            SyncTapButton.Content = "Tap sync";
            Publish();
            StatusText.Text = $"Sync trim set to {SyncTrim.Value:0} ms from the taps.";
        }
        UpdateLyrics();
        UpdatePitchRibbon();
        if (++_videoTick % 4 == 0)
            _ = SyncVideoAsync();
        if (_engine.IsPlaying)
            _songEndedArmed = true;
        if (_engine.LengthSeconds <= 0 || Seek.IsMouseCaptureWithin)
            return;
        _updatingSeek = true;
        Seek.Maximum = Math.Max(Seek.Maximum, _engine.LengthSeconds);
        Seek.Value = Math.Clamp(_engine.PositionSeconds, Seek.Minimum, Seek.Maximum);
        _updatingSeek = false;
        TimeText.Text = $"{Clock(_engine.PositionSeconds)} / {Clock(_engine.LengthSeconds)}";
        if (_songEndedArmed && !_engine.IsPlaying && _engine.PositionSeconds >= _engine.LengthSeconds - 0.08)
        {
            _songEndedArmed = false;
            PlayButton.Content = "Play";
            _ = AdvanceAsync();
        }
    }

    void Publish()
    {
        if (_suppress || ModeBox.SelectedIndex < 0 || KeyBox.SelectedIndex < 0 || ScaleBox.SelectedIndex < 0 || VoiceBox.SelectedIndex < 0)
            return;
        _engine.SetControls(ReadControls());
    }

    ControlSnapshot ReadControls() => new()
    {
        MicGain = Percent(MicGain),
        MusicGain = Percent(MusicGain),
        GuideGain = Percent(GuideGain),
        MasterGain = Percent(MasterGain),
        TuneBypass = TuneBypass.IsChecked == true,
        GateThresholdDb = (float)Gate.Value,
        EchoDelayMs = (float)EchoDelay.Value,
        EchoFeedback = Percent(EchoFeedback),
        EchoMix = Percent(EchoMix),
        ReverbSize = Percent(ReverbSize),
        ReverbMix = Percent(ReverbMix),
        Strength = Percent(Strength),
        RetuneMs = (float)Retune.Value,
        Voice = (VoicePreset)VoiceBox.SelectedIndex,
        Mode = (TuneMode)ModeBox.SelectedIndex,
        KeyRoot = KeyBox.SelectedIndex,
        Scale = (ScaleKind)ScaleBox.SelectedIndex,
        SyncTrimMs = (float)SyncTrim.Value,
        HarmonyMix = Percent(Harmony),
        DoubleMix = Percent(DoubleMix),
        SongShiftSemitones = (int)Math.Round(SongShift.Value)
    };

    void ApplyPreset()
    {
        if (_suppress || PresetBox.SelectedIndex <= 0)
            return;
        ControlSnapshot stage = PresetBox.SelectedIndex switch
        {
            1 => ControlSnapshot.Natural(),
            2 => ControlSnapshot.Party(),
            _ => ControlSnapshot.RobotStage()
        };
        _suppress = true;
        Strength.Value = stage.Strength * 100.0;
        Retune.Value = stage.RetuneMs;
        VoiceBox.SelectedIndex = (int)stage.Voice;
        ReverbMix.Value = stage.ReverbMix * 100.0;
        EchoMix.Value = stage.EchoMix * 100.0;
        EchoDelay.Value = stage.EchoDelayMs;
        EchoFeedback.Value = stage.EchoFeedback * 100.0;
        Harmony.Value = stage.HarmonyMix * 100.0;
        DoubleMix.Value = stage.DoubleMix * 100.0;
        _suppress = false;
        UpdateLabels();
        Publish();
    }

    void MarkCustom()
    {
        if (_suppress || PresetBox.SelectedIndex <= 0)
            return;
        _suppress = true;
        PresetBox.SelectedIndex = 0;
        _suppress = false;
    }

    void ToggleSyncTap()
    {
        if (_engine.SyncTapRunning)
        {
            _engine.StopSyncTap();
            SyncTapButton.Content = "Tap sync";
            StatusText.Text = "Sync tap cancelled.";
            return;
        }
        if (!_engine.IsRunning)
        {
            StatusText.Text = "Start monitoring, then tap sync.";
            return;
        }
        _engine.StartSyncTap();
        SyncTapButton.Content = "Stop tap";
        StatusText.Text = "You'll hear clicks. Tap the mic, or the table in front of it, on each click.";
    }

    void UpdateLabels()
    {
        StrengthValue.Text = $"{Strength.Value:0}%";
        RetuneValue.Text = $"{Retune.Value:0}";
        SyncValue.Text = $"{SyncTrim.Value:0}";
        HarmonyValue.Text = $"{Harmony.Value:0}%";
        DoubleValue.Text = $"{DoubleMix.Value:0}%";
        SongShiftValue.Text = $"{SongShift.Value:0}";
        GateValue.Text = $"{Gate.Value:0}";
        EchoDelayValue.Text = $"{EchoDelay.Value:0}";
        EchoFeedbackValue.Text = $"{EchoFeedback.Value:0}%";
        EchoMixValue.Text = $"{EchoMix.Value:0}%";
        ReverbSizeValue.Text = $"{ReverbSize.Value:0}%";
        ReverbMixValue.Text = $"{ReverbMix.Value:0}%";
        MicGainValue.Text = $"{MicGain.Value:0}%";
        MusicGainValue.Text = $"{MusicGain.Value:0}%";
        GuideGainValue.Text = $"{GuideGain.Value:0}%";
        MasterGainValue.Text = $"{MasterGain.Value:0}%";
    }

    void AttachPrepared(PreparedSong song)
    {
        if (string.IsNullOrEmpty(song.YouTubeVideoId))
            return;
        PlaylistItem? item = _playlist.FirstOrDefault(entry => entry.VideoId == song.YouTubeVideoId);
        if (item == null || item.State == PlaylistItemState.Playing)
            return;
        item.MarkReady(song);
    }

    void NotePlaylistPlayback()
    {
        if (_prepared?.YouTubeVideoId == null)
            return;
        PlaylistItem? item = _playlist.FirstOrDefault(entry => entry.VideoId == _prepared.YouTubeVideoId);
        if (item != null)
            MarkPlaying(item);
    }

    void MarkPlaying(PlaylistItem item)
    {
        if (_playingItem != null && !ReferenceEquals(_playingItem, item))
            _playingItem.MarkPlayed();
        _playingItem = item;
        item.MarkPlaying();
    }

    void KickQueue()
    {
        if (_shuttingDown || _queuePaused)
            return;
        if (Interlocked.CompareExchange(ref _queuePump, 1, 0) != 0)
            return;
        _ = PumpQueueAsync();
    }

    async Task PumpQueueAsync()
    {
        try
        {
            while (!_shuttingDown)
            {
                if (_queuePaused)
                {
                    await Task.Delay(200);
                    continue;
                }
                CancellationToken token = _queueCts.Token;
                PlaylistItem? item = _playlist.FirstOrDefault(entry => entry.State == PlaylistItemState.Queued);
                if (item == null)
                    return;
                item.MarkPreparing();
                bool held = false;
                try
                {
                    await _prepGate.WaitAsync(token);
                    held = true;
                    if (item.State != PlaylistItemState.Preparing || !_playlist.Contains(item))
                        continue;
                    var progress = new Progress<string>(text =>
                    {
                        if (item.State == PlaylistItemState.Preparing)
                            item.SetStatus(text);
                    });
                    PreparedSong song = await _prep.PrepareYouTubeAsync(item.Url, progress, token);
                    if (!_playlist.Contains(item))
                        continue;
                    item.MarkReady(song);
                    RefreshRecentYouTubeList();
                    OnItemReady(item);
                }
                catch (OperationCanceledException)
                {
                    if (_playlist.Contains(item) && item.State == PlaylistItemState.Preparing)
                        item.MarkQueued();
                    if (_shuttingDown)
                        return;
                }
                catch (Exception ex)
                {
                    if (_playlist.Contains(item))
                        item.MarkFailed(ex.Message);
                }
                finally
                {
                    if (held)
                        _prepGate.Release();
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _queuePump, 0);
            if (!_shuttingDown && !_queuePaused && _playlist.Any(entry => entry.State == PlaylistItemState.Queued))
                KickQueue();
        }
    }

    void OnItemReady(PlaylistItem item)
    {
        if (_advancing)
            return;
        if (_prepared?.YouTubeVideoId == item.VideoId && _engine.IsPlaying)
            return;
        if (_playWhenReady == item || (_holdForNext && IsUpNext(item)))
        {
            _ = StartPlaylistItem(item, keepMix: _prepared != null);
            return;
        }
        if (_prepared == null && !_engine.IsPlaying && _playingItem == null && !_holdForNext && IsUpNext(item) && item.Song != null)
            _ = UsePrepared(item.Song, applyMelodyDefaults: true);
    }

    bool IsUpNext(PlaylistItem item)
    {
        int start = 0;
        if (_playingItem != null)
        {
            int index = _playlist.IndexOf(_playingItem);
            start = index < 0 ? 0 : index + 1;
        }
        for (int i = start; i < _playlist.Count; i++)
        {
            if (_playlist[i].State == PlaylistItemState.Failed)
                continue;
            return ReferenceEquals(_playlist[i], item);
        }
        return false;
    }

    async Task AdvanceAsync()
    {
        if (_advancing || _shuttingDown)
            return;
        PlaylistItem? next = NextPlayable();
        if (next == null)
        {
            _holdForNext = false;
            _playWhenReady = null;
            if (_playlist.Count > 0)
                StatusText.Text = "Playlist finished.";
            return;
        }
        if (next.Song == null)
        {
            _holdForNext = true;
            _playWhenReady = next;
            StatusText.Text = $"{next.Title} is still preparing.";
            return;
        }
        await StartPlaylistItem(next, keepMix: true);
    }

    PlaylistItem? NextPlayable()
    {
        int start = 0;
        if (_playingItem != null)
        {
            int index = _playlist.IndexOf(_playingItem);
            start = index < 0 ? 0 : index + 1;
        }
        for (int i = start; i < _playlist.Count; i++)
        {
            if (_playlist[i].State != PlaylistItemState.Failed)
                return _playlist[i];
        }
        return null;
    }

    async Task StartPlaylistItem(PlaylistItem item, bool keepMix)
    {
        if (_advancing || item.Song == null || _shuttingDown)
            return;
        _advancing = true;
        try
        {
            _songEndedArmed = false;
            _holdForNext = false;
            _playWhenReady = null;
            PreparedSong song = item.Song;
            await UsePrepared(song, applyMelodyDefaults: !keepMix);
            if (_shuttingDown || _prepared?.YouTubeVideoId != song.YouTubeVideoId)
                return;
            if (_engine.LengthSeconds <= 0)
            {
                _holdForNext = true;
                _playWhenReady = item;
                return;
            }
            MarkPlaying(item);
            _engine.Play();
            PlayButton.Content = "Pause";
            _songEndedArmed = true;
        }
        finally
        {
            _advancing = false;
        }
    }

    async Task PlaySelectedAsync()
    {
        if (PlaylistList.SelectedItem is not PlaylistItem item)
        {
            StatusText.Text = "Pick a song in the playlist first.";
            return;
        }
        if (item.State == PlaylistItemState.Failed)
        {
            item.MarkQueued();
            Promote(item);
            _playWhenReady = item;
            KickQueue();
            StatusText.Text = $"Preparing {item.Title} again.";
            return;
        }
        if (item.Song == null)
        {
            Promote(item);
            _playWhenReady = item;
            KickQueue();
            StatusText.Text = $"{item.Title} is still preparing.";
            return;
        }
        await StartPlaylistItem(item, keepMix: _prepared != null);
    }

    void Promote(PlaylistItem item)
    {
        int index = _playlist.IndexOf(item);
        if (index <= 0)
            return;
        int insert = 0;
        for (int i = 0; i < index; i++)
        {
            if (_playlist[i].State is PlaylistItemState.Preparing or PlaylistItemState.Playing)
                insert = i + 1;
        }
        if (insert < index)
            _playlist.Move(index, insert);
    }

    void RemoveSelected()
    {
        if (PlaylistList.SelectedItem is not PlaylistItem item)
            return;
        bool preparing = item.State == PlaylistItemState.Preparing;
        if (ReferenceEquals(_playingItem, item))
            _playingItem = null;
        if (ReferenceEquals(_playWhenReady, item))
            _playWhenReady = null;
        _playlist.Remove(item);
        if (!preparing)
            return;
        _queueCts.Cancel();
        _queueCts = new CancellationTokenSource();
    }

    void ClearPlaylist()
    {
        _playingItem = null;
        _playWhenReady = null;
        _holdForNext = false;
        _playlist.Clear();
        _queueCts.Cancel();
        _queueCts = new CancellationTokenSource();
    }

    void PlaylistDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        bool ok = DropHasYouTube(e.Data);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        PlaylistDrop.BorderBrush = ok ? PlaylistHot : PlaylistIdle;
    }

    void PlaylistDragLeave(object sender, DragEventArgs e)
    {
        Point point = e.GetPosition(PlaylistDrop);
        if (point.X < 0 || point.Y < 0 || point.X > PlaylistDrop.ActualWidth || point.Y > PlaylistDrop.ActualHeight)
            PlaylistDrop.BorderBrush = PlaylistIdle;
    }

    void PlaylistDropUrls(object sender, DragEventArgs e)
    {
        e.Handled = true;
        PlaylistDrop.BorderBrush = PlaylistIdle;
        string? text = ReadDrop(e.Data);
        int added = AddLinks(text);
        if (added == 0)
            StatusText.Text = "That drop did not include a YouTube link.";
    }

    int AddLinks(string? text)
    {
        int added = 0;
        foreach (YouTubeCache.YouTubeLink link in YouTubeCache.ExtractLinks(text))
        {
            if (_playlist.Any(entry => entry.VideoId == link.VideoId))
                continue;
            _playlist.Add(new PlaylistItem(link.Url, link.VideoId));
            added++;
        }
        if (added == 0)
            return 0;
        StatusText.Text = added == 1
            ? "Added 1 song. It will prepare while this one plays."
            : $"Added {added} songs. They prepare one at a time while this one plays.";
        KickQueue();
        return added;
    }

    static bool DropHasYouTube(IDataObject data)
    {
        try
        {
            if (YouTubeCache.ExtractLinks(ReadDrop(data)).Count > 0)
                return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }
        return data.GetDataPresent(DataFormats.UnicodeText)
            || data.GetDataPresent(DataFormats.Text)
            || data.GetDataPresent(DataFormats.Html)
            || data.GetDataPresent("text/x-moz-url")
            || data.GetDataPresent("UniformResourceLocator")
            || data.GetDataPresent("UniformResourceLocatorW");
    }

    static string? ReadDrop(IDataObject data)
    {
        var text = new StringBuilder();
        foreach (string format in new[]
                 {
                     DataFormats.UnicodeText,
                     DataFormats.Text,
                     DataFormats.StringFormat,
                     DataFormats.Html,
                     "text/x-moz-url",
                     "UniformResourceLocatorW",
                     "UniformResourceLocator"
                 })
        {
            if (!data.GetDataPresent(format))
                continue;
            object? value = data.GetData(format);
            if (value is string chunk && chunk.Length > 0)
                text.AppendLine(chunk);
            else if (value is MemoryStream stream)
                text.AppendLine(DecodeDrop(stream));
        }
        return text.Length == 0 ? null : text.ToString();
    }

    static string DecodeDrop(MemoryStream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.Unicode, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }

    void SetBusy(bool busy)
    {
        LoadFileButton.IsEnabled = !busy;
        FetchButton.IsEnabled = !busy;
        YouTubeBox.IsEnabled = !busy;
        RecentYouTubeList.IsEnabled = !busy;
        LoadRecentYouTubeButton.IsEnabled = !busy && RecentYouTubeList.SelectedItem is YouTubeCacheEntry;
    }

    void UpdateLyrics()
    {
        if (_lyrics == null || LyricCard.Visibility != Visibility.Visible)
            return;
        if (_lyrics.Lines.Count == 0)
        {
            if (_shownWord == -2)
                return;
            PrevLyric.Text = "";
            NextLyric.Text = "";
            SetLyricMessage("No timed lyrics for this video.");
            _shownWord = -2;
            return;
        }

        double playhead = Seek.IsMouseCaptureWithin ? Seek.Value : _engine.PositionSeconds;
        double heard = playhead - _engine.LatencySeconds + SyncTrim.Value / 1000.0;
        LyricView view = _lyrics.At(heard + LyricTrack.ReadAheadSec);
        if (ReferenceEquals(view.Previous, _shownPrevious)
            && ReferenceEquals(view.Current, _shownCurrent)
            && ReferenceEquals(view.Next, _shownNext)
            && view.ActiveWord == _shownWord)
            return;

        _shownPrevious = view.Previous;
        _shownCurrent = view.Current;
        _shownNext = view.Next;
        _shownWord = view.ActiveWord;
        PrevLyric.Text = view.Previous?.Text ?? "";
        NextLyric.Text = view.Next?.Text ?? "";
        PaintCurrent(view.Current, view.ActiveWord);
    }

    void PaintCurrent(LyricLine? line, int activeWord)
    {
        CurrentLyric.Inlines.Clear();
        if (line == null)
            return;
        if (line.Words.Count == 0)
        {
            CurrentLyric.Inlines.Add(new Run(line.Text) { Foreground = LyricActive });
            return;
        }
        for (int i = 0; i < line.Words.Count; i++)
        {
            string text = i == line.Words.Count - 1 ? line.Words[i].Text : line.Words[i].Text + " ";
            SolidColorBrush color = i < activeWord ? LyricSung : i == activeWord ? LyricActive : LyricWaiting;
            CurrentLyric.Inlines.Add(new Run(text) { Foreground = color });
        }
    }

    void UpdateStage()
    {
        bool performing = LyricCard.Visibility == Visibility.Visible
            || PitchRibbonCard.Visibility == Visibility.Visible
            || _videoPlace != VideoPlace.Hidden;
        StageEmpty.Visibility = performing ? Visibility.Collapsed : Visibility.Visible;
    }

    void OnVideoKey(object sender, KeyEventArgs e)
    {
        if (_videoPlace == VideoPlace.Hidden)
            return;
        if (e.Key == Key.Escape && _videoPlace == VideoPlace.Full)
        {
            ExitFullScreen();
            e.Handled = true;
        }
        else if (e.Key == Key.F11 && Keyboard.FocusedElement is not TextBox)
        {
            if (_videoPlace == VideoPlace.Full)
                ExitFullScreen();
            else
                EnterFullScreen();
            e.Handled = true;
        }
    }

    void PopOutVideo()
    {
        if (string.IsNullOrEmpty(_prepared?.YouTubeVideoId))
            return;
        RememberDockedHeight();
        EnsurePopout();
        PlaceVideo(_popout!.Host);
        _returnFullToPop = false;
        _videoPlace = VideoPlace.Popped;
        ApplyVideoLayout();
    }

    void EnterFullScreen()
    {
        if (string.IsNullOrEmpty(_prepared?.YouTubeVideoId))
            return;
        _returnFullToPop = _videoPlace == VideoPlace.Popped;
        if (_videoPlace == VideoPlace.Docked)
            RememberDockedHeight();
        EnsurePopout();
        PlaceVideo(_popout!.Host);
        _popout.EnterFullScreen();
        _videoPlace = VideoPlace.Full;
        ApplyVideoLayout();
    }

    void ExitFullScreen()
    {
        if (_returnFullToPop && _popout != null)
        {
            _popout.ExitFullScreen();
            _videoPlace = VideoPlace.Popped;
            ApplyVideoLayout();
            return;
        }

        DockVideo();
    }

    void DockVideo()
    {
        if (_shuttingDown)
            return;
        RememberPopBounds();
        PlaceVideo(VideoHost);
        VideoPopoutWindow? window = _popout;
        _popout = null;
        _returnFullToPop = false;
        window?.CloseForReal();
        _videoPlace = string.IsNullOrEmpty(_prepared?.YouTubeVideoId) ? VideoPlace.Hidden : VideoPlace.Docked;
        ApplyVideoLayout();
    }

    void HideVideo()
    {
        RememberPopBounds();
        PlaceVideo(VideoHost);
        VideoPopoutWindow? window = _popout;
        _popout = null;
        _returnFullToPop = false;
        window?.CloseForReal();
        _videoPlace = VideoPlace.Hidden;
        ApplyVideoLayout();
    }

    void EnsurePopout()
    {
        if (_popout != null)
            return;
        var window = new VideoPopoutWindow();
        if (_popBounds is Rect bounds && bounds.Width >= 100 && bounds.Height >= 80)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = bounds.Left;
            window.Top = bounds.Top;
            window.Width = bounds.Width;
            window.Height = bounds.Height;
        }

        window.DockRequested += (_, _) => DockVideo();
        window.EscapePressed += (_, _) => ExitFullScreen();
        window.FullScreenKeyPressed += (_, _) =>
        {
            if (_videoPlace == VideoPlace.Full)
                ExitFullScreen();
            else
                EnterFullScreen();
        };
        window.Show();
        _popout = window;
    }

    void ApplyVideoLayout()
    {
        RememberDockedHeight();
        bool docked = _videoPlace == VideoPlace.Docked;
        bool popped = _videoPlace == VideoPlace.Popped;
        VideoCard.Visibility = docked || popped ? Visibility.Visible : Visibility.Collapsed;
        VideoDockHint.Visibility = popped ? Visibility.Visible : Visibility.Collapsed;
        if (docked)
        {
            VideoRow.MinHeight = 180;
            VideoRow.MaxHeight = double.PositiveInfinity;
            if (VideoRow.Height.GridUnitType != GridUnitType.Pixel || VideoRow.Height.Value < 180)
                VideoRow.Height = new GridLength(_videoHeight);
            VideoSplitter.Visibility = Visibility.Visible;
        }
        else if (popped)
        {
            VideoRow.MinHeight = 0;
            VideoRow.MaxHeight = double.PositiveInfinity;
            VideoRow.Height = GridLength.Auto;
            VideoSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            VideoRow.MinHeight = 0;
            VideoRow.MaxHeight = 0;
            VideoRow.Height = new GridLength(0);
            VideoSplitter.Visibility = Visibility.Collapsed;
        }

        VideoSurface.SetChrome(_videoPlace is VideoPlace.Popped or VideoPlace.Full, _videoPlace == VideoPlace.Full);
        UpdateStage();
    }

    void RememberDockedHeight()
    {
        if (VideoRow.Height.GridUnitType == GridUnitType.Pixel && VideoRow.Height.Value >= 180)
            _videoHeight = VideoRow.Height.Value;
    }

    void RememberPopBounds()
    {
        if (_popout == null)
            return;
        Rect bounds = _popout.RestorableBounds;
        if (bounds.Width >= 100 && bounds.Height >= 80 && !double.IsNaN(bounds.Left))
            _popBounds = bounds;
    }

    static void PlaceVideo(Panel host, VideoSurface surface)
    {
        if (ReferenceEquals(surface.Parent, host))
            return;
        if (surface.Parent is Panel panel)
            panel.Children.Remove(surface);
        host.Children.Add(surface);
    }

    void PlaceVideo(Panel host) => PlaceVideo(host, VideoSurface);

    enum VideoPlace
    {
        Hidden,
        Docked,
        Popped,
        Full
    }

    void SetLyricMessage(string message)
    {
        CurrentLyric.Inlines.Clear();
        CurrentLyric.Inlines.Add(new Run(message) { Foreground = LyricQuiet });
    }

    void UpdatePitchRibbon()
    {
        MelodyContour? contour = _engine.SongContour;
        if (!_engine.IsRunning || !_engine.IsPlaying || contour == null)
        {
            PitchRibbonCard.Visibility = Visibility.Collapsed;
            UpdateStage();
            return;
        }

        PitchRibbonCard.Visibility = Visibility.Visible;
        UpdateStage();
        double endHeard = (Seek.IsMouseCaptureWithin ? Seek.Value : _engine.PositionSeconds)
            - _engine.LatencySeconds + SyncTrim.Value / 1000.0;
        int shift = (int)Math.Round(SongShift.Value);
        float ratio = shift == 0 ? 1f : MathF.Pow(2f, shift / 12f);
        float center = 440f;
        if (contour.TryCenter(endHeard, out float leadNow, out _) && leadNow > 20f)
            center = leadNow * ratio;
        else if (_engine.TargetHz > 20f)
            center = _engine.TargetHz;
        else if (_engine.DetectedHz > 20f)
            center = _engine.DetectedHz;

        PitchRibbon.Update(endHeard, center, contour, shift, useSongClock: true);
    }

    static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    static float Percent(Slider slider) => (float)(slider.Value / 100.0);

    static string Clock(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0)
            seconds = 0;
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}
