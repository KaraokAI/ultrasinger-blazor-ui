namespace UltraSinger.Contracts;

public class LocalSongResult
{
    public string Artist { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? DirectoryPath { get; set; }
    public string? TxtFilePath { get; set; }
    public string? Language { get; set; }
    public string? Year { get; set; }
    public string? Genre { get; set; }
    public string? Edition { get; set; }
    public bool HasMp3 { get; set; }
    public bool HasVideo { get; set; }
    public bool HasCover { get; set; }
}
