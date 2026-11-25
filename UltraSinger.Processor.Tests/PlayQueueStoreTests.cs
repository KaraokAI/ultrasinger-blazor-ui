using Microsoft.Extensions.Logging.Abstractions;
using UltraSinger.Contracts;
using UltraSinger.Processor.Services;
using UltraSinger.Processor.Tests.Infrastructure;

namespace UltraSinger.Processor.Tests;

public sealed class PlayQueueStoreTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private PlayQueueStore CreateStore()
    {
        var store = new PlayQueueStore(
            new PlayQueueDatabase(_env.CreateValues(), NullLogger<PlayQueueDatabase>.Instance),
            NullLogger<PlayQueueStore>.Instance);
        store.Load();
        return store;
    }

    private static string[] Titles(PlayQueueStore store) => store.GetAll().Select(x => x.Title).ToArray();

    [Fact]
    public void Add_AppendsInOrderAndPersists()
    {
        var store = CreateStore();
        store.Add("One", "A", SongSource.Local, "2001", "/one.txt");
        store.Add("Two", "B", SongSource.USDB, null, null);

        Assert.Equal(["One", "Two"], Titles(store));

        var reloaded = CreateStore().GetAll();
        Assert.Equal(["One", "Two"], reloaded.Select(x => x.Title));
        Assert.Equal("/one.txt", reloaded[0].FilePath);
        Assert.Equal(SongSource.USDB, reloaded[1].Source);
    }

    [Fact]
    public void MoveUpAndDown_SwapNeighboursAndPersist()
    {
        var store = CreateStore();
        store.Add("One", "", SongSource.Local, null, null);
        var two = store.Add("Two", "", SongSource.Local, null, null);
        store.Add("Three", "", SongSource.Local, null, null);

        Assert.True(store.MoveUp(two.Id));
        Assert.Equal(["Two", "One", "Three"], Titles(store));

        Assert.True(store.MoveDown(two.Id));
        Assert.True(store.MoveDown(two.Id));
        Assert.Equal(["One", "Three", "Two"], Titles(store));

        Assert.Equal(["One", "Three", "Two"], Titles(CreateStore()));
    }

    [Fact]
    public void MovesPastTheEnds_AndUnknownIds_AreRejected()
    {
        var store = CreateStore();
        var first = store.Add("One", "", SongSource.Local, null, null);
        var last = store.Add("Two", "", SongSource.Local, null, null);

        Assert.False(store.MoveUp(first.Id));
        Assert.False(store.MoveDown(last.Id));
        Assert.False(store.MoveUp(Guid.NewGuid()));
        Assert.False(store.Remove(Guid.NewGuid()));
        Assert.False(store.MarkSung(Guid.NewGuid(), true));
    }

    [Fact]
    public void MarkSung_SetsAndClearsTheTimestamp()
    {
        var store = CreateStore();
        var item = store.Add("One", "", SongSource.Local, null, null);

        store.MarkSung(item.Id, true);
        var sung = CreateStore().GetAll().Single();
        Assert.True(sung.IsSung);
        Assert.NotNull(sung.SungAt);

        store.MarkSung(item.Id, false);
        var unsung = CreateStore().GetAll().Single();
        Assert.False(unsung.IsSung);
        Assert.Null(unsung.SungAt);
    }

    [Fact]
    public void RemoveAndClear_Persist()
    {
        var store = CreateStore();
        var one = store.Add("One", "", SongSource.Local, null, null);
        store.Add("Two", "", SongSource.Local, null, null);

        store.Remove(one.Id);
        Assert.Equal(["Two"], Titles(CreateStore()));

        store.Clear();
        Assert.Empty(CreateStore().GetAll());
    }

    [Fact]
    public void Add_AfterRemovingTheLastItemStillSortsLast()
    {
        var store = CreateStore();
        store.Add("One", "", SongSource.Local, null, null);
        var two = store.Add("Two", "", SongSource.Local, null, null);
        store.MoveUp(two.Id);
        store.Add("Three", "", SongSource.Local, null, null);

        Assert.Equal(["Two", "One", "Three"], Titles(CreateStore()));
    }
}
