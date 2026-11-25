using Bunit;
using UltraSinger.Blazor.Components.Pages.Components;
using UltraSinger.Blazor.Tests.Infrastructure;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Tests.Components;

public class AddSongTests : UiTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    public AddSongTests() => Usdb.Route = UsdbPages.Route;

    private static bool AddButtonVisible(IRenderedComponent<AddSong> cut) =>
        !cut.Find("a").ClassList.Contains("invisible");

    private IEnumerable<EnqueueSongRequest> Enqueued() =>
        Processor.RequestBodies<EnqueueSongRequest>(HttpMethod.Post, "/api/songs");

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=fJ9rUzIMcZQ", true)]
    [InlineData("https://youtu.be/fJ9rUzIMcZQ", true)]
    [InlineData("  https://m.youtube.com/watch?v=fJ9rUzIMcZQ  ", true)]
    [InlineData("https://usdb.animux.de/index.php?link=detail&id=123", true)]
    [InlineData("123", true)]
    [InlineData("https://example.com/video", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void AddButton_OnlyAppearsForYouTubeOrUsdbInput(string input, bool expected)
    {
        var cut = Render<AddSong>();

        cut.Find("input").Change(input);

        Assert.Equal(expected, AddButtonVisible(cut));
    }

    [Fact]
    public void AddingAYouTubeUrl_QueuesItAndClearsTheBox()
    {
        var cut = Render<AddSong>();
        cut.Find("input").Change("  https://youtu.be/fJ9rUzIMcZQ ");

        cut.Find("a").Click();

        cut.WaitForAssertion(() => Assert.Equal("https://youtu.be/fJ9rUzIMcZQ", Enqueued().Single().Url), Wait);
        Assert.Equal(SongSource.YouTube, Enqueued().Single().Source);
        cut.WaitForAssertion(() => Assert.Equal(string.Empty, cut.Find("input").GetAttribute("value")), Wait);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("https://usdb.animux.de/index.php?link=detail&id=123")]
    public void AddingAUsdbSong_QueuesItWithItsNotes(string input)
    {
        var cut = Render<AddSong>();
        cut.Find("input").Change(input);

        cut.Find("a").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Enqueued().Single();
            Assert.Equal(SongSource.USDB, request.Source);
            Assert.Equal(123, request.UsdbSongId);
            Assert.Equal("Queen - Bohemian Rhapsody", request.Title);
            Assert.Equal("https://www.youtube.com/watch?v=fJ9rUzIMcZQ", request.Url);
            Assert.Equal(UsdbPages.BohemianTxt, request.UltraStarTxt);
        }, Wait);
    }

    [Fact]
    public void AnUnresolvableUsdbSong_IsNotQueued()
    {
        var cut = Render<AddSong>();
        cut.Find("input").Change("999");

        cut.Find("a").Click();

        cut.WaitForAssertion(() => Assert.Equal(string.Empty, cut.Find("input").GetAttribute("value")), Wait);
        Assert.Empty(Enqueued());
    }
}
