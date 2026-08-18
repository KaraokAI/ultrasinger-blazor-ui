namespace UltraSinger.Contracts;

public class UsdbSongDetails
{
    public int Id { get; set; }
    public string Artist { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? YoutubeUrl { get; set; }
    public string? UltraStarTxt { get; set; }
    public string? CoverUrl { get; set; }
    public double? Gap { get; set; }
    public double? Bpm { get; set; }
}
