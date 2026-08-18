namespace UltraSinger.Blazor.Configuration;

public class UltraStarPlayConfiguration
{
    public const string SectionName = "UltraStarPlay";

    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "127.0.0.1";
    public int HttpPort { get; set; } = 6789;
    public int UdpPort { get; set; } = 34567;
    public string ClientId { get; set; } = "UltraSingerUI";
    public string ClientName { get; set; } = "UltraSinger UI";
    public bool AutoQueueOnCompletion { get; set; } = true;
    public string? DefaultPlayerProfile { get; set; } = "Player 1";
    public int TimeoutSeconds { get; set; } = 5;
}
