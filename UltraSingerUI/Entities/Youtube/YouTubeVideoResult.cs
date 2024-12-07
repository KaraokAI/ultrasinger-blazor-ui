namespace UltraSingerUI.Entities.Youtube;

public record YouTubeVideoResult
{
    public required string VideoId { get; set; }
    public required string Title { get; set; }
    public required string ThumbnailUrl { get; set; }
}
