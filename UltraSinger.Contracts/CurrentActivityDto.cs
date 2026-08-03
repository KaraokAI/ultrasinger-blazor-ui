namespace UltraSinger.Contracts;

/// <summary>
/// What the processor is doing right now, plus new log output, in a single round trip.
/// </summary>
public record CurrentActivityDto
{
    /// <summary>The song currently being processed, or null when the processor is idle.</summary>
    public SongDto? Current { get; init; }

    public LogChunkDto Log { get; init; } = new();
}
