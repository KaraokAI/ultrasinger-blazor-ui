using Bunit;
using UltraSinger.Blazor.Components.Pages.Components;
using UltraSinger.Blazor.Tests.Infrastructure;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Tests.Components;

public class ProcessorStatusTests : UiTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public void Healthy_ShowsNoBanner()
    {
        var cut = Render<ProcessorStatus>();

        cut.WaitForAssertion(() => Assert.Contains(Processor.Requests, r => r.PathAndQuery == "/api/health"), Wait);
        Assert.Empty(cut.FindAll(".processor-banner"));
    }

    [Fact]
    public void Misconfigured_ListsTheProblems()
    {
        Processor.Health = new ProcessorHealthDto { Healthy = false, Problems = ["PythonExecutable not found.", "Library missing."] };

        var cut = Render<ProcessorStatus>();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find(".processor-banner-warn");
            Assert.Contains("misconfigured: PythonExecutable not found. Library missing.", banner.TextContent);
        }, Wait);
    }

    [Fact]
    public void Unreachable_ShowsTheConnectionError()
    {
        Processor.Unreachable = true;

        var cut = Render<ProcessorStatus>();

        cut.WaitForAssertion(() =>
            Assert.Contains("Can't reach the processor: Connection refused", cut.Find(".processor-banner").TextContent), Wait);
    }
}

public class HistoryTests : UiTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public void Empty_SaysSo()
    {
        var cut = Render<History>();

        cut.WaitForAssertion(() => Assert.Contains("No active or completed downloads yet.", cut.Markup), Wait);
    }

    [Fact]
    public void ListsSongsWithTheirStateAndDeliveryStatus()
    {
        Processor.Songs.AddRange(
        [
            new SongDto { Id = Guid.NewGuid(), Url = "https://youtu.be/1", Title = "Fetched &amp; Ready", State = SongState.COMPLETED, FetchedAt = DateTime.Now },
            new SongDto { Id = Guid.NewGuid(), Url = "https://youtu.be/2", Title = "Awaiting Download", State = SongState.COMPLETED, Source = SongSource.USDB },
            new SongDto { Id = Guid.NewGuid(), Url = "https://youtu.be/3", Title = null, State = SongState.FAILED },
            new SongDto { Id = Guid.NewGuid(), Url = "https://youtu.be/4", Title = "Running", State = SongState.IN_PROGRESS, BeganProcessingAt = DateTime.Now.AddMinutes(-2) },
        ]);

        var cut = Render<History>();

        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("li").Count), Wait);
        Assert.Contains("(4)", cut.Find("h3").TextContent);

        var rows = cut.FindAll("li");
        Assert.Contains("Ready to sing", rows[0].TextContent);
        Assert.Contains("Fetched & Ready", rows[0].TextContent);
        Assert.Contains("Downloading...", rows[1].TextContent);
        Assert.Contains("USDB", rows[1].TextContent);
        Assert.NotNull(rows[2].QuerySelector("[title=Failed]"));
        Assert.Contains("https://youtu.be/3", rows[2].TextContent);
        Assert.NotNull(rows[3].QuerySelector("[title=Processing]"));
        Assert.Contains("(02:", rows[3].TextContent);

        // Numbered newest (top) = highest.
        Assert.StartsWith("4.", rows[0].QuerySelector(".font-mono")!.TextContent);
    }
}
