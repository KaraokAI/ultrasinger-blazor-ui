using System.Collections.Concurrent;
using UltraSinger.Contracts;
using UltraSinger.Processor.Entities;

namespace UltraSinger.Processor.Services;

/// <summary>
/// SQLite-backed store that keeps every record in memory as a read cache.
///
/// Reads have to be cheap — the UI polls the song list every second and activity every
/// 500ms — so queries never touch the database. Writes are write-behind: mutations mark a
/// record dirty and <see cref="SongPersistenceService"/> flushes them on a timer.
/// </summary>
public class SqliteSongStore(SongDatabase database, ILogger<SqliteSongStore> logger) : ISongStore
{
    private readonly ConcurrentDictionary<Guid, SongRecord> _songs = new();

    // Deliberately excludes COMPLETED: whether a completed song is worth reprocessing (e.g.
    // the local copy went missing) is a call only a remote client can make, since the
    // processor has no visibility into what's actually on disk at the client end.
    private static readonly SongState[] BlocksRequeue =
    [
        SongState.IN_PROGRESS, SongState.NOT_STARTED
    ];

    /// <summary>
    /// Loads persisted records and repairs any that were mid-flight when the processor last
    /// stopped. Their operating system process is long gone, so leaving them IN_PROGRESS
    /// would show a job spinning forever in the UI.
    /// </summary>
    public void Load()
    {
        database.Initialise();

        var recovered = 0;

        foreach (var record in database.LoadAll())
        {
            if (record.State == SongState.IN_PROGRESS)
            {
                record.AppendLog("[UltraSinger] Processor restarted while this song was being processed; marking as failed.");
                record.State = SongState.FAILED;
                record.CompletedAt ??= DateTime.Now;
                recovered++;
            }

            _songs[record.Id] = record;
        }

        logger.LogInformation(
            "Loaded {Count} song(s) from disk; {Recovered} interrupted job(s) marked failed.",
            _songs.Count, recovered);
    }

    /// <summary>Records with unsaved changes. Used by the flusher.</summary>
    public IReadOnlyList<SongRecord> GetDirty() => _songs.Values.Where(x => x.IsDirty).ToList();

    public SongRecord Add(string url, string? title, SongSource source = SongSource.YouTube, int? usdbSongId = null, string? ultraStarTxt = null)
    {
        var record = new SongRecord
        {
            Id = Guid.NewGuid(),
            Url = url,
            Title = title,
            Source = source,
            UsdbSongId = usdbSongId,
            UltraStarTxt = ultraStarTxt
        };

        _songs[record.Id] = record;
        return record;
    }

    public SongRecord? Get(Guid id) => _songs.TryGetValue(id, out var song) ? song : null;

    public IReadOnlyList<SongRecord> GetAll() =>
        _songs.Values.OrderByDescending(x => x.CreatedAt).ToList();

    public SongRecord? GetCurrent() =>
        _songs.Values
            .Where(x => x.State == SongState.IN_PROGRESS)
            .OrderByDescending(x => x.BeganProcessingAt ?? x.CreatedAt)
            .FirstOrDefault();

    public bool HasActive(string url) =>
        _songs.Values.Any(x => x.Url == url && BlocksRequeue.Contains(x.State));

    public void Remove(Guid id)
    {
        if (_songs.TryRemove(id, out _))
        {
            database.Delete(id);
        }
    }
}