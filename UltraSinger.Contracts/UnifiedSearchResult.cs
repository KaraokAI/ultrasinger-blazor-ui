namespace UltraSinger.Contracts;

public class UnifiedSearchResult
{
    public SongSource Source { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
    public string? Url { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Description { get; set; }
    public string? ExtraInfo { get; set; }
    public double? Rating { get; set; }
    public bool? HasGoldenNotes { get; set; }
    public int? UsdbSongId { get; set; }
    public LocalSongResult? LocalSong { get; set; }
    public UsdbSongResult? UsdbSong { get; set; }
    public string? YouTubeVideoId { get; set; }
}
