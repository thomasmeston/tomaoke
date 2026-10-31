using System.Text.RegularExpressions;

namespace KaraokeVox;

public sealed record YouTubeCacheEntry(
    string Title,
    string VideoId,
    string Url,
    string CacheDir,
    DateTime LastPreparedUtc)
{
    public override string ToString() => Title;
}

public static class YouTubeCache
{
    static readonly Regex FolderVideoId = new(
        @"^[A-Za-z0-9_-]{11}$",
        RegexOptions.Compiled);

    static readonly Regex UrlVideoId = new(
        @"(?:youtu\.be/|youtube\.com/(?:watch\?v=|shorts/|live/|embed/))([A-Za-z0-9_-]{11})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex DroppedLink = new(
        @"(?<![A-Za-z0-9])(?:https?://)?(?:www\.|m\.|music\.)?(?:youtube\.com/(?:watch\?(?:[^\s""'<>]*&)?v=|shorts/|live/|embed/)|youtu\.be/)([A-Za-z0-9_-]{11})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public readonly record struct YouTubeLink(string VideoId, string Url);

    public static IReadOnlyList<YouTubeLink> ExtractLinks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<YouTubeLink>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var links = new List<YouTubeLink>();
        foreach (Match match in DroppedLink.Matches(text))
        {
            string id = match.Groups[1].Value;
            if (!seen.Add(id))
                continue;
            links.Add(new YouTubeLink(id, "https://www.youtube.com/watch?v=" + id));
        }
        return links;
    }

    public static IReadOnlyList<YouTubeCacheEntry> List(string karaokeRoot)
    {
        string youtubeRoot = Path.Combine(karaokeRoot, "youtube");
        if (!Directory.Exists(youtubeRoot))
            return Array.Empty<YouTubeCacheEntry>();

        var entries = new List<YouTubeCacheEntry>();
        foreach (string dir in Directory.GetDirectories(youtubeRoot))
        {
            string instrumental = Path.Combine(dir, "instrumental.wav");
            string contour = Path.Combine(dir, "contour.json");
            if (!File.Exists(instrumental) || !File.Exists(contour))
                continue;

            string folder = Path.GetFileName(dir);
            string urlPath = Path.Combine(dir, "url.txt");
            string url = File.Exists(urlPath)
                ? File.ReadAllText(urlPath).Trim()
                : FolderVideoId.IsMatch(folder)
                    ? $"https://www.youtube.com/watch?v={folder}"
                    : "";
            if (string.IsNullOrWhiteSpace(url))
                continue;

            string titlePath = Path.Combine(dir, "title.txt");
            string title = File.Exists(titlePath)
                ? File.ReadAllText(titlePath).Trim()
                : folder;
            if (string.IsNullOrWhiteSpace(title))
                title = folder;

            Match match = UrlVideoId.Match(url);
            string videoId = match.Success ? match.Groups[1].Value : folder;
            DateTime prepared = LatestWrite(instrumental, contour, Path.Combine(dir, "source.wav"));
            entries.Add(new YouTubeCacheEntry(title, videoId, url, dir, prepared));
        }

        entries.Sort((a, b) => b.LastPreparedUtc.CompareTo(a.LastPreparedUtc));
        return entries;
    }

    static DateTime LatestWrite(params string[] paths)
    {
        DateTime latest = DateTime.MinValue;
        foreach (string path in paths)
        {
            if (!File.Exists(path))
                continue;
            DateTime write = File.GetLastWriteTimeUtc(path);
            if (write > latest)
                latest = write;
        }
        return latest == DateTime.MinValue ? DateTime.UtcNow : latest;
    }
}
