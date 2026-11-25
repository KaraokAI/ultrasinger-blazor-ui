using UltraSinger.Contracts;

namespace UltraSinger.Processor.Tests;

public class UltraStarTxtHelperTests
{
    private static string[] Lines(string text) => text.Split(Environment.NewLine);

    [Fact]
    public void ReplacesExistingHeadersAndInsertsMissingOnesBeforeTheNotes()
    {
        const string txt = "#ARTIST:A\n#TITLE:T\n#MP3:old.mp3\n#BPM:300\n: 0 4 5 Hi\nE";

        var updated = UltraStarTxtHelper.UpdateUltraStarTxtHeaders(
            txt, "new.mp3", "video.mp4", "vocals.mp3", "instrumental.mp3", "cover.jpg");

        Assert.Equal(
            [
                "#ARTIST:A", "#TITLE:T", "#MP3:new.mp3", "#BPM:300",
                "#VIDEO:video.mp4", "#VOCALS:vocals.mp3", "#INSTRUMENTAL:instrumental.mp3", "#COVER:cover.jpg",
                ": 0 4 5 Hi", "E"
            ],
            Lines(updated));
    }

    [Fact]
    public void LeavesHeadersAloneWhenNoReplacementIsGiven()
    {
        const string txt = "#ARTIST:A\n#MP3:keep.mp3\n#VIDEO:keep.mp4\n: 0 4 5 Hi";

        var updated = UltraStarTxtHelper.UpdateUltraStarTxtHeaders(txt, null, null);

        Assert.Equal(["#ARTIST:A", "#MP3:keep.mp3", "#VIDEO:keep.mp4", ": 0 4 5 Hi"], Lines(updated));
    }

    [Fact]
    public void MatchesHeaderNamesCaseInsensitively()
    {
        var updated = UltraStarTxtHelper.UpdateUltraStarTxtHeaders("#mp3:old.mp3\n: 0 1 1 x", "new.mp3", null);

        Assert.Equal(["#MP3:new.mp3", ": 0 1 1 x"], Lines(updated));
    }

    [Fact]
    public void IgnoresHashLinesAfterTheNotesStart()
    {
        const string txt = "#TITLE:T\n: 0 1 1 x\n#MP3:not-a-header.mp3";

        var updated = UltraStarTxtHelper.UpdateUltraStarTxtHeaders(txt, "song.mp3", null);

        Assert.Equal(["#TITLE:T", "#MP3:song.mp3", ": 0 1 1 x", "#MP3:not-a-header.mp3"], Lines(updated));
    }
}
