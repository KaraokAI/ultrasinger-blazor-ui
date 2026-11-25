using Hangfire.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UltraSinger.Contracts;
using UltraSinger.Processor.Entities;
using UltraSinger.Processor.Services;

namespace UltraSinger.Processor.Tests;

public class JobStateReconcilerTests
{
    private readonly ISongStore _store = Substitute.For<ISongStore>();
    private readonly IStorageConnection _connection = Substitute.For<IStorageConnection>();

    private SongRecord Song(SongState state, string? jobId = "1")
    {
        var song = new SongRecord { Id = Guid.NewGuid(), Url = "u", JobId = jobId, State = state };
        _store.GetAll().Returns([song]);
        return song;
    }

    private void HangfireSays(string jobId, string? state) =>
        _connection.GetJobData(jobId).Returns(state == null ? null : new JobData { State = state });

    private void Reconcile() =>
        new JobStateReconciler(_store, NullLogger<JobStateReconciler>.Instance).Reconcile(_connection);

    [Theory]
    [InlineData(SongState.NOT_STARTED, "Succeeded", SongState.COMPLETED)]
    [InlineData(SongState.NOT_STARTED, "Failed", SongState.FAILED)]
    [InlineData(SongState.IN_PROGRESS, "Deleted", SongState.FAILED)]
    [InlineData(SongState.NOT_STARTED, "Processing", SongState.IN_PROGRESS)]
    [InlineData(SongState.UNKNOWN, "Enqueued", SongState.NOT_STARTED)]
    [InlineData(SongState.NOT_STARTED, "SomethingNew", SongState.UNKNOWN)]
    public void MapsHangfireStateOntoTheSong(SongState before, string hangfireState, SongState expected)
    {
        var song = Song(before);
        HangfireSays("1", hangfireState);

        Reconcile();

        Assert.Equal(expected, song.State);
    }

    [Fact]
    public void FinishedStates_StampCompletion()
    {
        var song = Song(SongState.IN_PROGRESS);
        HangfireSays("1", "Failed");

        Reconcile();

        Assert.NotNull(song.CompletedAt);
    }

    [Fact]
    public void NeverWalksARunningSongBackToQueued()
    {
        var song = Song(SongState.IN_PROGRESS);
        HangfireSays("1", "Enqueued");

        Reconcile();

        Assert.Equal(SongState.IN_PROGRESS, song.State);
    }

    [Fact]
    public void MissingJob_BecomesUnknown()
    {
        var song = Song(SongState.NOT_STARTED);
        HangfireSays("1", null);

        Reconcile();

        Assert.Equal(SongState.UNKNOWN, song.State);
    }

    [Theory]
    [InlineData(SongState.COMPLETED)]
    [InlineData(SongState.FAILED)]
    public void SettledSongsAreLeftAlone(SongState state)
    {
        var song = Song(state);
        HangfireSays("1", "Processing");

        Reconcile();

        Assert.Equal(state, song.State);
        _connection.DidNotReceiveWithAnyArgs().GetJobData(default!);
    }

    [Fact]
    public void SongsWithoutAJobIdAreSkipped()
    {
        var song = Song(SongState.NOT_STARTED, jobId: null);

        Reconcile();

        Assert.Equal(SongState.NOT_STARTED, song.State);
        _connection.DidNotReceiveWithAnyArgs().GetJobData(default!);
    }
}
