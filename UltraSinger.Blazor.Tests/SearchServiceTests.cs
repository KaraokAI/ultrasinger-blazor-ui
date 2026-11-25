using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UltraSinger.Blazor.Configuration;
using UltraSinger.Blazor.Exceptions;
using UltraSinger.Blazor.Services;
using UltraSinger.Blazor.Tests.Infrastructure;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Tests;

public sealed class SearchServiceTests : IDisposable
{
    private readonly LibraryFixture _library = new();
    private readonly RoutingHandler _usdb = new(UsdbPages.Route);
    private readonly RoutingHandler _youTube = new();

    public void Dispose() => _library.Dispose();

    private YouTubeAPIService CreateYouTube() => new(
        Options.Create(new YouTubeApiConfiguration { ApiKey = "main", BackupKey = "backup" }),
        new HttpClient(_youTube));

    private UnifiedSearchService CreateUnified() => new(
        new LocalLibraryService(Options.Create(new LibraryConfiguration { LocalPath = _library.Path }), NullLogger<LocalLibraryService>.Instance),
        new UsdbService(Options.Create(new UsdbConfiguration { BaseUrl = "http://usdb.test" }), NullLogger<UsdbService>.Instance, _usdb),
        CreateYouTube(),
        NullLogger<UnifiedSearchService>.Instance);

    [Fact]
    public async Task YouTube_MapsResultsAndEscapesTheQuery()
    {
        _youTube.Route = (_, _) => YouTubeResponses.Search(("abc123", "Queen - Bohemian Rhapsody (Official Video)"));

        var results = await CreateYouTube().SearchVideosAsync("queen & co");

        var video = Assert.Single(results);
        Assert.Equal("abc123", video.VideoId);
        Assert.Equal("Queen - Bohemian Rhapsody (Official Video)", video.Title);
        Assert.Equal("https://i.ytimg.com/vi/abc123/default.jpg", video.ThumbnailUrl);
        Assert.Contains("q=queen%20%26%20co", _youTube.Requests.Single().PathAndQuery);
    }

    [Fact]
    public async Task YouTube_FallsBackToTheBackupKeyWhenTheMainOneIsRejected()
    {
        _youTube.Route = (request, _) => YouTubeResponses.KeyOf(request) == "main"
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : YouTubeResponses.Search(("abc123", "Song"));

        var results = await CreateYouTube().SearchVideosAsync("song");

        Assert.Single(results);
        Assert.Equal(["main", "backup"], _youTube.Requests.Select(r => System.Web.HttpUtility.ParseQueryString(r.PathAndQuery.Split('?')[1])["key"]));
    }

    [Fact]
    public async Task YouTube_ThrowsRateLimitedWhenBothKeysAreRejected()
    {
        _youTube.Route = (_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden);

        await Assert.ThrowsAsync<YoutubeRateLimitException>(() => CreateYouTube().SearchVideosAsync("song"));
    }

    [Fact]
    public async Task Unified_OrdersLocalThenUsdbThenYouTube_AndFlagsSongsYouAlreadyHave()
    {
        _library.AddSong("Queen", "Bohemian Rhapsody");
        _youTube.Route = (_, _) => YouTubeResponses.Search(
            ("v1", "Queen - Bohemian Rhapsody (Official Video)"),
            ("v2", "Queen - Somebody To Love"));

        var results = await CreateUnified().SearchAsync("Queen");

        Assert.Equal(
            [
                (SongSource.Local, "Queen - Bohemian Rhapsody", false),
                (SongSource.USDB, "Queen - Bohemian Rhapsody", true),
                (SongSource.USDB, "Queen - Don't Stop Me Now", false),
                (SongSource.YouTube, "Queen - Bohemian Rhapsody (Official Video)", true),
                (SongSource.YouTube, "Queen - Somebody To Love", false),
            ],
            results.Select(r => (r.Source, r.DisplayTitle, r.AlreadyLocal)));

        var youTube = results.First(r => r.Source == SongSource.YouTube);
        Assert.Equal("https://www.youtube.com/watch?v=v1", youTube.Url);
        Assert.Equal(123, results.First(r => r.Source == SongSource.USDB).UsdbSongId);
    }

    [Fact]
    public async Task Unified_StillFlagsLocalCopiesWhenLocalResultsAreHidden()
    {
        _library.AddSong("Queen", "Bohemian Rhapsody");

        var results = await CreateUnified().SearchAsync("Queen", includeLocal: false, includeYoutube: false);

        Assert.DoesNotContain(results, r => r.Source == SongSource.Local);
        Assert.True(results.Single(r => r.Title == "Bohemian Rhapsody").AlreadyLocal);
    }

    [Fact]
    public async Task Unified_OneFailingSourceDoesNotSinkTheOthers()
    {
        _library.AddSong("Queen", "Bohemian Rhapsody");
        _usdb.Route = (_, _) => throw new HttpRequestException("usdb is down");
        _youTube.Route = (_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden);

        var results = await CreateUnified().SearchAsync("Queen");

        Assert.Equal([SongSource.Local], results.Select(r => r.Source));
    }

    [Fact]
    public async Task Unified_BlankQueryDoesNothing()
    {
        Assert.Empty(await CreateUnified().SearchAsync("  "));
        Assert.Empty(_usdb.Requests);
        Assert.Empty(_youTube.Requests);
    }
}
