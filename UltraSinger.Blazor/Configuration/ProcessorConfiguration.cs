namespace UltraSinger.Blazor.Configuration;

public record ProcessorConfiguration
{
    /// <summary>Base address of the remote processor, e.g. <c>http://karaoke-box:5209</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Shared secret sent as <c>X-Api-Key</c>. Must match the processor's <c>Processor:ApiKey</c>.</summary>
    public string? ApiKey { get; set; }
}
