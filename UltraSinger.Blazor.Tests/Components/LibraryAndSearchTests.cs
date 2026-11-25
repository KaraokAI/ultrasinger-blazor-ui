using System.Net;
using Bunit;
using UltraSinger.Blazor.Components.Pages.Components;
using UltraSinger.Blazor.Tests.Infrastructure;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Tests.Components;

public class SongLibraryViewTests : UiTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static string[] Songs(IRenderedComponent<SongLibraryView> cut) =>
        cut.FindAll("strong").Select(e => e.TextContent.Trim()).ToArray();

    [Fact]
    public void ListsTheLibrarySortedWithMediaBadges()
    {
        Library.AddSong("Queen", "Bohemian Rhapsody", "#MP3:a.mp3\n#YEAR:1975\n", "a.mp3");
        Library.AddSong("ABBA", "Waterloo", "#VIDEO:v.mp4\n", "v.mp4");

        var cut = Render<SongLibraryView>();

        Assert.Equal(["ABBA - Waterloo", "Queen - Bohemian Rhapsody"], Songs(cut));
        Assert.Contains("2 songs", cut.Find("h2").TextContent);
        Assert.Contains("🎬 Video", cut.FindAll(".flex-wrap")[0].TextContent);
        Assert.Contains("🎵 Audio", cut.FindAll(".flex-wrap")[1].TextContent);
        Assert.Contains("1975", cut.Markup);
    }

    [Fact]
    public void Filtering_NarrowsAsYouType_AndClearResets()
    {
        Library.AddSong("Queen", "Bohemian Rhapsody");
        Library.AddSong("ABBA", "Waterloo");
        var cut = Render<SongLibraryView>();

        cut.Find("input").Input("queen");
        Assert.Equal(["Queen - Bohemian Rhapsody"], Songs(cut));

        cut.Find("input").Input("metallica");
        Assert.Contains("No songs matched \"metallica\"", cut.Markup);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "✕").Click();
        Assert.Equal(2, Songs(cut).Length);
    }

    [Fact]
    public void Queueing_AddsTheLocalFileToThePlayQueue()
    {
        var txt = Library.AddSong("Queen", "Bohemian Rhapsody", "#YEAR:1975\n");
        var cut = Render<SongLibraryView>();

        cut.FindAll("button").Single(b => b.TextContent.Contains("Queue")).Click();

        Assert.Contains("Queued: Queen - Bohemian Rhapsody", cut.Markup);
        cut.WaitForAssertion(() =>
        {
            var item = Assert.Single(Processor.PlayQueue);
            Assert.Equal(("Queen", "Bohemian Rhapsody", SongSource.Local, "1975", txt),
                (item.Artist, item.Title, item.Source, item.ExtraInfo, item.FilePath));
        }, Wait);
    }

    [Fact]
    public void Rescan_PicksUpNewSongs()
    {
        var cut = Render<SongLibraryView>();
        Assert.Contains("No songs found in the local library.", cut.Markup);

        Library.AddSong("Queen", "Bohemian Rhapsody");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Rescan Library")).Click();

        Assert.Equal(["Queen - Bohemian Rhapsody"], Songs(cut));
        Assert.Contains("Library refreshed: 1 songs found.", cut.Markup);
    }
}

public class YouTubeSearchTests : UiTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    public YouTubeSearchTests()
    {
        Usdb.Route = UsdbPages.Route;
        YouTube.Route = (_, _) => YouTubeResponses.Search(("v1", "Queen - Somebody To Love"));
        Library.AddSong("Queen", "Bohemian Rhapsody");
    }

    private IRenderedComponent<YouTubeSearch> Search(string query)
    {
        var cut = Render<YouTubeSearch>();
        cut.Find("input").Change(query);
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".space-y-2 > .space-y-2 > div")), Wait);
        return cut;
    }

    /// <summary>A result row, by its source badge (LOCAL/USDB/YouTube) and title.</summary>
    private static AngleSharp.Dom.IElement Row(IRenderedComponent<YouTubeSearch> cut, string source, string title) =>
        cut.FindAll(".space-y-2 > .space-y-2 > div").Single(e =>
            e.QuerySelector("span")!.TextContent.Trim() == source
            && e.QuerySelector("strong")!.TextContent.Trim() == title);

    private IEnumerable<EnqueueSongRequest> Enqueued() =>
        Processor.RequestBodies<EnqueueSongRequest>(HttpMethod.Post, "/api/songs");

    [Fact]
    public void ShowsResultsFromEverySource_MarkingSongsYouAlreadyHave()
    {
        var cut = Search("Queen");

        Assert.Contains("Queue", Row(cut, "LOCAL", "Queen - Bohemian Rhapsody").TextContent);

        var usdbCopy = Row(cut, "USDB", "Queen - Bohemian Rhapsody");
        Assert.Contains("Already Have It", usdbCopy.TextContent);
        Assert.Contains("★ Gold", usdbCopy.TextContent);
        Assert.Null(usdbCopy.QuerySelector("button"));

        Assert.Contains("Request", Row(cut, "USDB", "Queen - Don't Stop Me Now").TextContent);
        Assert.Contains("Request", Row(cut, "YouTube", "Queen - Somebody To Love").TextContent);
    }

    [Fact]
    public void RequestingAYouTubeResult_QueuesItForProcessing()
    {
        var cut = Search("Queen");

        Row(cut, "YouTube", "Queen - Somebody To Love").QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => Assert.Contains("Queued YouTube song: Queen - Somebody To Love", cut.Markup), Wait);
        var request = Enqueued().Single();
        Assert.Equal(("https://www.youtube.com/watch?v=v1", "Queen - Somebody To Love", SongSource.YouTube),
            (request.Url, request.Title, request.Source));
    }

    [Fact]
    public void RequestingAUsdbResult_QueuesItWithItsNotes()
    {
        // Only song 123 has a detail page in the fixture, so present it as not-yet-local.
        Library.Dispose();
        var cut = Search("Queen");

        Row(cut, "USDB", "Queen - Bohemian Rhapsody").QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => Assert.Contains("Queued USDB song: Queen - Bohemian Rhapsody", cut.Markup), Wait);
        Assert.Equal(UsdbPages.BohemianTxt, Enqueued().Single().UltraStarTxt);
    }

    [Fact]
    public void RequestingSomethingAlreadyQueued_SaysSo()
    {
        Processor.EnqueueStatus = HttpStatusCode.Conflict;
        var cut = Search("Queen");

        Row(cut, "YouTube", "Queen - Somebody To Love").QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => Assert.Contains("already in queue or processing", cut.Markup), Wait);
    }

    [Fact]
    public void QueueingALocalResult_AddsItToThePlayQueue()
    {
        var cut = Search("Queen");

        Row(cut, "LOCAL", "Queen - Bohemian Rhapsody").QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => Assert.Contains("Queued local song: Queen - Bohemian Rhapsody", cut.Markup), Wait);
        cut.WaitForAssertion(() => Assert.Equal("Bohemian Rhapsody", Assert.Single(Processor.PlayQueue).Title), Wait);
        Assert.Empty(Enqueued());
    }

    [Fact]
    public void NoResults_SaysSo()
    {
        Usdb.Route = (_, _) => RoutingHandler.Html(UsdbPages.EmptySearchResults);
        YouTube.Route = (_, _) => YouTubeResponses.Search();

        var cut = Render<YouTubeSearch>();
        cut.Find("input").Change("zzzz");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("No results found", cut.Markup), Wait);
    }
}
