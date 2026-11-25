namespace UltraSinger.Blazor.Configuration;

public record YouTubeApiConfiguration
{
    public string? ApiKey { get; set; }
    
    public string? BackupKey { get; set; }
}