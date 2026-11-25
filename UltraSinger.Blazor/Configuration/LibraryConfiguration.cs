namespace UltraSinger.Blazor.Configuration;

public record LibraryConfiguration
{
    /// <summary>
    /// Where finished song bundles get extracted on this machine. Auto-fetch is a no-op
    /// while this is unset, so a pure viewer instance needs no extra configuration.
    /// </summary>
    public string? LocalPath { get; set; }

    /// <summary>How often to check for completed-but-unfetched songs.</summary>
    public int PollIntervalSeconds { get; set; } = 15;

    /// <summary>Path to yt-dlp executable. Defaults to "yt-dlp".</summary>
    public string YTDLPPath { get; set; } = "yt-dlp";
}
