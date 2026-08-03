using System.Net;

namespace UltraSinger.Contracts;

/// <summary>
/// A song as the processor sees it. The processor is the source of truth for every
/// field here — the UI only ever reads them.
/// </summary>
public record SongDto
{
    public Guid Id { get; init; }

    public required string Url { get; init; }

    public string? Title { get; init; }

    public SongState State { get; init; } = SongState.UNKNOWN;

    public DateTime CreatedAt { get; init; }

    public DateTime? BeganProcessingAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public string? UltraStarTxtPath { get; init; }

    /// <summary>
    /// When the UI last successfully fetched and extracted this song's bundle. Null means a
    /// completed song is still waiting to be pulled down.
    /// </summary>
    public DateTime? FetchedAt { get; init; }

    public string FriendlyName => WebUtility.HtmlDecode(Title ?? Url);

    /// <summary>
    /// Human readable wall clock time, e.g. "(04:32)". Empty until the job actually starts.
    /// Purely a display helper — unlike its predecessor it does not mutate the record,
    /// because <see cref="CompletedAt"/> now arrives already populated by the processor.
    /// </summary>
    public string WallClockTimeHuman()
    {
        if (State == SongState.NOT_STARTED || BeganProcessingAt is null)
        {
            return string.Empty;
        }

        var elapsed = (CompletedAt ?? DateTime.Now) - BeganProcessingAt.Value;

        return elapsed.TotalHours >= 1
            ? $@"({elapsed:hh\:mm\:ss})"
            : $@"({elapsed:mm\:ss})";
    }
}
