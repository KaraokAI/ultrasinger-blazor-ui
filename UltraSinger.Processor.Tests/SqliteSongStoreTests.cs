using Microsoft.Extensions.Logging.Abstractions;
using UltraSinger.Contracts;
using UltraSinger.Processor.Services;
using UltraSinger.Processor.Tests.Infrastructure;

namespace UltraSinger.Processor.Tests;

public sealed class SqliteSongStoreTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private (SqliteSongStore Store, SongDatabase Database) CreateStore()
    {
        var database = new SongDatabase(_env.CreateValues(), NullLogger<SongDatabase>.Instance);
        var store = new SqliteSongStore(database, NullLogger<SqliteSongStore>.Instance);
        store.Load();
        return (store, database);
    }

    private static void Flush(SqliteSongStore store, SongDatabase database) =>
        database.Save(store.GetDirty().Select(x => x.TakeSnapshotAndClean()).ToList());

    [Fact]
    public void FlushedRecordsSurviveARestart()
    {
        var (store, database) = CreateStore();
        var song = store.Add("https://youtu.be/a", "Artist - Title", SongSource.USDB, 42, "#TITLE:Title");
        song.State = SongState.COMPLETED;
        song.AppendLog("done");
        Flush(store, database);

        var (reloaded, _) = CreateStore();
        var restored = reloaded.Get(song.Id);

        Assert.NotNull(restored);
        Assert.Equivalent(song.ToDto(), restored.ToDto());
        Assert.Equal("#TITLE:Title", restored.UltraStarTxt);
        Assert.Contains("done", restored.ReadLogFrom(0).Text);
    }

    [Fact]
    public void Load_MarksInterruptedJobsAsFailed()
    {
        var (store, database) = CreateStore();
        var song = store.Add("https://youtu.be/a", null);
        song.State = SongState.IN_PROGRESS;
        Flush(store, database);

        var (reloaded, _) = CreateStore();
        var restored = reloaded.Get(song.Id)!;

        Assert.Equal(SongState.FAILED, restored.State);
        Assert.NotNull(restored.CompletedAt);
        Assert.Contains("restarted", restored.ReadLogFrom(0).Text);
    }

    [Theory]
    [InlineData(SongState.NOT_STARTED, true)]
    [InlineData(SongState.IN_PROGRESS, true)]
    [InlineData(SongState.COMPLETED, false)]
    [InlineData(SongState.FAILED, false)]
    public void HasActive_OnlyBlocksQueuedOrRunningSongs(SongState state, bool expected)
    {
        var (store, _) = CreateStore();
        store.Add("https://youtu.be/a", null).State = state;

        Assert.Equal(expected, store.HasActive("https://youtu.be/a"));
        Assert.False(store.HasActive("https://youtu.be/other"));
    }

    [Fact]
    public void GetAll_IsNewestFirst_AndGetCurrentIsTheRunningSong()
    {
        var (store, _) = CreateStore();
        var older = store.Add("https://youtu.be/1", null);
        Thread.Sleep(5);
        var newer = store.Add("https://youtu.be/2", null);
        older.State = SongState.IN_PROGRESS;

        Assert.Equal([newer.Id, older.Id], store.GetAll().Select(x => x.Id));
        Assert.Equal(older.Id, store.GetCurrent()?.Id);
    }

    [Fact]
    public void Remove_DeletesFromMemoryAndDisk()
    {
        var (store, database) = CreateStore();
        var song = store.Add("https://youtu.be/a", null);
        Flush(store, database);

        store.Remove(song.Id);

        Assert.Null(store.Get(song.Id));
        Assert.Null(CreateStore().Store.Get(song.Id));
    }
}
