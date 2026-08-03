namespace UltraSinger.Processor.Configuration;

public record ProcessorApiConfiguration
{
    /// <summary>
    /// Shared secret expected in the <c>X-Api-Key</c> header. When left unset the API is
    /// open, which keeps local development frictionless — do not expose the processor
    /// beyond a trusted network without setting this.
    /// </summary>
    public string? ApiKey { get; init; }
}
