namespace UltraSinger.Contracts;

/// <summary>
/// An incremental slice of a song's log. Callers pass the <see cref="Offset"/> they last
/// received back as <c>sinceOffset</c> so only new output crosses the wire.
/// </summary>
public record LogChunkDto
{
    /// <summary>Total length of the log after this chunk — send this back as the next <c>sinceOffset</c>.</summary>
    public int Offset { get; init; }

    /// <summary>Log text appended since the requested offset. Empty when there is nothing new.</summary>
    public string Text { get; init; } = string.Empty;
}
