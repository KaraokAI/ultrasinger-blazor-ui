namespace UltraSinger.Blazor.Services;

/// <summary>
/// Shared view of whether the remote processor is answering.
///
/// Registered as a singleton so that whichever component notices the outage first, every
/// other component's next poll tick sees it too — the typed <see cref="ProcessorApiClient"/>
/// itself is transient and cannot hold this.
/// </summary>
public class ProcessorConnectionState
{
    public bool IsReachable { get; private set; } = true;

    public string? LastError { get; private set; }

    public void MarkReachable()
    {
        IsReachable = true;
        LastError = null;
    }

    public void MarkUnreachable(string error)
    {
        IsReachable = false;
        LastError = error;
    }
}
