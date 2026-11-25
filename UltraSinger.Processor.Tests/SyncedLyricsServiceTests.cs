using Microsoft.Extensions.Logging.Abstractions;
using UltraSinger.Processor.Configuration;
using UltraSinger.Processor.Services;
using UltraSinger.Processor.Tests.Infrastructure;

namespace UltraSinger.Processor.Tests;

public sealed class SyncedLyricsServiceTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private SyncedLyricsService CreateService() =>
        new(_env.CreateValues(), NullLogger<SyncedLyricsService>.Instance);

    [Theory]
    [InlineData("Artist - Title", "Title Artist")]
    [InlineData("Artist - Title (Official Video)", "Title Artist")]
    [InlineData("Some Song [HD] (Lyrics)", "Some Song")]
    [InlineData("Just a title", "Just a title")]
    [InlineData("Artist - Title - Live", "Title - Live Artist")]
    [InlineData("", "")]
    public void BuildQuery_FlipsArtistAndTitleAndDropsNoise(string title, string expected)
    {
        Assert.Equal(expected, SyncedLyricsService.BuildQuery(title));
    }

    [Fact]
    public async Task TryGetLyrics_ReturnsConditionedSyncedLyrics()
    {
        _env.Fake("syncedlyrics", """
            printf '[ar:Someone]\n[00:01.00] Hello\n[00:02.00] Hello\n[00:03.00] World\n'
            """);

        var result = await CreateService().TryGetLyricsAsync("Someone - Song", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsSynced);
        Assert.Equal("[00:01.00] Hello\n[00:03.00] World", result.Text);
    }

    [Fact]
    public async Task TryGetLyrics_StripsTimestampsWhenConfiguredNotToSendThem()
    {
        _env.Configuration = _env.Configuration with { SendTimestampsToOpenAI = false };
        _env.Fake("syncedlyrics", "printf '[00:01.00] Hello\\n'");

        var result = await CreateService().TryGetLyricsAsync("Song", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsSynced);
        Assert.Equal("Hello", result.Text);
    }

    [Fact]
    public async Task TryGetLyrics_ReturnsNullWhenNothingFound()
    {
        // The real CLI exits 0 with empty stdout when it finds nothing.
        _env.Fake("syncedlyrics", "exit 0");

        Assert.Null(await CreateService().TryGetLyricsAsync("Song", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryGetLyrics_PassesConfiguredOptionsAndCleansUpScratchFile()
    {
        _env.Configuration = _env.Configuration with
        {
            SyncedLyricsMode = SyncedLyricsMode.SyncedOnly,
            SyncedLyricsEnhanced = true,
            SyncedLyricsLanguage = "en",
            SyncedLyricsProviders = "lrclib musixmatch",
        };
        var tool = _env.Fake("syncedlyrics", """
            touch "$3"
            echo 'Hello'
            """);

        await CreateService().TryGetLyricsAsync("Artist - Song Title", TestContext.Current.CancellationToken);

        var args = FakeTool.ReadArgs(tool);
        Assert.Equal("Song Title Artist", args[0]);
        Assert.Equal("-o", args[1]);
        Assert.False(File.Exists(args[2]), "the -o scratch file should be deleted afterwards");
        Assert.Equal(
            ["--synced-only", "--enhanced", "-l", "en", "-p", "lrclib", "musixmatch"],
            args[3..]);
    }

    [Fact]
    public async Task TryGetLyrics_PlainOnlyNeverAsksForEnhanced()
    {
        _env.Configuration = _env.Configuration with
        {
            SyncedLyricsMode = SyncedLyricsMode.PlainOnly,
            SyncedLyricsEnhanced = true,
        };
        var tool = _env.Fake("syncedlyrics", "echo 'Hello'");

        var result = await CreateService().TryGetLyricsAsync("Song", TestContext.Current.CancellationToken);

        Assert.False(result!.IsSynced);
        Assert.Contains("--plain-only", FakeTool.ReadArgs(tool));
        Assert.DoesNotContain("--enhanced", FakeTool.ReadArgs(tool));
    }

    [Fact]
    public async Task TryGetLyrics_KillsAHungLookup()
    {
        _env.Configuration = _env.Configuration with { SyncedLyricsTimeoutSeconds = 1 };
        _env.Fake("syncedlyrics", "sleep 30; echo 'too late'");

        var started = DateTime.UtcNow;
        var result = await CreateService().TryGetLyricsAsync("Song", TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10), "the lookup should have been killed at the timeout");
    }

    [Fact]
    public async Task TryGetLyrics_SurvivesAMissingExecutable()
    {
        _env.Configuration = _env.Configuration with { SyncedLyricsPath = _env.Root.Combine("does-not-exist") };

        Assert.Null(await CreateService().TryGetLyricsAsync("Song", TestContext.Current.CancellationToken));
    }
}
