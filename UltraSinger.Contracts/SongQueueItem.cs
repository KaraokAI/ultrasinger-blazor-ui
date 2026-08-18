namespace UltraSinger.Contracts;

public class SongQueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
    public SongSource Source { get; set; }
    public DateTime QueuedAt { get; set; } = DateTime.Now;
    public string? ExtraInfo { get; set; }
    public string? FilePath { get; set; }
}
