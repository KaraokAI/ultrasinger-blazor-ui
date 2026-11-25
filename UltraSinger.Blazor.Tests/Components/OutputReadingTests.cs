using Bunit;
using UltraSinger.Blazor.Components.Pages.Components;
using UltraSinger.Blazor.Tests.Infrastructure;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Tests.Components;

public class OutputReadingTests : UiTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly SongDto _song = new()
    {
        Id = Guid.NewGuid(), Url = "https://youtu.be/1", Title = "Artist - Title",
        State = SongState.IN_PROGRESS, BeganProcessingAt = DateTime.Now,
    };

    private static string Log(IRenderedComponent<OutputReading> cut) => cut.Find(".font-mono").TextContent.Trim();

    /// <summary>Serves <paramref name="log"/> incrementally from whatever offset is asked for.</summary>
    private Func<int, CurrentActivityDto> Running(string log) => offset => new CurrentActivityDto
    {
        Current = _song,
        Log = new LogChunkDto { Offset = log.Length, Text = offset >= log.Length ? "" : log[offset..] },
    };

    [Fact]
    public void Idle_SaysItIsWaiting()
    {
        var cut = Render<OutputReading>();

        cut.WaitForAssertion(() => Assert.Contains(Processor.Requests, r => r.PathAndQuery.StartsWith("/api/activity")), Wait);
        Assert.Contains("Eagerly awaiting your next song", cut.Markup);
    }

    [Fact]
    public void Running_ShowsTheSongAndItsLogNewestFirst_FetchingOnlyNewOutput()
    {
        Processor.Activity = Running("[UltraSinger] one\n[UltraSinger] two\n\n");

        var cut = Render<OutputReading>();

        cut.WaitForAssertion(() => Assert.Equal("[UltraSinger] two\n[UltraSinger] one", Log(cut)), Wait);
        Assert.Contains("Currently working on: Artist - Title", cut.Find(".text-red").TextContent);

        var logLength = "[UltraSinger] one\n[UltraSinger] two\n\n".Length;
        cut.WaitForAssertion(() =>
            Assert.Contains(Processor.Requests, r => r.PathAndQuery == $"/api/activity?sinceOffset={logLength}"), Wait);
    }

    [Fact]
    public void Finishing_CollectsTheTailAndKeepsTheOutputOnScreen()
    {
        const string log = "[UltraSinger] one\n";
        Processor.Activity = Running(log);
        Processor.SongLog = (id, offset) => id == _song.Id && offset == log.Length
            ? new LogChunkDto { Offset = offset + 22, Text = "[PostProcess] finished\n" }
            : new LogChunkDto { Offset = offset };

        var cut = Render<OutputReading>();
        cut.WaitForAssertion(() => Assert.Equal("[UltraSinger] one", Log(cut)), Wait);

        Processor.Activity = _ => new CurrentActivityDto();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("[PostProcess] finished\n[UltraSinger] one", Log(cut));
            Assert.Contains("Eagerly awaiting your next song", cut.Markup);
        }, Wait);
    }

    [Fact]
    public void ANewSong_ReplacesThePreviousOutput()
    {
        Processor.Activity = Running("[UltraSinger] first song\n");
        var cut = Render<OutputReading>();
        cut.WaitForAssertion(() => Assert.Equal("[UltraSinger] first song", Log(cut)), Wait);

        var next = _song with { Id = Guid.NewGuid(), Title = "Next - Song" };
        Processor.Activity = offset => new CurrentActivityDto
        {
            Current = next,
            Log = new LogChunkDto { Offset = 25, Text = offset == 0 ? "[UltraSinger] second song\n" : "" },
        };

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("[UltraSinger] second song", Log(cut));
            Assert.Contains("Next - Song", cut.Markup);
        }, Wait);
    }

    [Fact]
    public async Task AnOutage_LeavesTheLastOutputInPlace()
    {
        Processor.Activity = Running("[UltraSinger] one\n");
        var cut = Render<OutputReading>();
        cut.WaitForAssertion(() => Assert.Equal("[UltraSinger] one", Log(cut)), Wait);

        Processor.Unreachable = true;
        var requestsBefore = Processor.Requests.Count;
        await WaitUntilAsync(() => Processor.Requests.Count > requestsBefore + 1);

        Assert.Equal("[UltraSinger] one", Log(cut));
        Assert.Contains("Artist - Title", cut.Markup);
    }
}
