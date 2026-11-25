using System.Text;
using UltraSinger.Contracts;

namespace UltraSinger.Processor.Entities;

/// <summary>
/// The processor's mutable view of a song, including its own log buffer.
///
/// Each record owns its log rather than sharing one global buffer, so the output of one
/// job can no longer be overwritten by the next one starting.
/// </summary>
public class SongRecord
{
    private readonly StringBuilder _log = new();
    private readonly StringBuilder _errors = new();
    private readonly object _gate = new();

    private string? _title;
    private string? _jobId;
    private SongState _state = SongState.NOT_STARTED;
    private DateTime? _beganProcessingAt;
    private DateTime? _completedAt;
    private string? _ultraStarTxtPath;
    private string? _bundlePath;
    private DateTime? _fetchedAt;
    private SongSource _source = SongSource.YouTube;
    private int? _usdbSongId;
    private string? _ultraStarTxt;

    /// <summary>
    /// Set by every mutation and cleared once the record has been written to SQLite.
    /// Persistence is write-behind, so this is what the flusher looks for.
    /// </summary>
    public bool IsDirty { get; private set; }

    public required Guid Id { get; init; }

    public required string Url { get; init; }

    public SongSource Source
    {
        get => _source;
        set { _source = value; IsDirty = true; }
    }

    public int? UsdbSongId
    {
        get => _usdbSongId;
        set { _usdbSongId = value; IsDirty = true; }
    }

    public string? UltraStarTxt
    {
        get => _ultraStarTxt;
        set { _ultraStarTxt = value; IsDirty = true; }
    }

    public string? Title
    {
        get => _title;
        set { _title = value; IsDirty = true; }
    }

    /// <summary>Hangfire job id, assigned immediately after enqueue.</summary>
    public string? JobId
    {
        get => _jobId;
        set { _jobId = value; IsDirty = true; }
    }

    public SongState State
    {
        get => _state;
        set { _state = value; IsDirty = true; }
    }

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    public DateTime? BeganProcessingAt
    {
        get => _beganProcessingAt;
        set { _beganProcessingAt = value; IsDirty = true; }
    }

    public DateTime? CompletedAt
    {
        get => _completedAt;
        set { _completedAt = value; IsDirty = true; }
    }

    public string? UltraStarTxtPath
    {
        get => _ultraStarTxtPath;
        set { _ultraStarTxtPath = value; IsDirty = true; }
    }

    /// <summary>Path to the zipped bundle on the processor's own disk. Never exposed to the UI.</summary>
    public string? BundlePath
    {
        get => _bundlePath;
        set { _bundlePath = value; IsDirty = true; }
    }

    /// <summary>
    /// When the UI last successfully fetched and extracted this song's bundle. Null means a
    /// completed song still needs to be pulled down.
    /// </summary>
    public DateTime? FetchedAt
    {
        get => _fetchedAt;
        set { _fetchedAt = value; IsDirty = true; }
    }

    /// <summary>
    /// Appends a line to the user-visible log (fed from stdout). Called from the process
    /// reader threads, hence the lock.
    /// </summary>
    public void AppendLog(string line)
    {
        lock (_gate)
        {
            _log.AppendLine(line);
            IsDirty = true;
        }
    }

    /// <summary>
    /// Appends a line to the error buffer (fed from stderr). Kept separate from the visible
    /// log — it exists to build the failure message, as the old Stderr buffer did.
    /// </summary>
    public void AppendError(string line)
    {
        lock (_gate)
        {
            _errors.AppendLine(line);
            IsDirty = true;
        }
    }

    public string ErrorText
    {
        get
        {
            lock (_gate)
            {
                return _errors.ToString();
            }
        }
    }

    /// <summary>
    /// Returns log content written after <paramref name="sinceOffset"/> along with the new
    /// total length. A <paramref name="sinceOffset"/> beyond the current length (or below
    /// zero) yields the whole log, which is how a caller recovers if the buffer was reset.
    /// </summary>
    public LogChunkDto ReadLogFrom(int sinceOffset)
    {
        lock (_gate)
        {
            var length = _log.Length;

            if (sinceOffset < 0 || sinceOffset > length)
            {
                return new LogChunkDto { Offset = length, Text = _log.ToString() };
            }

            return new LogChunkDto
            {
                Offset = length,
                Text = sinceOffset == length ? string.Empty : _log.ToString(sinceOffset, length - sinceOffset)
            };
        }
    }

    public SongDto ToDto() => new()
    {
        Id = Id,
        Url = Url,
        Title = Title,
        State = State,
        CreatedAt = CreatedAt,
        BeganProcessingAt = BeganProcessingAt,
        CompletedAt = CompletedAt,
        UltraStarTxtPath = UltraStarTxtPath,
        FetchedAt = FetchedAt,
        Source = Source,
        UsdbSongId = UsdbSongId
    };

    /// <summary>
    /// Takes a consistent copy of everything that gets persisted and clears the dirty flag
    /// in the same lock, so a write that lands mid-append can't drop the appended text: any
    /// mutation racing this call re-sets the flag and is picked up by the next flush.
    /// </summary>
    public SongSnapshot TakeSnapshotAndClean()
    {
        lock (_gate)
        {
            IsDirty = false;

            return new SongSnapshot(
                Id, Url, Title, JobId, State, CreatedAt,
                BeganProcessingAt, CompletedAt, UltraStarTxtPath,
                BundlePath, FetchedAt, Source, UsdbSongId, UltraStarTxt,
                _log.ToString(), _errors.ToString());
        }
    }

    /// <summary>Rebuilds a record from its persisted row. Comes back clean.</summary>
    public static SongRecord FromSnapshot(SongSnapshot snapshot)
    {
        var record = new SongRecord
        {
            Id = snapshot.Id,
            Url = snapshot.Url,
            CreatedAt = snapshot.CreatedAt,
            Source = snapshot.Source,
            UsdbSongId = snapshot.UsdbSongId,
            UltraStarTxt = snapshot.UltraStarTxt
        };

        record._title = snapshot.Title;
        record._jobId = snapshot.JobId;
        record._state = snapshot.State;
        record._beganProcessingAt = snapshot.BeganProcessingAt;
        record._completedAt = snapshot.CompletedAt;
        record._ultraStarTxtPath = snapshot.UltraStarTxtPath;
        record._bundlePath = snapshot.BundlePath;
        record._fetchedAt = snapshot.FetchedAt;
        record._log.Append(snapshot.Log);
        record._errors.Append(snapshot.Errors);
        record.IsDirty = false;

        return record;
    }
}

public record SongSnapshot(
    Guid Id,
    string Url,
    string? Title,
    string? JobId,
    SongState State,
    DateTime CreatedAt,
    DateTime? BeganProcessingAt,
    DateTime? CompletedAt,
    string? UltraStarTxtPath,
    string? BundlePath,
    DateTime? FetchedAt,
    SongSource Source,
    int? UsdbSongId,
    string? UltraStarTxt,
    string Log,
    string Errors);
