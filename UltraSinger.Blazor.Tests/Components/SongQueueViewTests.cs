using Bunit;
using UltraSinger.Blazor.Components.Pages.Components;
using UltraSinger.Blazor.Tests.Infrastructure;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Tests.Components;

public class SongQueueViewTests : UiTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private async Task<IRenderedComponent<SongQueueView>> RenderWithQueueAsync(params SongQueueItem[] items)
    {
        Processor.PlayQueue.AddRange(items);
        await StartQueueServiceAsync();
        return Render<SongQueueView>();
    }

    private static string[] Titles(IRenderedComponent<SongQueueView> cut) =>
        cut.FindAll("strong").Select(e => e.TextContent.Trim()).ToArray();

    private static void Click(IRenderedComponent<SongQueueView> cut, int row, string buttonTitle) =>
        cut.FindAll($"button[title='{buttonTitle}']")[row].Click();

    [Fact]
    public async Task Empty_SaysSo()
    {
        var cut = await RenderWithQueueAsync();

        Assert.Contains("Your song queue is empty.", cut.Markup);
        Assert.DoesNotContain("Clear Queue", cut.Markup);
    }

    [Fact]
    public async Task ListsItemsWithSourceBadges_AndCountsOnlyUnsungSongs()
    {
        var cut = await RenderWithQueueAsync(
            new SongQueueItem { Title = "Bohemian Rhapsody", Artist = "Queen", Source = SongSource.Local, ExtraInfo = "1975" },
            new SongQueueItem { Title = "Waterloo", Artist = "ABBA", Source = SongSource.USDB, IsSung = true, SungAt = DateTime.Now },
            new SongQueueItem { Title = "Untitled", Source = SongSource.YouTube });

        Assert.Equal(["Queen - Bohemian Rhapsody", "ABBA - Waterloo", "Untitled"], Titles(cut));
        Assert.Contains("2 songs", cut.Find("h2").TextContent);
        Assert.Contains("LOCAL", cut.Markup);
        Assert.Contains("USDB", cut.Markup);
        Assert.Contains("YouTube", cut.Markup);
        Assert.Contains("1975", cut.Markup);

        // First can't move up, last can't move down.
        Assert.True(cut.FindAll("button[title='Move Up']")[0].HasAttribute("disabled"));
        Assert.True(cut.FindAll("button[title='Move Down']")[2].HasAttribute("disabled"));
        Assert.Single(cut.FindAll("button[title='Mark as not sung']"));
    }

    [Fact]
    public async Task MovingAnItem_UpdatesTheProcessorAndTheView()
    {
        var cut = await RenderWithQueueAsync(new SongQueueItem { Title = "One" }, new SongQueueItem { Title = "Two" });

        Click(cut, 0, "Move Down");

        cut.WaitForAssertion(() => Assert.Equal(["Two", "One"], Titles(cut)), Wait);
        Assert.Equal(["Two", "One"], Processor.PlayQueue.Select(x => x.Title));
    }

    [Fact]
    public async Task TogglingSung_RoundTrips()
    {
        var cut = await RenderWithQueueAsync(new SongQueueItem { Title = "One" });

        Click(cut, 0, "Mark as sung");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button[title='Mark as not sung']")), Wait);
        Assert.Contains("0 songs", cut.Find("h2").TextContent);

        Click(cut, 0, "Mark as not sung");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button[title='Mark as sung']")), Wait);
        Assert.False(Processor.PlayQueue.Single().IsSung);
    }

    [Fact]
    public async Task RemovingAndClearing_EmptyTheQueue()
    {
        var cut = await RenderWithQueueAsync(new SongQueueItem { Title = "One" }, new SongQueueItem { Title = "Two" });

        Click(cut, 0, "Remove from Queue");
        cut.WaitForAssertion(() => Assert.Equal(["Two"], Titles(cut)), Wait);

        cut.FindAll("button").Single(b => b.TextContent.Contains("Clear Queue")).Click();
        cut.WaitForAssertion(() => Assert.Contains("Your song queue is empty.", cut.Markup), Wait);
        Assert.Empty(Processor.PlayQueue);
    }
}
