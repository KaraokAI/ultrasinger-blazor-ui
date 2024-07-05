namespace UltraSingerUI.Entities;

public record Song
{
    public required string Url { get; set; }
    
    public string? Title { get; set; }
    
    public bool Processed { get; set; }
    
    public string? JobId { get; set; }

    public string FriendlyName => Title ?? Url;
    
    public DateTime CreatedAt => DateTime.Now;
}