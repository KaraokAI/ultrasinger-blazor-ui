namespace UltraSinger.Blazor.Configuration;

public record YouTubeAPIConfiguration
{
    public string? ApiKey { get; set; }
    
    public string? BackupKey { get; set; }
}