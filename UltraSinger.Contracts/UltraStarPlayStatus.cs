namespace UltraSinger.Contracts;

public class UltraStarPlayStatus
{
    public bool IsConnected { get; set; }
    public string ServerAddress { get; set; } = string.Empty;
    public int HttpPort { get; set; } = 6789;
    public int LoadedSongsCount { get; set; }
    public List<string> AvailablePlayers { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public DateTime? LastCheckedAt { get; set; }
}
