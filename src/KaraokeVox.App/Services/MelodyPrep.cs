using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KaraokeVox.App.Audio;

namespace KaraokeVox.App.Services;

public sealed record PreparedSong(
    string Title,
    string InstrumentalPath,
    string GuidePath,
    string ContourPath,
    string? LyricsPath = null,
    bool FromYouTube = false,
    string? YouTubeVideoId = null);

public sealed class MelodyPrep
{
    static readonly Regex VideoId = new(
        @"(?:youtu\.be/|youtube\.com/(?:watch\?v=|shorts/|live/|embed/))([A-Za-z0-9_-]{11})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KaraokeVox");

    public async Task<PreparedSong> PrepareFileAsync(string mediaPath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        string dir = Path.Combine(Root, "songs", HashKey(mediaPath));
        Directory.CreateDirectory(dir);
        string source = Path.Combine(dir, "source.wav");
        if (!File.Exists(source))
        {
            progress.Report("Decoding audio…");
            await Task.Run(() => SongLoader.ConvertToWav(mediaPath, source), cancellationToken);
        }
        return await PrepareWavAsync(source, Path.GetFileNameWithoutExtension(mediaPath), dir, progress, cancellationToken);
    }

    public async Task<PreparedSong> PrepareYouTubeAsync(string url, IProgress<string> progress, CancellationToken cancellationToken)
    {
        string dir = Path.Combine(Root, "youtube", FolderForUrl(url.Trim()));
        Directory.CreateDirectory(dir);
        string source = Path.Combine(dir, "source.wav");
        string titlePath = Path.Combine(dir, "title.txt");
        await File.WriteAllTextAsync(Path.Combine(dir, "url.txt"), url.Trim(), cancellationToken);
        if (!File.Exists(source))
        {
            string python = await EnsureVenvAsync(progress, cancellationToken);
            progress.Report("Downloading audio…");
            var (code, stdout, stderr) = await RunAsync(
                python,
                ["-m", "yt_dlp", "--no-playlist", "--no-progress", "--print-to-file", "%(title)s", "title.txt",
                    "-f", "ba[ext=m4a]/ba", "-o", "audio.%(ext)s", url.Trim()],
                dir,
                progress,
                cancellationToken);
            if (code != 0)
                throw new InvalidOperationException("Could not download that video. " + Tail(stderr + stdout));
            string? downloaded = FindDownloadedAudio(dir);
            if (downloaded == null)
                throw new InvalidOperationException("The download finished without an audio file.");
            progress.Report("Decoding audio…");
            if (downloaded.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                File.Copy(downloaded, source, true);
            else
                await Task.Run(() => SongLoader.ConvertToWav(downloaded, source), cancellationToken);
        }
        string title = File.Exists(titlePath)
            ? (await File.ReadAllTextAsync(titlePath, cancellationToken)).Trim()
            : "YouTube song";
        if (string.IsNullOrWhiteSpace(title))
            title = "YouTube song";
        string? lyrics = await TryLyricsAsync(url.Trim(), dir, progress, cancellationToken);
        PreparedSong song = await PrepareWavAsync(source, title, dir, progress, cancellationToken);
        Match video = VideoId.Match(url.Trim());
        return song with { LyricsPath = lyrics, FromYouTube = true, YouTubeVideoId = video.Success ? video.Groups[1].Value : null };
    }

    async Task<string?> TryLyricsAsync(string url, string dir, IProgress<string> progress, CancellationToken cancellationToken)
    {
        string dest = Path.Combine(dir, "lyrics.json");
        if (File.Exists(dest))
            return dest;
        try
        {
            LyricTrack? track = ChooseCaption(dir);
            if (track == null)
            {
                string python = await EnsureVenvAsync(progress, cancellationToken);
                progress.Report("Fetching captions…");
                await RunAsync(
                    python,
                    ["-m", "yt_dlp", "--skip-download", "--no-playlist", "--no-progress",
                        "--write-subs", "--write-auto-subs", "--sub-langs", "en,.*-orig",
                        "--sub-format", "json3/vtt", "-o", "caption.%(ext)s", url],
                    dir,
                    progress,
                    cancellationToken);
                track = ChooseCaption(dir);
            }
            if (track == null)
                return null;
            track.Save(dest);
            return dest;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            progress.Report(ex.Message);
            return null;
        }
    }

    static string? FindDownloadedAudio(string dir)
    {
        foreach (string path in Directory.GetFiles(dir, "audio.*"))
        {
            string ext = Path.GetExtension(path);
            if (ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".webm", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".opus", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".aac", StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return null;
    }

    static LyricTrack? ChooseCaption(string dir)
    {
        LyricTrack? best = null;
        int bestScore = int.MinValue;
        if (!Directory.Exists(dir))
            return null;
        foreach (string path in Directory.GetFiles(dir, "caption.*"))
        {
            int score = CaptionScore(path);
            if (score < 0)
                continue;
            LyricTrack? parsed;
            try
            {
                parsed = LyricTrack.ParseFile(path);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
            {
                continue;
            }
            if (parsed == null)
                continue;
            if (parsed.Lines.Any(line => line.Words.Count > 0))
                score += 15;
            if (score > bestScore)
            {
                best = parsed;
                bestScore = score;
            }
        }
        return best;
    }

    static int CaptionScore(string path)
    {
        string name = Path.GetFileName(path).ToLowerInvariant();
        if (name.Contains("live_chat", StringComparison.Ordinal))
            return -1;
        int score;
        if (name.EndsWith(".json3", StringComparison.Ordinal) || name.EndsWith(".srv3", StringComparison.Ordinal))
            score = 100;
        else if (name.EndsWith(".vtt", StringComparison.Ordinal))
            score = 50;
        else
            return -1;
        string lang = LanguageOf(name);
        if (lang == "en")
            score += 40;
        else if (lang.EndsWith("-orig", StringComparison.Ordinal))
            score += 30;
        else if (lang.StartsWith("en", StringComparison.Ordinal))
            score += 10;
        return score;
    }

    static string LanguageOf(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        int dot = stem.IndexOf('.');
        return dot < 0 ? stem : stem[(dot + 1)..];
    }

    async Task<PreparedSong> PrepareWavAsync(string sourceWav, string title, string dir, IProgress<string> progress, CancellationToken cancellationToken)
    {
        string instrumental = Path.Combine(dir, "instrumental.wav");
        string vocal = Path.Combine(dir, "vocal.wav");
        string contour = Path.Combine(dir, "contour.json");
        if (!File.Exists(instrumental) || !File.Exists(vocal) || !File.Exists(contour))
        {
            string python = await EnsureVenvAsync(progress, cancellationToken);
            string script = FindAnalysis("prepare_song.py");
            string models = Path.Combine(Root, "models");
            Directory.CreateDirectory(models);
            progress.Report("Separating vocals…");
            var (code, stdout, stderr) = await RunAsync(
                python,
                ["-u", script, "--input", sourceWav, "--outdir", dir, "--model-dir", models],
                dir,
                line =>
                {
                    if (line.StartsWith("STAGE\t", StringComparison.Ordinal))
                        progress.Report(line[6..].Replace('\t', ' '));
                    else if (line.StartsWith("ERROR\t", StringComparison.Ordinal))
                        progress.Report(line[6..]);
                },
                cancellationToken);
            if (code != 0)
            {
                string reason = stdout.Split('\n').LastOrDefault(line => line.StartsWith("ERROR\t", StringComparison.Ordinal))
                    ?? Tail(stderr);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(reason) ? "Melody prep failed." : reason.Replace("ERROR\t", ""));
            }
        }
        if (!File.Exists(instrumental) || !File.Exists(contour))
            throw new InvalidOperationException("Melody prep did not write an instrumental and a pitch contour.");
        progress.Report("Melody ready");
        return new PreparedSong(title, instrumental, vocal, contour);
    }

    public IReadOnlyList<YouTubeCacheEntry> ListYouTubeCache() => YouTubeCache.List(Root);

    public async Task<string> EnsureVenvAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        string venv = Path.Combine(Root, "venv");
        string python = Path.Combine(venv, "Scripts", "python.exe");
        if (File.Exists(python) && await IsUnsupportedPythonAsync(python, cancellationToken))
        {
            progress.Report("Replacing the Python 3.14 environment. The vocal model needs Python 3.13 or older.");
            Directory.Delete(venv, true);
        }
        string requirements = FindAnalysis("requirements.txt");
        string stampPath = Path.Combine(venv, ".requirements-stamp");
        string stamp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(requirements)));
        if (File.Exists(python) && File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
            return python;

        Directory.CreateDirectory(Root);
        if (!File.Exists(python))
        {
            progress.Report("Creating the melody-tools environment…");
            bool created = await TryCreateVenv(venv, progress, cancellationToken);
            if (!created || !File.Exists(python))
                throw new InvalidOperationException("Melody prep needs Python 3.10 through 3.13. Python 3.14 cannot install the vocal model. Install Python 3.13 from python.org, then load the song again.");
        }

        progress.Report("Installing melody tools. The first time takes several minutes…");
        var (code, stdout, stderr) = await RunAsync(
            python,
            ["-m", "pip", "install", "-r", requirements],
            Root,
            progress,
            cancellationToken);
        if (code != 0)
            throw new InvalidOperationException("Could not install melody tools. " + Tail(stderr + stdout));
        await File.WriteAllTextAsync(stampPath, stamp, cancellationToken);
        return python;
    }

    static async Task<bool> TryCreateVenv(string venv, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (Directory.Exists(venv))
            Directory.Delete(venv, true);
        foreach (string version in new[] { "-3.13", "-3.12", "-3.11", "-3.10" })
        {
            try
            {
                var (code, _, _) = await RunAsync("py", [version, "-m", "venv", venv], null, progress, cancellationToken);
                if (code == 0 && File.Exists(Path.Combine(venv, "Scripts", "python.exe")))
                    return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }
            if (Directory.Exists(venv))
                Directory.Delete(venv, true);
        }
        return false;
    }

    static async Task<bool> IsUnsupportedPythonAsync(string python, CancellationToken cancellationToken)
    {
        var (code, stdout, _) = await RunAsync(python, ["-c", "import sys; print(sys.version_info[0], sys.version_info[1])"], null, _ => { }, cancellationToken);
        if (code != 0)
            return true;
        string[] parts = stdout.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0] == "3" && int.TryParse(parts[1], out int minor) && minor >= 14;
    }

    static string FindAnalysis(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "analysis", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not find analysis/{fileName}.");
    }

    static string HashKey(string path)
    {
        var info = new FileInfo(path);
        string raw = info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16];
    }

    static string FolderForUrl(string url)
    {
        Match match = VideoId.Match(url);
        if (match.Success)
            return match.Groups[1].Value;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];
    }

    static string Tail(string text)
    {
        text = text.Trim();
        return text.Length <= 500 ? text : text[^500..];
    }

    static async Task<(int Code, string Stdout, string Stderr)> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        string? workingDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return await RunAsync(fileName, args, workingDirectory, line => progress?.Report(line), cancellationToken);
    }

    static async Task<(int Code, string Stdout, string Stderr)> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        string? workingDirectory,
        Action<string> onLine,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };
        foreach (string arg in args)
            start.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
                return;
            lock (stdout)
                stdout.AppendLine(e.Data);
            if (e.Data.Length > 0)
                onLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
                return;
            lock (stderr)
                stderr.AppendLine(e.Data);
        };
        if (!process.Start())
            throw new InvalidOperationException($"Could not start {fileName}.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await using CancellationTokenRegistration _ = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        });
        await process.WaitForExitAsync(cancellationToken);
        string outText;
        string errText;
        lock (stdout)
            outText = stdout.ToString();
        lock (stderr)
            errText = stderr.ToString();
        return (process.ExitCode, outText, errText);
    }
}
