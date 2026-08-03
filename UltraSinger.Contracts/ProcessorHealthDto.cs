namespace UltraSinger.Contracts;

/// <summary>
/// Configuration sanity report, so a misconfigured processor is visible from the UI
/// rather than only failing once a job runs.
/// </summary>
public record ProcessorHealthDto
{
    public bool Healthy { get; init; }

    /// <summary>Human readable problems, e.g. a required path that is not set or does not exist.</summary>
    public List<string> Problems { get; init; } = new();

    public bool OpenAICorrectionsEnabled { get; init; }

    public int QueuedCount { get; init; }

    public int InProgressCount { get; init; }
}
