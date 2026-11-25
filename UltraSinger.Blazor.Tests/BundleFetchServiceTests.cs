using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UltraSinger.Blazor.Configuration;
using UltraSinger.Blazor.Services;
using UltraSinger.Blazor.Tests.Infrastructure;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Tests;

public sealed class BundleFetchServiceTests : IAsyncDisposable
{
    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private readonly FakeProcessor _processor = new();
    private readonly LibraryFixture _library = new();
    private readonly BlazorSongQueueService _queue;
    private readonly BundleFetchService _service;

    public BundleFetchServiceTests()
    {
        var client = _processor.CreateClient();
        var configuration = new LibraryConfiguration { LocalPath = _library.Path, PollIntervalSeconds = 1 };

        _queue = new BlazorSongQueueService(client, NullLogger<BlazorSongQueueService>.Instance);
        _service = new BundleFetchService(
            client,
            new StaticOptionsMonitor<LibraryConfiguration>(configuration),
            new LocalLibraryService(Options.Create(configuration), NullLogger<LocalLibraryService>.Instance),
            _queue,
            NullLogger<BundleFetchService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _service.StopAsync(CancellationToken.None);
        _library.Dispose();
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private SongDto CompletedSong(string title, byte[]? bundle, DateTime? fetchedAt = null)
    {
        var song = new SongDto { Id = Guid.NewGuid(), Url = "u", Title = title, State = SongState.COMPLETED, FetchedAt = fetchedAt, Source = SongSource.USDB };
        _processor.Songs.Add(song);
        if (bundle != null) _processor.Bundles[song.Id] = bundle;
        return song;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for the bundle fetch");
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ExtractsAcksAndQueuesCompletedSongs()
    {
        var song = CompletedSong("ignored - title", Zip(
            ("Queen - Bohemian Rhapsody/Queen - Bohemian Rhapsody.txt", "#ARTIST:Queen\n#TITLE:Bohemian Rhapsody\n: 0 1 1 x\nE"),
            ("Queen - Bohemian Rhapsody/Queen - Bohemian Rhapsody.mp3", "audio")));

        await _service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil(() => _processor.AckedBundles.Contains(song.Id) && _processor.PlayQueue.Count == 1);

        var txt = Path.Combine(_library.Path, "Queen - Bohemian Rhapsody", "Queen - Bohemian Rhapsody.txt");
        Assert.True(File.Exists(txt));
        Assert.True(File.Exists(Path.ChangeExtension(txt, ".mp3")));

        var queued = _processor.PlayQueue.Single();
        Assert.Equal("Queen", queued.Artist);
        Assert.Equal("Bohemian Rhapsody", queued.Title);
        Assert.Equal(SongSource.USDB, queued.Source);
        Assert.Equal(txt, queued.FilePath);
    }

    [Fact]
    public async Task FallsBackToSplittingTheJobTitleWhenTheTxtHasNoArtist()
    {
        var song = CompletedSong("ABBA - Waterloo", Zip(("Waterloo/notes.md", "not an UltraStar file")));

        await _service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil(() => _processor.PlayQueue.Count == 1);

        Assert.Contains(song.Id, _processor.AckedBundles);
        Assert.Equal(("ABBA", "Waterloo"), (_processor.PlayQueue[0].Artist, _processor.PlayQueue[0].Title));
    }

    [Fact]
    public async Task SkipsFetchedSongs_AndLeavesUndownloadableOnesForTheNextPoll()
    {
        var alreadyFetched = CompletedSong("A - B", Zip(("A - B/A - B.txt", "#ARTIST:A\n#TITLE:B")), fetchedAt: DateTime.Now);
        var notReady = CompletedSong("C - D", bundle: null);
        var ready = CompletedSong("E - F", Zip(("E - F/E - F.txt", "#ARTIST:E\n#TITLE:F")));

        await _service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil(() => _processor.AckedBundles.Contains(ready.Id));

        Assert.DoesNotContain(alreadyFetched.Id, _processor.AckedBundles);
        Assert.DoesNotContain(notReady.Id, _processor.AckedBundles);
        Assert.False(Directory.Exists(Path.Combine(_library.Path, "A - B")));
        Assert.Contains(_processor.Requests, r => r.PathAndQuery == $"/api/songs/{notReady.Id}/bundle");
    }
}
