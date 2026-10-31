using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KaraokeVox.App.Services;

public enum PlaylistItemState
{
    Queued,
    Preparing,
    Ready,
    Playing,
    Failed
}

public sealed class PlaylistItem : INotifyPropertyChanged
{
    string _title;
    string _status;
    string _detail;

    public PlaylistItem(string url, string videoId)
    {
        Url = url;
        VideoId = videoId;
        _title = videoId;
        _status = "Queued";
        _detail = url;
    }

    public string Url { get; }

    public string VideoId { get; }

    public PreparedSong? Song { get; private set; }

    public PlaylistItemState State { get; private set; } = PlaylistItemState.Queued;

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string Detail
    {
        get => _detail;
        private set => Set(ref _detail, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetStatus(string status) => Status = status;

    public void MarkQueued()
    {
        State = PlaylistItemState.Queued;
        Status = "Queued";
    }

    public void MarkPreparing()
    {
        State = PlaylistItemState.Preparing;
        Status = "Preparing";
    }

    public void MarkReady(PreparedSong song)
    {
        Song = song;
        if (!string.IsNullOrWhiteSpace(song.Title))
            Title = song.Title;
        State = PlaylistItemState.Ready;
        Status = "Ready";
        Detail = Url;
    }

    public void MarkPlaying()
    {
        State = PlaylistItemState.Playing;
        Status = "Playing";
    }

    public void MarkPlayed()
    {
        if (State != PlaylistItemState.Playing)
            return;
        State = PlaylistItemState.Ready;
        Status = "Played";
    }

    public void MarkFailed(string message)
    {
        State = PlaylistItemState.Failed;
        Status = "Failed";
        Detail = message;
    }

    void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value)
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
