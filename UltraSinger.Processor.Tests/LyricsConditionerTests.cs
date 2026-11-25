using UltraSinger.Processor.Services;

namespace UltraSinger.Processor.Tests;

public class LyricsConditionerTests
{
    [Fact]
    public void Condition_StripsLrcIdTags()
    {
        const string raw = "[ar:Some Artist]\n[ti:Some Title]\n[by:someone]\n[00:01.00] Hello";

        Assert.Equal("[00:01.00] Hello", LyricsConditioner.Condition(raw, keepTimestamps: true));
    }

    [Fact]
    public void Condition_CanDropTimestamps()
    {
        const string raw = "[00:01.00] Hello\n[00:02.50] World";

        Assert.Equal("Hello\nWorld", LyricsConditioner.Condition(raw, keepTimestamps: false));
    }

    [Fact]
    public void Condition_RemovesEnhancedWordTimings()
    {
        const string raw = "[00:01.00] <00:01.00> Hello <00:01.50> world";

        Assert.Equal("[00:01.00] Hello world", LyricsConditioner.Condition(raw, keepTimestamps: true));
    }

    [Theory]
    [InlineData("♪")]
    [InlineData("[Instrumental]")]
    [InlineData("(music)")]
    [InlineData("interlude")]
    public void Condition_DropsInstrumentalMarkers(string marker)
    {
        var raw = $"[00:01.00] Hello\n[00:05.00] {marker}\n[00:09.00] Goodbye";

        Assert.Equal("Hello\nGoodbye", LyricsConditioner.Condition(raw, keepTimestamps: false));
    }

    [Fact]
    public void Condition_CollapsesConsecutiveDuplicateLinesOnly()
    {
        const string raw = "[00:01.00] Hey\n[00:02.00] hey\n[00:03.00] Ho\n[00:04.00] Hey";

        Assert.Equal(
            "[00:01.00] Hey\n[00:03.00] Ho\n[00:04.00] Hey",
            LyricsConditioner.Condition(raw, keepTimestamps: true));
    }

    [Fact]
    public void Condition_FoldsTypographicPunctuationToAscii()
    {
        Assert.Equal(
            "It's \"fine\" - ok",
            LyricsConditioner.Condition("It’s “fine” – ok", keepTimestamps: false));
    }

    [Fact]
    public void Condition_HandlesWindowsLineEndingsAndBlankLines()
    {
        const string raw = "Line one\r\n\r\n   \r\nLine two\r\n";

        Assert.Equal("Line one\nLine two", LyricsConditioner.Condition(raw, keepTimestamps: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Condition_EmptyInputGivesEmptyOutput(string raw)
    {
        Assert.Equal(string.Empty, LyricsConditioner.Condition(raw, keepTimestamps: true));
    }

    [Theory]
    [InlineData("[00:12.34] Words", true)]
    [InlineData("[01:02.345] Words", true)]
    [InlineData("Plain words\nMore words", false)]
    [InlineData("[ar:Artist]\nPlain", false)]
    public void IsSynced_DetectsLineTimestamps(string lyrics, bool expected)
    {
        Assert.Equal(expected, LyricsConditioner.IsSynced(lyrics));
    }
}
