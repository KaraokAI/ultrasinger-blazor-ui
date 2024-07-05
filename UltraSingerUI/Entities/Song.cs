namespace UltraSingerUI.Entities;

public record Song
{
    public required string Url { get; set; }
    
    public string? Title { get; set; }
    
    public string? JobId { get; set; }

    public SongState JobState { get; set; } = SongState.UNKNOWN;

    public string FriendlyName => Title ?? Url;
    
    public DateTime CreatedAt { get; private set; } = DateTime.Now;
    
    public DateTime? CompletedAt { get; set; }
}