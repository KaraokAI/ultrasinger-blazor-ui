using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using UltraSinger.Contracts;
using UltraSinger.Processor.Services;
using UltraSinger.Processor.Tests.Infrastructure;

namespace UltraSinger.Processor.Tests;

/// <summary>
/// HTTP-level tests against the real app (routing, middleware, controllers, DI, SQLite),
/// with only Hangfire's job client faked.
/// </summary>
public sealed class ProcessorApiTests(ProcessorAppFactory factory) : IClassFixture<ProcessorAppFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private ISongStore Store => factory.Services.GetRequiredService<ISongStore>();

    private static string UniqueUrl() => $"https://www.youtube.com/watch?v={Guid.NewGuid():N}";

    private async Task<SongDto> EnqueueAsync(string? url = null, string title = "Artist - Title")
    {
        var response = await _client.PostAsJsonAsync("api/songs", new EnqueueSongRequest { Url = url ?? UniqueUrl(), Title = title }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SongDto>(Ct))!;
    }

    [Fact]
    public async Task Api_RequiresTheApiKey()
    {
        var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("api/songs", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Api_RejectsTheWrongApiKey()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/health", Ct)).StatusCode);
    }

    [Fact]
    public async Task Health_IsHealthyForTheSandboxConfiguration()
    {
        var health = await _client.GetFromJsonAsync<ProcessorHealthDto>("api/health", Ct);

        Assert.NotNull(health);
        Assert.True(health.Healthy, string.Join("; ", health.Problems));
        Assert.False(health.OpenAICorrectionsEnabled);
    }

    [Fact]
    public async Task Enqueue_Accepts_ThenListsAndGetsTheSong()
    {
        var song = await EnqueueAsync();

        Assert.Equal(SongState.NOT_STARTED, song.State);

        var all = await _client.GetFromJsonAsync<List<SongDto>>("api/songs", Ct);
        Assert.Contains(all!, s => s.Id == song.Id);

        var fetched = await _client.GetFromJsonAsync<SongDto>($"api/songs/{song.Id}", Ct);
        Assert.Equal("Artist - Title", fetched!.Title);
    }

    [Fact]
    public async Task Enqueue_DuplicateUrlIsAConflict()
    {
        var url = UniqueUrl();
        var first = await EnqueueAsync(url);

        var response = await _client.PostAsJsonAsync("api/songs", new EnqueueSongRequest { Url = url, Title = "x" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(first.Id, (await response.Content.ReadFromJsonAsync<SongDto>(Ct))!.Id);
    }

    [Fact]
    public async Task Enqueue_BlankUrlIsABadRequest()
    {
        var response = await _client.PostAsJsonAsync("api/songs", new EnqueueSongRequest { Url = " " }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownSong_IsNotFound()
    {
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"api/songs/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"api/songs/{id}/log", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"api/songs/{id}/bundle/ack", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Log_IsIncremental()
    {
        var song = await EnqueueAsync();
        Store.Get(song.Id)!.AppendLog("first");

        var first = await _client.GetFromJsonAsync<LogChunkDto>($"api/songs/{song.Id}/log", Ct);
        Store.Get(song.Id)!.AppendLog("second");
        var second = await _client.GetFromJsonAsync<LogChunkDto>($"api/songs/{song.Id}/log?sinceOffset={first!.Offset}", Ct);

        Assert.Contains("first", first.Text);
        Assert.DoesNotContain("first", second!.Text);
        Assert.Contains("second", second.Text);
    }

    [Fact]
    public async Task Bundle_IsOnlyServedOnceCompleted_AndAckMarksItFetched()
    {
        var song = await EnqueueAsync(title: "Artist - Title");
        var record = Store.Get(song.Id)!;

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"api/songs/{song.Id}/bundle", Ct)).StatusCode);

        var bundlePath = factory.Env.Root.Combine($"{song.Id:N}.zip");
        using (var zip = ZipFile.Open(bundlePath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("Artist - Title/Artist - Title.txt");
        }
        record.BundlePath = bundlePath;
        record.State = SongState.COMPLETED;

        var response = await _client.GetAsync($"api/songs/{song.Id}/bundle", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Artist - Title.zip", response.Content.Headers.ContentDisposition?.FileNameStar);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync($"api/songs/{song.Id}/bundle/ack", null, Ct)).StatusCode);
        var fetched = await _client.GetFromJsonAsync<SongDto>($"api/songs/{song.Id}", Ct);
        Assert.NotNull(fetched!.FetchedAt);
    }

    [Fact]
    public async Task Activity_ReportsTheRunningSongAndItsLog()
    {
        var song = await EnqueueAsync();
        var record = Store.Get(song.Id)!;
        record.State = SongState.IN_PROGRESS;
        record.BeganProcessingAt = DateTime.Now.AddYears(1); // sorts ahead of anything else running
        record.AppendLog("[UltraSinger] working");

        try
        {
            var activity = await _client.GetFromJsonAsync<CurrentActivityDto>("api/activity", Ct);

            Assert.Equal(song.Id, activity!.Current?.Id);
            Assert.Contains("working", activity.Log?.Text);
        }
        finally
        {
            record.State = SongState.FAILED;
        }
    }

    [Fact]
    public async Task PlayQueue_SupportsTheFullLifecycle()
    {
        await _client.DeleteAsync("api/playqueue", Ct);

        async Task<SongQueueItem> Add(string title)
        {
            var response = await _client.PostAsJsonAsync("api/playqueue", new SongQueueItem { Title = title, Artist = "A" }, Ct);
            return (await response.Content.ReadFromJsonAsync<SongQueueItem>(Ct))!;
        }

        async Task<string[]> Titles() =>
            (await _client.GetFromJsonAsync<List<SongQueueItem>>("api/playqueue", Ct))!.Select(x => x.Title).ToArray();

        await Add("One");
        var two = await Add("Two");
        Assert.Equal(["One", "Two"], await Titles());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync($"api/playqueue/{two.Id}/up", null, Ct)).StatusCode);
        Assert.Equal(["Two", "One"], await Titles());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsJsonAsync($"api/playqueue/{two.Id}/sung", new { isSung = true }, Ct)).StatusCode);
        var items = await _client.GetFromJsonAsync<List<SongQueueItem>>("api/playqueue", Ct);
        Assert.True(items!.Single(x => x.Id == two.Id).IsSung);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"api/playqueue/{two.Id}", Ct)).StatusCode);
        Assert.Equal(["One"], await Titles());

        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"api/playqueue/{Guid.NewGuid()}/up", null, Ct)).StatusCode);
    }
}
