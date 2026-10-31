namespace KaraokeVox.Tests;

public class YouTubeCacheTests
{
    [Fact]
    public void List_ReturnsReadySongsNewestFirst()
    {
        string root = Path.Combine(Path.GetTempPath(), "karaoke-vox-cache-" + Guid.NewGuid().ToString("N"));
        string dir = Path.Combine(root, "youtube", "jNQXAC9IVRw");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "title.txt"), "Me at the zoo");
        File.WriteAllText(Path.Combine(dir, "url.txt"), "https://www.youtube.com/watch?v=jNQXAC9IVRw");
        File.WriteAllText(Path.Combine(dir, "instrumental.wav"), "x");
        File.WriteAllText(Path.Combine(dir, "contour.json"), "{}");
        try
        {
            IReadOnlyList<YouTubeCacheEntry> entries = YouTubeCache.List(root);
            Assert.Single(entries);
            Assert.Equal("Me at the zoo", entries[0].Title);
            Assert.Equal("jNQXAC9IVRw", entries[0].VideoId);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExtractLinks_ReadsSeveralDroppedUrls()
    {
        string text = """
            https://youtu.be/jNQXAC9IVRw
            https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=12s
            https://music.youtube.com/watch?v=jNQXAC9IVRw
            https://www.youtube.com/shorts/M7lc1UVf-VE
            notyoutube.com/watch?v=abcdefghijk
            """;
        IReadOnlyList<YouTubeCache.YouTubeLink> links = YouTubeCache.ExtractLinks(text);
        Assert.Equal(3, links.Count);
        Assert.Equal("jNQXAC9IVRw", links[0].VideoId);
        Assert.Equal("dQw4w9WgXcQ", links[1].VideoId);
        Assert.Equal("M7lc1UVf-VE", links[2].VideoId);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", links[1].Url);
    }

    [Fact]
    public void List_SkipsFoldersWithoutMelodyPrep()
    {
        string root = Path.Combine(Path.GetTempPath(), "karaoke-vox-cache-" + Guid.NewGuid().ToString("N"));
        string dir = Path.Combine(root, "youtube", "dQw4w9WgXcQ");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "title.txt"), "Only downloaded");
        File.WriteAllText(Path.Combine(dir, "source.wav"), "x");
        try
        {
            Assert.Empty(YouTubeCache.List(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
