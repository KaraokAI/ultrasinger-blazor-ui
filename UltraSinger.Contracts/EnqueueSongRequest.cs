namespace UltraSinger.Contracts;

/// <summary>
/// Request to queue a song for processing. When <see cref="Title"/> is omitted the
/// processor resolves it via yt-dlp, so callers without a local yt-dlp can just send a URL.
/// </summary>
public record EnqueueSongRequest
{
    public required string Url { get; init; }

    public string? Title { get; init; }
}
