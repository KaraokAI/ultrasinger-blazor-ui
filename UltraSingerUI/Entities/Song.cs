using System.Web;

namespace UltraSingerUI.Entities;

public record Song
{
    public required string Url { get; set; }
    
    public string? Title { get; set; }
    
    public string? JobId { get; set; }

    public SongState JobState { get; set; } = SongState.UNKNOWN;

    public string FriendlyName => HttpUtility.HtmlDecode(Title ?? Url);
    
    public DateTime CreatedAt { get; private set; } = DateTime.Now;
    
    public DateTime? BeganProcessingAt { get; set; }
    
    public DateTime? CompletedAt { get; set; }
    
    public string? UltraStarTxtPath { get; set; }
    
    public string WallClockTimeHuman()
    {
        if (this.JobState == SongState.NOT_STARTED || this.BeganProcessingAt == null)
        {
            return string.Empty;
        }
        
        if (this.CompletedAt == null && new[] { SongState.COMPLETED, SongState.FAILED }.Contains(this.JobState))
        {
            this.CompletedAt = DateTime.Now;
        }
        
        var timespan = (this.CompletedAt ?? DateTime.Now) - this.BeganProcessingAt;
        return $"({timespan?.Negate():mm\\:ss})";
    }
}