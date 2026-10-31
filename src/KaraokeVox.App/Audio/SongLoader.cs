using KaraokeVox;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KaraokeVox.App.Audio;

public static class SongLoader
{
    public static SongAudio Load(string instrumentalPath, string? guidePath, string? contourPath, string title, int sampleRate)
    {
        float[] instrumental = ReadStereo(instrumentalPath, sampleRate);
        float[]? guide = null;
        if (!string.IsNullOrWhiteSpace(guidePath) && File.Exists(guidePath))
        {
            guide = ReadStereo(guidePath, sampleRate);
            if (guide.Length != instrumental.Length)
                Array.Resize(ref guide, instrumental.Length);
        }
        MelodyContour? contour = null;
        if (!string.IsNullOrWhiteSpace(contourPath) && File.Exists(contourPath))
            contour = MelodyContour.Load(contourPath);
        if (instrumental.Length < 2)
            throw new InvalidDataException("The song file has no audio.");
        return new SongAudio(instrumental, guide, contour, title);
    }

    public static void ConvertToWav(string sourcePath, string wavPath)
    {
        if (sourcePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(wavPath), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, wavPath, true);
            return;
        }
        using var reader = new MediaFoundationReader(sourcePath);
        WaveFileWriter.CreateWaveFile(wavPath, reader);
    }

    static float[] ReadStereo(string path, int sampleRate)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider source = reader;
        if (reader.WaveFormat.Channels == 1)
            source = new MonoToStereoSampleProvider(reader);
        else if (reader.WaveFormat.Channels > 2)
            source = new StereoWrap(reader);
        if (source.WaveFormat.SampleRate != sampleRate)
            source = new WdlResamplingSampleProvider(source, sampleRate);

        var samples = new List<float>(sampleRate * 4);
        float[] buffer = new float[sampleRate];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
                samples.Add(buffer[i]);
        }
        if (samples.Count % 2 == 1)
            samples.Add(0f);
        return samples.ToArray();
    }

    sealed class StereoWrap : ISampleProvider
    {
        readonly AudioFileReader _reader;
        public StereoWrap(AudioFileReader reader)
        {
            _reader = reader;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(reader.WaveFormat.SampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int channels = _reader.WaveFormat.Channels;
            int frames = count / 2;
            float[] raw = new float[frames * channels];
            int got = _reader.Read(raw, 0, raw.Length);
            int inFrames = got / channels;
            for (int i = 0; i < inFrames; i++)
            {
                buffer[offset + i * 2] = raw[i * channels];
                buffer[offset + i * 2 + 1] = raw[i * channels + 1];
            }
            return inFrames * 2;
        }
    }
}
