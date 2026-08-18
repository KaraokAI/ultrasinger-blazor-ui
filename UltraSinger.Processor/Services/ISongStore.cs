using UltraSinger.Contracts;
using UltraSinger.Processor.Entities;

namespace UltraSinger.Processor.Services;

/// <summary>
/// Owns all song/job state for the processor.
/// </summary>
public interface ISongStore
{
    /// <summary>Creates a record and returns it. The id is assigned here, before the job is enqueued.</summary>
    SongRecord Add(string url, string? title, SongSource source = SongSource.YouTube, int? usdbSongId = null, string? ultraStarTxt = null);

    SongRecord? Get(Guid id);

    /// <summary>All songs, newest first — matching the stack ordering the UI previously relied on.</summary>
    IReadOnlyList<SongRecord> GetAll();

    /// <summary>The song currently being processed, if any.</summary>
    SongRecord? GetCurrent();

    /// <summary>
    /// True when the url already has a record that is queued or currently processing.
    /// Completed songs are deliberately not blocking: whether to skip a re-download because a
    /// local copy still exists is left to remote clients, who can actually see their own disk.
    /// </summary>
    bool HasActive(string url);

    void Remove(Guid id);
}