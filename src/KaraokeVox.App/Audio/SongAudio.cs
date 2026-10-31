using KaraokeVox;

namespace KaraokeVox.App.Audio;

public sealed class SongAudio
{
    public SongAudio(float[] instrumental, float[]? guide, MelodyContour? contour, string title)
    {
        Instrumental = instrumental;
        Guide = guide;
        Contour = contour;
        Title = title;
        Frames = instrumental.Length / 2;
    }

    public float[] Instrumental { get; }
    public float[]? Guide { get; }
    public MelodyContour? Contour { get; }
    public string Title { get; }
    public int Frames { get; }
}

public sealed class DeviceChoice
{
    public DeviceChoice(string id, string name, bool isBluetooth, bool isHandsFree)
    {
        Id = id;
        Name = name;
        IsBluetooth = isBluetooth;
        IsHandsFree = isHandsFree;
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsBluetooth { get; }
    public bool IsHandsFree { get; }

    public override string ToString() => Name;
}
