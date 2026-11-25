namespace UltraSinger.Contracts;

public class UsdbSongResult
{
    public int Id { get; set; }
    public string Artist { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Edition { get; set; }
    public string? Genre { get; set; }
    public int? Year { get; set; }
    public string? Language { get; set; }
    public bool? GoldenNotes { get; set; }
    public double Rating { get; set; }
    public int Views { get; set; }
    public string? YoutubeId { get; set; }
    public string? CoverUrl { get; set; }
}
