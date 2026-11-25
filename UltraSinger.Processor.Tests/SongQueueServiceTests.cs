using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UltraSinger.Contracts;
using UltraSinger.Processor.Constants;
using UltraSinger.Processor.Services;
using UltraSinger.Processor.Tests.Infrastructure;

namespace UltraSinger.Processor.Tests;

public sealed class SongQueueServiceTests : IDisposable
{
    private readonly TestEnvironment _env = new();
    private readonly IBackgroundJobClient _jobClient = Substitute.For<IBackgroundJobClient>();
    private readonly SqliteSongStore _store;

    public SongQueueServiceTests()
    {
        _jobClient.Create(Arg.Any<Job>(), Arg.Any<IState>()).Returns("job-1");

        _store = new SqliteSongStore(
            new SongDatabase(_env.CreateValues(), NullLogger<SongDatabase>.Instance),
            NullLogger<SqliteSongStore>.Instance);
        _store.Load();
    }

    public void Dispose() => _env.Dispose();

    private SongQueueService CreateService() => new(
        _store,
        _jobClient,
        new YoutubeMetadataService(_env.CreateValues()),
        NullLogger<SongQueueService>.Instance);

    [Fact]
    public async Task Enqueue_StoresTheSongAndSchedulesItsJobOnTheSongQueue()
    {
        var (outcome, song) = await CreateService().EnqueueAsync(new EnqueueSongRequest
        {
            Url = "https://youtu.be/a",
            Title = "Artist - Title",
            Source = SongSource.USDB,
            UsdbSongId = 7,
            UltraStarTxt = "#TITLE:Title",
        });

        Assert.Equal(EnqueueOutcome.Queued, outcome);
        Assert.Equal(SongState.NOT_STARTED, song.State);
        Assert.Equal(7, song.UsdbSongId);

        var record = _store.Get(song.Id)!;
        Assert.Equal("job-1", record.JobId);
        Assert.Equal("#TITLE:Title", record.UltraStarTxt);

        _jobClient.Received(1).Create(
            Arg.Is<Job>(job =>
                job.Type == typeof(SongProcessingJob)
                && job.Method.Name == nameof(SongProcessingJob.ProcessAsync)
                && job.Queue == Queues.SongQueue
                && (Guid)job.Args[0] == song.Id),
            Arg.Any<IState>());
    }

    [Fact]
    public async Task Enqueue_RejectsAUrlThatIsAlreadyQueued()
    {
        var service = CreateService();
        var (_, first) = await service.EnqueueAsync(new EnqueueSongRequest { Url = "https://youtu.be/a", Title = "T" });

        var (outcome, existing) = await service.EnqueueAsync(new EnqueueSongRequest { Url = "https://youtu.be/a", Title = "T" });

        Assert.Equal(EnqueueOutcome.Duplicate, outcome);
        Assert.Equal(first.Id, existing.Id);
        _jobClient.ReceivedWithAnyArgs(1).Create(default!, default!);
    }

    [Fact]
    public async Task Enqueue_AllowsReprocessingACompletedSong()
    {
        var service = CreateService();
        var (_, first) = await service.EnqueueAsync(new EnqueueSongRequest { Url = "https://youtu.be/a", Title = "T" });
        _store.Get(first.Id)!.State = SongState.COMPLETED;

        var (outcome, second) = await service.EnqueueAsync(new EnqueueSongRequest { Url = "https://youtu.be/a", Title = "T" });

        Assert.Equal(EnqueueOutcome.Queued, outcome);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task Enqueue_ResolvesAMissingTitleWithYtDlp()
    {
        var ytDlp = _env.Fake("yt-dlp", "echo 'Resolved Title'");

        var (_, song) = await CreateService().EnqueueAsync(new EnqueueSongRequest { Url = "https://youtu.be/a" });

        Assert.Equal("Resolved Title", song.Title);
        Assert.Contains("https://youtu.be/a", FakeTool.ReadArgs(ytDlp));
    }

    [Fact]
    public async Task Enqueue_StillQueuesWhenTheTitleCannotBeResolved()
    {
        _env.Fake("yt-dlp", "echo 'ERROR: nope' >&2; exit 1");

        var (outcome, song) = await CreateService().EnqueueAsync(new EnqueueSongRequest { Url = "https://youtu.be/a" });

        Assert.Equal(EnqueueOutcome.Queued, outcome);
        Assert.Null(song.Title);
    }
}
