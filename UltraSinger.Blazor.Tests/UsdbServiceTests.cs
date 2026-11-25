using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UltraSinger.Blazor.Configuration;
using UltraSinger.Blazor.Services;
using UltraSinger.Blazor.Tests.Infrastructure;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Tests;

public class UsdbServiceTests
{
    private readonly RoutingHandler _usdb = new(UsdbPages.Route);

    private UsdbService CreateService(string? username = null, string? password = null) =>
        new(Options.Create(new UsdbConfiguration { BaseUrl = "http://usdb.test", Username = username, Password = password }),
            NullLogger<UsdbService>.Instance,
            _usdb);

    [Fact]
    public void ParseSearchResults_ReadsEveryColumn()
    {
        var results = UsdbService.ParseSearchResultsHtml(UsdbPages.SearchResults);

        Assert.Equal(2, results.Count);

        var bohemian = results[0];
        Assert.Equal(123, bohemian.Id);
        Assert.Equal("Queen", bohemian.Artist);
        Assert.Equal("Bohemian Rhapsody", bohemian.Title);
        Assert.Equal("Rock", bohemian.Genre);
        Assert.Equal(1975, bohemian.Year);
        Assert.Equal("SingStar", bohemian.Edition);
        Assert.True(bohemian.GoldenNotes);
        Assert.Equal("English", bohemian.Language);
        Assert.Equal(4.5, bohemian.Rating);
        Assert.Equal(1234, bohemian.Views);
    }

    [Fact]
    public void ParseSearchResults_HandlesStarRatingsEntitiesAndBlanks()
    {
        var dontStop = UsdbService.ParseSearchResultsHtml(UsdbPages.SearchResults)[1];

        Assert.Equal(456, dontStop.Id);
        Assert.Equal("Don't Stop Me Now", dontStop.Title);
        Assert.Null(dontStop.Genre);
        Assert.Null(dontStop.Year);
        Assert.Null(dontStop.Edition);
        Assert.False(dontStop.GoldenNotes);
        Assert.Equal(3, dontStop.Rating);
        Assert.Equal(1000, dontStop.Views);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>No table here</body></html>")]
    [InlineData(UsdbPages.EmptySearchResults)]
    public void ParseSearchResults_ToleratesPagesWithNoResults(string html)
    {
        Assert.Empty(UsdbService.ParseSearchResultsHtml(html));
    }

    [Fact]
    public void ParseSongDetails_ReadsTheUnlabelledHeadRowAndMetadata()
    {
        var details = UsdbService.ParseSongDetailsHtml(UsdbPages.BohemianDetails, 123);

        Assert.Equal(123, details.Id);
        Assert.Equal("Queen", details.Artist);
        Assert.Equal("Bohemian Rhapsody", details.Title);
        Assert.Equal(310.5, details.Bpm);
        Assert.Equal(1200, details.Gap);
        Assert.Equal(1975, details.Year);
        Assert.Equal("Rock", details.Genre);
        Assert.Equal("English", details.Language);
        Assert.Equal("https://www.youtube.com/watch?v=fJ9rUzIMcZQ", details.YoutubeUrl);
    }

    [Theory]
    [InlineData("https://youtu.be/fJ9rUzIMcZQ")]
    [InlineData("https://www.youtube.com/embed/fJ9rUzIMcZQ")]
    [InlineData("https://www.youtube.com/shorts/fJ9rUzIMcZQ")]
    public void ParseSongDetails_NormalisesYouTubeLinkFormats(string link)
    {
        var details = UsdbService.ParseSongDetailsHtml($"""<a href="{link}">video</a>""", 1);

        Assert.Equal("https://www.youtube.com/watch?v=fJ9rUzIMcZQ", details.YoutubeUrl);
    }

    [Fact]
    public async Task TryBuildEnqueueRequest_CombinesDetailsAndTxt()
    {
        var request = await CreateService().TryBuildEnqueueRequestAsync(123);

        Assert.Equal(
            new EnqueueSongRequest
            {
                Url = "https://www.youtube.com/watch?v=fJ9rUzIMcZQ",
                Title = "Queen - Bohemian Rhapsody",
                Source = SongSource.USDB,
                UsdbSongId = 123,
                UltraStarTxt = UsdbPages.BohemianTxt,
            },
            request);
    }

    [Fact]
    public async Task TryBuildEnqueueRequest_IsNullWithoutAVideo()
    {
        _usdb.Route = (request, body) => request.RequestUri!.Query switch
        {
            "?link=detail&id=123" => RoutingHandler.Html("<table><tr class='list_head'><td>Queen</td><td>Song</td></tr></table>"),
            "?link=gettxt&id=123" => RoutingHandler.Html(UsdbPages.GetTxtPage(UsdbPages.BohemianTxt)),
            _ => null,
        };

        Assert.Null(await CreateService().TryBuildEnqueueRequestAsync(123));
    }

    [Theory]
    [InlineData("Queen - Bohemian", "Queen", "Bohemian")]
    [InlineData("Queen:Bohemian", "Queen", "Bohemian")]
    [InlineData("Bohemian", "", "Bohemian")]
    public async Task Search_SplitsArtistAndTitleIntoTheForm(string query, string interpret, string title)
    {
        await CreateService().SearchAsync(query);

        var form = System.Web.HttpUtility.ParseQueryString(_usdb.Requests.First(r => r.PathAndQuery == "/?link=list").Body!);
        Assert.Equal(interpret, form["interpret"]);
        Assert.Equal(title, form["title"]);
    }

    [Fact]
    public async Task Search_RetriesATitleOnlyQueryAsAnArtist()
    {
        _usdb.Route = (request, body) => body!.Contains("interpret=Queen")
            ? RoutingHandler.Html(UsdbPages.SearchResults)
            : RoutingHandler.Html(UsdbPages.EmptySearchResults);

        var results = await CreateService().SearchAsync("Queen");

        Assert.Equal(2, results.Count);
        Assert.Equal(2, _usdb.Requests.Count);
    }

    [Fact]
    public async Task Search_LogsInOnceWhenCredentialsAreConfigured()
    {
        _usdb.Route = (request, body) => request.RequestUri!.Query == "?link=login"
            ? RoutingHandler.Html("<a href='?link=logout'>Logout</a>")
            : UsdbPages.Route(request, body);

        var service = CreateService("user", "pass");
        await service.SearchAsync("Queen - Bohemian");
        await service.SearchAsync("Queen - Bohemian");

        var logins = _usdb.Requests.Where(r => r.PathAndQuery == "/?link=login").ToList();
        Assert.Single(logins);
        Assert.Contains("user=user", logins[0].Body);
    }

    [Fact]
    public async Task Search_WithoutCredentialsNeverTriesToLogIn()
    {
        await CreateService().SearchAsync("Queen - Bohemian");

        Assert.DoesNotContain(_usdb.Requests, r => r.PathAndQuery.Contains("login"));
    }
}
