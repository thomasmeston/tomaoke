using KaraokeVox;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace KaraokeVox.App.Audio;

public sealed class AudioEngine : IDisposable
{
    readonly MMDeviceEnumerator _enumerator = new();
    readonly MicRing _ring = new();
    readonly TrackPitch _track = new();
    readonly SyncTap _syncTap = new();
    readonly PitchRibbonRing _pitchRibbon = new();
    SnapshotBox _controls = new() { Value = ControlSnapshot.Default };
    SongAudio? _song;
    DspChain? _chain;
    WasapiCapture? _capture;
    WasapiOut? _render;
    MMDevice? _captureDevice;
    MMDevice? _renderDevice;
    WaveFormat? _captureFormat;
    WaveFormat? _outputFormat;
    LinearResampler? _resampler;
    float[] _mono = new float[8192];
    float[] _pulled = new float[8192];
    float[] _micBlock = new float[8192];
    float[] _vocal = new float[8192];
    int _sampleRate;
    int _channels = 2;
    int _bits = 32;
    bool _floatOutput;
    bool _floatCapture;
    int _captureBits;
    int _captureChannels;
    int _renderMs = 10;
    double _latencySec;
    long _playFrame;
    long _positionFrame;
    long _seekFrame;
    int _seekPending;
    int _playing;
    int _running;
    int _stopping;
    int _errorOnce;
    float _music = 0.85f;
    float _guide;
    float _master = 0.9f;
    bool _gainsPrimed;
    int _outPeakBits;
    bool _live;
    int _cushion;
    long _ribbonSecBits;

    public event Action<string>? Failed;

    public bool IsRunning => Volatile.Read(ref _running) == 1;
    public bool IsPlaying => Volatile.Read(ref _playing) == 1;
    public int SampleRate => _sampleRate;
    public double LatencySeconds => _latencySec;

    public double PositionSeconds =>
        _sampleRate == 0 ? 0 : Volatile.Read(ref _positionFrame) / (double)_sampleRate;

    public double LengthSeconds
    {
        get
        {
            SongAudio? song = Volatile.Read(ref _song);
            return song == null || _sampleRate == 0 ? 0 : song.Frames / (double)_sampleRate;
        }
    }

    public float InputPeak => _chain?.InputPeak ?? 0f;
    public float OutputPeak => BitConverter.Int32BitsToSingle(Volatile.Read(ref _outPeakBits));
    public float DetectedHz => _chain?.Pitch.DetectedHz ?? 0f;
    public float TargetHz => _chain?.Pitch.TargetHz ?? 0f;
    public bool PitchVoiced => _chain?.Pitch.Voiced ?? false;
    public bool MelodyTargetActive => _chain?.Pitch.MelodyTargetActive ?? false;
    public bool SyncTapRunning => _syncTap.Running;
    public int SyncTapHits => _syncTap.Hits;
    public float SyncTapTrimMs => _syncTap.SuggestedTrimMs;
    public PitchRibbonRing PitchRibbon => _pitchRibbon;

    public double LiveRibbonSeconds =>
        BitConverter.Int64BitsToDouble(Volatile.Read(ref _ribbonSecBits));

    public MelodyContour? SongContour => Volatile.Read(ref _song)?.Contour;

    public void StartSyncTap()
    {
        if (_sampleRate > 0)
            _syncTap.Start(_sampleRate);
    }

    public void StopSyncTap() => _syncTap.Stop();

    public void SetControls(ControlSnapshot snapshot) =>
        Volatile.Write(ref _controls, new SnapshotBox { Value = snapshot });

    public void SetSong(SongAudio? song)
    {
        Volatile.Write(ref _song, song);
        Volatile.Write(ref _playing, 0);
        _playFrame = 0;
        _track.Reset(0);
        Volatile.Write(ref _positionFrame, 0);
    }

    public void Play()
    {
        if (Volatile.Read(ref _song) != null)
            Volatile.Write(ref _playing, 1);
    }

    public void Pause() => Volatile.Write(ref _playing, 0);

    public void SeekSeconds(double seconds)
    {
        if (_sampleRate <= 0)
            return;
        SongAudio? song = Volatile.Read(ref _song);
        long frame = (long)(seconds * _sampleRate);
        if (frame < 0)
            frame = 0;
        if (song != null && frame > song.Frames)
            frame = song.Frames;
        Volatile.Write(ref _seekFrame, frame);
        Interlocked.Exchange(ref _seekPending, 1);
    }

    public void Start(string captureId, string renderId, bool bluetoothRender)
    {
        Stop();
        _renderMs = bluetoothRender ? 50 : 10;
        const int captureMs = 10;
        _captureDevice = _enumerator.GetDevice(captureId);
        _renderDevice = _enumerator.GetDevice(renderId);
        _outputFormat = _renderDevice.AudioClient.MixFormat;
        _sampleRate = _outputFormat.SampleRate;
        _channels = Math.Max(1, _outputFormat.Channels);
        _bits = _outputFormat.BitsPerSample;
        _floatOutput = IsFloat(_outputFormat);
        Volatile.Write(ref _ribbonSecBits, BitConverter.DoubleToInt64Bits(0));
        _pitchRibbon.Reset();
        _chain = new DspChain(_sampleRate, _pitchRibbon);
        _ring.Reset();
        _ring.MaxHoldSamples = Math.Max(_sampleRate / 5, _sampleRate * _renderMs * 4 / 1000);
        _cushion = Math.Max(64, _sampleRate * _renderMs / 1000);
        _live = false;
        _gainsPrimed = false;
        _latencySec = (captureMs + _renderMs + 12) / 1000.0;
        _capture = new WasapiCapture(_captureDevice, true, captureMs);
        _captureFormat = _capture.WaveFormat;
        _captureBits = _captureFormat.BitsPerSample;
        _captureChannels = Math.Max(1, _captureFormat.Channels);
        _floatCapture = IsFloat(_captureFormat);
        _resampler = new LinearResampler(_captureFormat.SampleRate, _sampleRate);
        var provider = new EngineProvider(this, _outputFormat);
        // Leave the stream category at the WASAPI default. Communications mode
        // moves many Bluetooth speakers off A2DP stereo and onto the hands-free profile.
        _render = new WasapiOut(_renderDevice, AudioClientShareMode.Shared, true, _renderMs);
        _render.PlaybackStopped += OnPlaybackStopped;
        _render.Init(provider);
        _capture.DataAvailable += OnCapture;
        _capture.RecordingStopped += OnCaptureStopped;
        Interlocked.Exchange(ref _errorOnce, 0);
        Volatile.Write(ref _running, 1);
        _capture.StartRecording();
        _render.Play();
    }

    public void Stop()
    {
        Interlocked.Exchange(ref _stopping, 1);
        Volatile.Write(ref _running, 0);
        Volatile.Write(ref _playing, 0);
        try
        {
            if (_render != null)
            {
                _render.PlaybackStopped -= OnPlaybackStopped;
                _render.Stop();
                _render.Dispose();
                _render = null;
            }
            if (_capture != null)
            {
                _capture.DataAvailable -= OnCapture;
                _capture.RecordingStopped -= OnCaptureStopped;
                _capture.StopRecording();
                _capture.Dispose();
                _capture = null;
            }
        }
        finally
        {
            _captureDevice?.Dispose();
            _renderDevice?.Dispose();
            _captureDevice = null;
            _renderDevice = null;
            Interlocked.Exchange(ref _stopping, 0);
        }
    }

    public void Dispose()
    {
        Stop();
        _enumerator.Dispose();
    }

    void OnCapture(object? sender, WaveInEventArgs e)
    {
        try
        {
            WaveFormat? format = _captureFormat;
            LinearResampler? resampler = _resampler;
            if (format == null || resampler == null || format.BlockAlign <= 0)
                return;
            int frames = e.BytesRecorded / format.BlockAlign;
            if (frames <= 0)
                return;
            if (_mono.Length < frames)
                _mono = new float[frames];
            ToMono(e.Buffer, frames, _mono);
            resampler.Push(_mono.AsSpan(0, frames));
            for (int guard = 0; guard < 8; guard++)
            {
                if (_pulled.Length < frames * 2)
                    _pulled = new float[frames * 2];
                int produced = resampler.Pull(_pulled);
                if (produced <= 0)
                    break;
                _ring.Write(_pulled.AsSpan(0, produced));
            }
        }
        catch (Exception ex)
        {
            ReportOnce(ex.Message);
        }
    }

    void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        if (Volatile.Read(ref _stopping) == 0 && e.Exception != null)
            ReportOnce(e.Exception.Message);
    }

    void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (Volatile.Read(ref _stopping) == 0 && e.Exception != null)
            ReportOnce(e.Exception.Message);
    }

    internal int Render(byte[] buffer, int offset, int count)
    {
        try
        {
            WaveFormat? format = _outputFormat;
            DspChain? chain = _chain;
            if (format == null || chain == null || format.BlockAlign <= 0)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            int frames = count / format.BlockAlign;
            if (frames <= 0)
                return count;
            if (!_live && _ring.Available < _cushion)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }
            _live = true;

            if (_micBlock.Length < frames)
                _micBlock = new float[frames];
            if (_vocal.Length < frames)
                _vocal = new float[frames];
            _ring.Read(_micBlock.AsSpan(0, frames));

            if (Interlocked.Exchange(ref _seekPending, 0) == 1)
            {
                _playFrame = Volatile.Read(ref _seekFrame);
                _track.Reset(_playFrame);
            }

            SnapshotBox box = Volatile.Read(ref _controls);
            ControlSnapshot controls = box.Value;
            SongAudio? song = Volatile.Read(ref _song);
            bool playing = Volatile.Read(ref _playing) == 1 && song != null;
            if (playing && song != null && _playFrame >= song.Frames)
            {
                playing = false;
                Volatile.Write(ref _playing, 0);
            }
            double songTime = song != null ? _playFrame / (double)_sampleRate : -1;
            double liveRibbon = BitConverter.Int64BitsToDouble(Volatile.Read(ref _ribbonSecBits));
            chain.Process(_micBlock.AsSpan(0, frames), _vocal.AsSpan(0, frames), controls, song?.Contour, songTime, _latencySec, liveRibbon);
            liveRibbon += frames / (double)_sampleRate;
            Volatile.Write(ref _ribbonSecBits, BitConverter.DoubleToInt64Bits(liveRibbon));

            if (!_gainsPrimed)
            {
                _music = controls.MusicGain;
                _guide = controls.GuideGain;
                _master = controls.MasterGain;
                _gainsPrimed = true;
            }
            float alpha = 1f - MathF.Exp(-1f / (_sampleRate * 0.02f));
            float peak = 0f;
            int cursor = offset;
            for (int i = 0; i < frames; i++)
            {
                _music += (controls.MusicGain - _music) * alpha;
                _guide += (controls.GuideGain - _guide) * alpha;
                _master += (controls.MasterGain - _master) * alpha;
                float left = 0f;
                float right = 0f;
                if (playing && song != null && _playFrame < song.Frames)
                {
                    TrackFrame track = _track.Read(song.Instrumental, song.Guide, _playFrame, controls.SongShiftSemitones);
                    left = track.Left * _music + track.GuideLeft * _guide;
                    right = track.Right * _music + track.GuideRight * _guide;
                    _playFrame++;
                }
                float vocal = _vocal[i];
                left = (left + vocal) * _master;
                right = (right + vocal) * _master;
                if (_syncTap.Running)
                    _syncTap.Tick(_micBlock[i], ref left, ref right);
                if (left > 1f) left = 1f;
                if (left < -1f) left = -1f;
                if (right > 1f) right = 1f;
                if (right < -1f) right = -1f;
                peak = MathF.Max(peak, MathF.Max(MathF.Abs(left), MathF.Abs(right)));
                cursor = WriteFrame(buffer, cursor, left, right);
            }
            Volatile.Write(ref _positionFrame, _playFrame);
            float held = BitConverter.Int32BitsToSingle(Volatile.Read(ref _outPeakBits));
            float decay = MathF.Exp(-(frames / (float)_sampleRate) / 0.15f);
            held = MathF.Max(peak, held * decay);
            Volatile.Write(ref _outPeakBits, BitConverter.SingleToInt32Bits(held));
            return frames * format.BlockAlign;
        }
        catch (Exception ex)
        {
            ReportOnce(ex.Message);
            Array.Clear(buffer, offset, count);
            return count;
        }
    }

    int WriteFrame(byte[] buffer, int offset, float left, float right)
    {
        for (int channel = 0; channel < _channels; channel++)
        {
            float sample = _channels == 1 ? 0.5f * (left + right) : channel == 0 ? left : channel == 1 ? right : 0f;
            if (_floatOutput && _bits == 32)
            {
                BitConverter.TryWriteBytes(buffer.AsSpan(offset), sample);
                offset += 4;
            }
            else if (_bits == 16)
            {
                short quantized = (short)Math.Clamp(sample * 32767f, short.MinValue, short.MaxValue);
                BitConverter.TryWriteBytes(buffer.AsSpan(offset), quantized);
                offset += 2;
            }
            else if (_bits == 24)
            {
                int quantized = (int)Math.Clamp(sample * 8388607f, -8388608f, 8388607f);
                buffer[offset] = (byte)quantized;
                buffer[offset + 1] = (byte)(quantized >> 8);
                buffer[offset + 2] = (byte)(quantized >> 16);
                offset += 3;
            }
            else
            {
                int quantized = (int)Math.Clamp(sample * 2147483647f, int.MinValue, int.MaxValue);
                BitConverter.TryWriteBytes(buffer.AsSpan(offset), quantized);
                offset += 4;
            }
        }
        return offset;
    }

    void ToMono(byte[] source, int frames, float[] destination)
    {
        int bytesPerSample = Math.Max(1, _captureBits / 8);
        int stride = _captureFormat?.BlockAlign ?? bytesPerSample * _captureChannels;
        for (int frame = 0; frame < frames; frame++)
        {
            int frameOffset = frame * stride;
            float sum = 0f;
            for (int channel = 0; channel < _captureChannels; channel++)
                sum += ReadCaptureSample(source, frameOffset + channel * bytesPerSample);
            destination[frame] = sum / _captureChannels;
        }
    }

    float ReadCaptureSample(byte[] source, int offset)
    {
        if (_floatCapture)
            return BitConverter.ToSingle(source, offset);
        if (_captureBits == 16)
            return BitConverter.ToInt16(source, offset) / 32768f;
        if (_captureBits == 24)
        {
            int value = source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16);
            if ((value & 0x800000) != 0)
                value |= ~0xFFFFFF;
            return value / 8388608f;
        }
        if (_captureBits == 32)
            return BitConverter.ToInt32(source, offset) / 2147483648f;
        return 0f;
    }

    static bool IsFloat(WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
            return true;
        return format is WaveFormatExtensible extensible
            && extensible.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
    }

    void ReportOnce(string message)
    {
        if (Interlocked.Exchange(ref _errorOnce, 1) == 0)
            Failed?.Invoke(message);
    }

    sealed class SnapshotBox
    {
        public ControlSnapshot Value;
    }

    sealed class EngineProvider : IWaveProvider
    {
        readonly AudioEngine _engine;
        public EngineProvider(AudioEngine engine, WaveFormat format)
        {
            _engine = engine;
            WaveFormat = format;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count) => _engine.Render(buffer, offset, count);
    }
}
