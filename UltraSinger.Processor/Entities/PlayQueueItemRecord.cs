using UltraSinger.Contracts;

namespace UltraSinger.Processor.Entities;

/// <summary>
/// A single row in the "sing next" play queue. Unlike <see cref="SongRecord"/> this has no
/// write-behind cache — mutations are driven by low-frequency human actions (add/remove/
/// move/mark-sung), so <see cref="Services.PlayQueueStore"/> persists every change straight
/// through to SQLite.
/// </summary>
public class PlayQueueItemRecord
{
    public required Guid Id { get; init; }
    public required string Title { get; set; }
    public required string Artist { get; set; }
    public required SongSource Source { get; set; }
    public string? ExtraInfo { get; set; }
    public string? FilePath { get; set; }
    public required DateTime QueuedAt { get; set; }
    public required int SortOrder { get; set; }
    public bool IsSung { get; set; }
    public DateTime? SungAt { get; set; }

    public SongQueueItem ToDto() => new()
    {
        Id = Id,
        Title = Title,
        Artist = Artist,
        Source = Source,
        ExtraInfo = ExtraInfo,
        FilePath = FilePath,
        QueuedAt = QueuedAt,
        IsSung = IsSung,
        SungAt = SungAt
    };
}
