using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KaraokeVox;

public sealed class LyricTrack
{
    public const double ReadAheadSec = 0.25;

    public static LyricTrack Empty { get; } = new(Array.Empty<LyricLine>());

    static readonly Regex VttCue = new(
        @"^((?:\d+:)?\d{1,2}:\d{2}\.\d{3})\s+-->\s+((?:\d+:)?\d{1,2}:\d{2}\.\d{3})",
        RegexOptions.Compiled);

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyList<LyricLine> Lines { get; }

    public LyricTrack(IReadOnlyList<LyricLine> lines)
    {
        Lines = lines;
    }

    public LyricView At(double timeSec)
    {
        if (Lines.Count == 0)
            return default;

        int current = -1;
        for (int i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].StartSec <= timeSec)
                current = i;
            else
                break;
        }
        if (current == Lines.Count - 1 && timeSec > Lines[current].EndSec)
            current = -1;

        LyricLine? previous = current > 0
            ? Lines[current - 1]
            : current < 0 && timeSec > Lines[0].StartSec ? Lines[^1] : null;
        LyricLine? next = current >= 0 && current + 1 < Lines.Count
            ? Lines[current + 1]
            : current < 0 && timeSec <= Lines[0].StartSec ? Lines[0] : null;
        LyricLine? line = current >= 0 ? Lines[current] : null;
        return new LyricView(previous, line, next, line?.ActiveWord(timeSec) ?? -1);
    }

    public static LyricTrack? ParseFile(string path)
    {
        string text = File.ReadAllText(path);
        string ext = Path.GetExtension(path);
        if (ext.Equals(".vtt", StringComparison.OrdinalIgnoreCase))
            return FromWebVtt(text);
        return FromYouTubeJson3(text);
    }

    public static LyricTrack? FromYouTubeJson3(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array)
            return null;

        var raw = new List<RawEvent>();
        foreach (JsonElement item in events.EnumerateArray())
        {
            if (!item.TryGetProperty("segs", out JsonElement segs) || segs.ValueKind != JsonValueKind.Array)
                continue;
            double startMs = Number(item, "tStartMs");
            double durationMs = Number(item, "dDurationMs");
            var pieces = new List<RawPiece>();
            foreach (JsonElement seg in segs.EnumerateArray())
            {
                string utf8 = seg.TryGetProperty("utf8", out JsonElement text) ? text.GetString() ?? "" : "";
                pieces.Add(new RawPiece(Number(seg, "tOffsetMs"), utf8));
            }
            if (pieces.Count > 0)
                raw.Add(new RawEvent(startMs, durationMs, pieces));
        }
        raw.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        bool wordTimed = raw.Exists(item => item.Pieces.Exists(piece => piece.OffsetMs > 0));
        List<LyricLine> lines = wordTimed ? LinesFromWords(raw) : LinesFromCues(raw);
        return lines.Count == 0 ? null : new LyricTrack(lines);
    }

    public static LyricTrack? FromWebVtt(string vtt)
    {
        string[] rows = vtt.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var cues = new List<RawEvent>();
        for (int i = 0; i < rows.Length; i++)
        {
            Match match = VttCue.Match(rows[i].Trim());
            if (!match.Success)
                continue;
            var text = new StringBuilder();
            i++;
            for (; i < rows.Length && rows[i].Trim().Length > 0; i++)
            {
                if (text.Length > 0)
                    text.Append(' ');
                text.Append(rows[i].Trim());
            }
            string cleaned = Clean(StripTags(text.ToString()));
            if (IsNoise(cleaned))
                continue;
            double start = Timestamp(match.Groups[1].Value) * 1000.0;
            double end = Timestamp(match.Groups[2].Value) * 1000.0;
            cues.Add(new RawEvent(start, Math.Max(0, end - start), [new RawPiece(0, cleaned)]));
        }
        List<LyricLine> lines = LinesFromCues(cues);
        return lines.Count == 0 ? null : new LyricTrack(lines);
    }

    public static LyricTrack Load(string path)
    {
        string json = File.ReadAllText(path);
        LyricFile? dto = JsonSerializer.Deserialize<LyricFile>(json, JsonOptions);
        if (dto?.Lines == null || dto.Lines.Count == 0)
            return Empty;
        var lines = new List<LyricLine>(dto.Lines.Count);
        foreach (LyricLineFile line in dto.Lines)
        {
            var words = new List<LyricWord>();
            if (line.Words != null)
            {
                foreach (LyricWordFile word in line.Words)
                {
                    if (!string.IsNullOrWhiteSpace(word.Text))
                        words.Add(new LyricWord(word.T, word.Text.Trim()));
                }
            }
            string text = string.IsNullOrWhiteSpace(line.Text)
                ? string.Join(' ', words.Select(word => word.Text))
                : line.Text.Trim();
            if (text.Length == 0)
                continue;
            lines.Add(new LyricLine(line.T, line.End, text, words));
        }
        return lines.Count == 0 ? Empty : new LyricTrack(lines);
    }

    public void Save(string path)
    {
        var dto = new LyricFile
        {
            Lines = Lines.Select(line => new LyricLineFile
            {
                T = line.StartSec,
                End = line.EndSec,
                Text = line.Text,
                Words = line.Words.Select(word => new LyricWordFile { T = word.StartSec, Text = word.Text }).ToList()
            }).ToList()
        };
        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
    }

    static List<LyricLine> LinesFromWords(List<RawEvent> events)
    {
        var words = new List<TimedWord>();
        double last = -1;
        bool breakNext = false;
        foreach (RawEvent item in events)
        {
            foreach (RawPiece piece in item.Pieces)
            {
                bool broke = piece.Text.Contains('\n', StringComparison.Ordinal);
                string cleaned = Clean(piece.Text.Replace('\n', ' '));
                if (cleaned.Length == 0 || IsNoise(cleaned))
                {
                    if (broke)
                        breakNext = true;
                    continue;
                }
                double time = (item.StartMs + piece.OffsetMs) / 1000.0;
                if (time <= last + 0.001)
                {
                    if (broke)
                        breakNext = true;
                    continue;
                }
                words.Add(new TimedWord(time, cleaned, breakNext));
                breakNext = broke;
                last = time;
            }
        }
        return GroupWords(words);
    }

    static List<LyricLine> GroupWords(List<TimedWord> words)
    {
        var grouped = new List<List<TimedWord>>();
        var current = new List<TimedWord>();
        foreach (TimedWord word in words)
        {
            bool gap = current.Count > 0 && word.Time - current[^1].Time > 0.8;
            bool crowded = current.Count > 0 && LineLength(current) + word.Text.Length + 1 > 42;
            if (current.Count > 0 && (word.BreakBefore || gap || crowded))
            {
                grouped.Add(current);
                current = [];
            }
            current.Add(word);
        }
        if (current.Count > 0)
            grouped.Add(current);

        var lines = new List<LyricLine>(grouped.Count);
        for (int i = 0; i < grouped.Count; i++)
        {
            List<TimedWord> group = grouped[i];
            double start = group[0].Time;
            double end = i + 1 < grouped.Count ? grouped[i + 1][0].Time : group[^1].Time + 0.6;
            var lyricWords = group.Select(word => new LyricWord(word.Time, word.Text)).ToList();
            lines.Add(new LyricLine(start, end, string.Join(' ', lyricWords.Select(word => word.Text)), lyricWords));
        }
        return lines;
    }

    static List<LyricLine> LinesFromCues(List<RawEvent> events)
    {
        var cues = new List<(double Start, double End, string Text)>();
        foreach (RawEvent item in events)
        {
            string text = Clean(string.Join(' ', item.Pieces.Select(piece => piece.Text)));
            if (IsNoise(text))
                continue;
            double start = item.StartMs / 1000.0;
            double end = item.DurationMs > 0 ? start + item.DurationMs / 1000.0 : start + 2;
            if (cues.Count > 0)
            {
                (double prevStart, double prevEnd, string prevText) = cues[^1];
                bool touches = start < prevEnd + 0.05;
                if (touches && (text.StartsWith(prevText, StringComparison.Ordinal) || prevText.StartsWith(text, StringComparison.Ordinal)))
                {
                    cues[^1] = (prevStart, Math.Max(prevEnd, end), text.Length >= prevText.Length ? text : prevText);
                    continue;
                }
            }
            cues.Add((start, end, text));
        }
        return cues.Select(cue => new LyricLine(cue.Start, cue.End, cue.Text, Array.Empty<LyricWord>())).ToList();
    }

    static int LineLength(List<TimedWord> words) =>
        words.Sum(word => word.Text.Length) + Math.Max(0, words.Count - 1);

    static bool IsNoise(string text)
    {
        if (text.Length == 0 || text is "♪" or "♫")
            return true;
        if ((text[0] == '[' && text[^1] == ']') || (text[0] == '(' && text[^1] == ')'))
            return true;
        return false;
    }

    static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool space = false;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                space = builder.Length > 0;
                continue;
            }
            if (space)
            {
                builder.Append(' ');
                space = false;
            }
            builder.Append(character);
        }
        return builder.ToString().Trim();
    }

    static string StripTags(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool tag = false;
        foreach (char character in text)
        {
            if (character == '<')
            {
                tag = true;
                continue;
            }
            if (character == '>')
            {
                tag = false;
                continue;
            }
            if (!tag)
                builder.Append(character);
        }
        return builder.ToString()
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("&#39;", "'", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase);
    }

    static double Timestamp(string text)
    {
        string[] parts = text.Split(':');
        double seconds = double.Parse(parts[^1], CultureInfo.InvariantCulture);
        double minutes = double.Parse(parts[^2], CultureInfo.InvariantCulture);
        double hours = parts.Length > 2 ? double.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
        return hours * 3600 + minutes * 60 + seconds;
    }

    static double Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
            return 0;
        return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
    }

    sealed class LyricFile
    {
        public List<LyricLineFile>? Lines { get; set; }
    }

    sealed class LyricLineFile
    {
        public double T { get; set; }
        public double End { get; set; }
        public string? Text { get; set; }
        public List<LyricWordFile>? Words { get; set; }
    }

    sealed class LyricWordFile
    {
        public double T { get; set; }
        public string? Text { get; set; }
    }

    readonly record struct RawEvent(double StartMs, double DurationMs, List<RawPiece> Pieces);
    readonly record struct RawPiece(double OffsetMs, string Text);
    readonly record struct TimedWord(double Time, string Text, bool BreakBefore);
}

public sealed class LyricLine
{
    public double StartSec { get; }
    public double EndSec { get; }
    public string Text { get; }
    public IReadOnlyList<LyricWord> Words { get; }

    public LyricLine(double startSec, double endSec, string text, IReadOnlyList<LyricWord> words)
    {
        StartSec = startSec;
        EndSec = Math.Max(startSec, endSec);
        Text = text;
        Words = words;
    }

    public int ActiveWord(double timeSec)
    {
        int active = -1;
        for (int i = 0; i < Words.Count; i++)
        {
            if (Words[i].StartSec <= timeSec)
                active = i;
        }
        return active;
    }
}

public readonly record struct LyricWord(double StartSec, string Text);

public readonly record struct LyricView(LyricLine? Previous, LyricLine? Current, LyricLine? Next, int ActiveWord);
