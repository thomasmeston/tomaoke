namespace KaraokeVox.Tests;

public class LyricTests
{
    [Fact]
    public void Json3_DropsRepeatedWordsInRollingWindows()
    {
        const string json = """
            {
              "events": [
                {"tStartMs": 1000, "dDurationMs": 2000, "segs": [
                  {"utf8": "hello "},
                  {"utf8": "there", "tOffsetMs": 400}
                ]},
                {"tStartMs": 1400, "dDurationMs": 2000, "segs": [
                  {"utf8": "there "},
                  {"utf8": "friend", "tOffsetMs": 600}
                ]},
                {"tStartMs": 5000, "dDurationMs": 800, "segs": [{"utf8": "[Music]"}]},
                {"tStartMs": 6200, "dDurationMs": 1000, "segs": [{"utf8": "again"}]}
              ]
            }
            """;

        LyricTrack? track = LyricTrack.FromYouTubeJson3(json);
        Assert.NotNull(track);
        Assert.Equal(2, track.Lines.Count);
        Assert.Equal("hello there friend", track.Lines[0].Text);
        Assert.Equal(["hello", "there", "friend"], track.Lines[0].Words.Select(word => word.Text).ToArray());
        Assert.Equal(1.0, track.Lines[0].Words[0].StartSec, 3);
        Assert.Equal(1.4, track.Lines[0].Words[1].StartSec, 3);
        Assert.Equal(2.0, track.Lines[0].Words[2].StartSec, 3);
        Assert.Equal("again", track.Lines[1].Text);
    }

    [Fact]
    public void Json3_MergesARollingLineWithoutWordTimes()
    {
        const string json = """
            {
              "events": [
                {"tStartMs": 1000, "dDurationMs": 3000, "segs": [{"utf8": "Hello"}]},
                {"tStartMs": 1500, "dDurationMs": 3000, "segs": [{"utf8": "Hello there"}]},
                {"tStartMs": 5000, "dDurationMs": 2000, "segs": [{"utf8": "Goodbye"}]}
              ]
            }
            """;

        LyricTrack? track = LyricTrack.FromYouTubeJson3(json);
        Assert.NotNull(track);
        Assert.Equal(["Hello there", "Goodbye"], track.Lines.Select(line => line.Text).ToArray());
        Assert.Equal(1.0, track.Lines[0].StartSec, 3);
        Assert.Empty(track.Lines[0].Words);
    }

    [Fact]
    public void Json3_TurnsAnEmbeddedBreakIntoOneLineAndSkipsParentheticalNoise()
    {
        const string json = """
            {
              "events": [
                {"tStartMs": 1200, "dDurationMs": 2160, "segs": [{"utf8": "in front of the\nelephants"}]},
                {"tStartMs": 4000, "dDurationMs": 800, "segs": [{"utf8": "(baaah!!)"}]}
              ]
            }
            """;

        LyricTrack? track = LyricTrack.FromYouTubeJson3(json);
        Assert.NotNull(track);
        Assert.Single(track.Lines);
        Assert.Equal("in front of the elephants", track.Lines[0].Text);
    }

    [Fact]
    public void WebVtt_ReadsCuesAndStripsTags()
    {
        const string vtt = """
            WEBVTT

            00:00:01.000 --> 00:00:04.000
            <c>Hello</c> there

            00:00:04.500 --> 00:00:06.000
            friend
            """;

        LyricTrack? track = LyricTrack.FromWebVtt(vtt);
        Assert.NotNull(track);
        Assert.Equal(["Hello there", "friend"], track.Lines.Select(line => line.Text).ToArray());
        Assert.Equal(1.0, track.Lines[0].StartSec, 3);
        Assert.Equal(4.0, track.Lines[0].EndSec, 3);
    }

    [Fact]
    public void At_FollowsTheAudibleTime()
    {
        var track = new LyricTrack([
            new LyricLine(1, 3, "one", [new LyricWord(1, "one"), new LyricWord(2, "line")]),
            new LyricLine(3, 5, "two", [])
        ]);

        LyricView before = track.At(0.2);
        Assert.Null(before.Current);
        Assert.Equal("one", before.Next?.Text);

        LyricView firstWord = track.At(1.2);
        Assert.Equal("one", firstWord.Current?.Text);
        Assert.Equal(0, firstWord.ActiveWord);
        Assert.Equal("two", firstWord.Next?.Text);

        LyricView secondWord = track.At(2.1);
        Assert.Equal(1, secondWord.ActiveWord);

        LyricView secondLine = track.At(3.2);
        Assert.Equal("two", secondLine.Current?.Text);
        Assert.Equal("one", secondLine.Previous?.Text);

        LyricView after = track.At(6);
        Assert.Null(after.Current);
        Assert.Equal("two", after.Previous?.Text);
    }

    [Fact]
    public void Save_RoundTripsWordTimes()
    {
        var track = new LyricTrack([
            new LyricLine(1.25, 3.5, "hello there", [new LyricWord(1.25, "hello"), new LyricWord(2.0, "there")])
        ]);
        string path = Path.Combine(Path.GetTempPath(), "karaoke-vox-lyrics-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            track.Save(path);
            LyricTrack loaded = LyricTrack.Load(path);
            Assert.Equal("hello there", loaded.Lines[0].Text);
            Assert.Equal(2.0, loaded.Lines[0].Words[1].StartSec, 3);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
