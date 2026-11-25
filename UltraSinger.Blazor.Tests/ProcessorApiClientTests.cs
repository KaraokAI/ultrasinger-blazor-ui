using System.Net;
using UltraSinger.Blazor.Services;
using UltraSinger.Blazor.Tests.Infrastructure;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Tests;

public class ProcessorApiClientTests
{
    private readonly FakeProcessor _processor = new();
    private readonly ProcessorConnectionState _state = new();
    private readonly ProcessorApiClient _client;

    public ProcessorApiClientTests() => _client = _processor.CreateClient(_state);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(HttpStatusCode.Accepted, EnqueueResult.Queued)]
    [InlineData(HttpStatusCode.Conflict, EnqueueResult.Duplicate)]
    [InlineData(HttpStatusCode.InternalServerError, EnqueueResult.Failed)]
    public async Task Enqueue_MapsTheProcessorsResponse(HttpStatusCode status, EnqueueResult expected)
    {
        _processor.EnqueueStatus = status;

        Assert.Equal(expected, await _client.EnqueueAsync("https://youtu.be/a", cancellationToken: Ct));
    }

    [Fact]
    public async Task Enqueue_SendsTheWholeRequest()
    {
        await _client.EnqueueAsync("https://youtu.be/a", "Artist - Title", SongSource.USDB, 7, "#TITLE:Title", Ct);

        var sent = _processor.RequestBodies<EnqueueSongRequest>(HttpMethod.Post, "/api/songs").Single();
        Assert.Equal(
            new EnqueueSongRequest { Url = "https://youtu.be/a", Title = "Artist - Title", Source = SongSource.USDB, UsdbSongId = 7, UltraStarTxt = "#TITLE:Title" },
            sent);
    }

    [Fact]
    public async Task ARejectedEnqueue_IsReportedOnTheConnectionState()
    {
        _processor.EnqueueStatus = HttpStatusCode.InternalServerError;

        await _client.EnqueueAsync("https://youtu.be/a", cancellationToken: Ct);

        Assert.False(_state.IsReachable);
        Assert.Contains("500", _state.LastError);
    }

    [Fact]
    public async Task AnOutage_IsRecordedRatherThanThrown_AndClearsOnRecovery()
    {
        _processor.Unreachable = true;

        Assert.Empty(await _client.GetSongsAsync(Ct));
        Assert.Null(await _client.GetHealthAsync(Ct));
        Assert.Equal(EnqueueResult.Failed, await _client.EnqueueAsync("https://youtu.be/a", cancellationToken: Ct));
        Assert.False(await _client.AckBundleFetchedAsync(Guid.NewGuid(), Ct));
        Assert.False(_state.IsReachable);
        Assert.Contains("Connection refused", _state.LastError);

        _processor.Unreachable = false;
        await _client.GetSongsAsync(Ct);

        Assert.True(_state.IsReachable);
        Assert.Null(_state.LastError);
    }

    [Fact]
    public async Task Cancellation_StillPropagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _client.GetSongsAsync(cts.Token));
        Assert.True(_state.IsReachable, "a cancelled poll is not an outage");
    }

    [Fact]
    public async Task GetActivity_PassesTheOffset()
    {
        var song = new SongDto { Id = Guid.NewGuid(), Url = "u", State = SongState.IN_PROGRESS };
        _processor.Activity = offset => new CurrentActivityDto { Current = song, Log = new LogChunkDto { Offset = offset + 3, Text = "abc" } };

        var activity = await _client.GetActivityAsync(10, Ct);

        Assert.Equal(song.Id, activity!.Current!.Id);
        Assert.Equal(13, activity.Log.Offset);
        Assert.Contains(_processor.Requests, r => r.PathAndQuery == "/api/activity?sinceOffset=10");
    }

    [Fact]
    public async Task DownloadBundle_SavesToATempFile_OrReturnsNullWhenNotReady()
    {
        var ready = Guid.NewGuid();
        _processor.Bundles[ready] = [1, 2, 3];

        var path = await _client.DownloadBundleAsync(ready, Ct);

        try
        {
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(path!, Ct));
            Assert.Null(await _client.DownloadBundleAsync(Guid.NewGuid(), Ct));
        }
        finally
        {
            File.Delete(path!);
        }
    }

    [Fact]
    public async Task PlayQueueCommands_HitTheRightEndpoints()
    {
        var item = await _client.EnqueuePlayQueueItemAsync(new SongQueueItem { Title = "One" }, Ct);
        Assert.NotNull(item);

        Assert.True(await _client.MarkPlayQueueItemSungAsync(item.Id, true, Ct));
        Assert.True(_processor.PlayQueue.Single().IsSung);

        Assert.False(await _client.MovePlayQueueItemUpAsync(item.Id, Ct), "already at the top");
        Assert.True(await _client.RemovePlayQueueItemAsync(item.Id, Ct));
        Assert.Empty(await _client.GetPlayQueueAsync(Ct));
    }
}
