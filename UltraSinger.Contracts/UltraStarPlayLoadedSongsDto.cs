namespace UltraSinger.Contracts;

public class UltraStarPlayLoadedSongsDto
{
    public bool IsSongScanFinished { get; set; }
    public int SongCount { get; set; }
    public List<UltraStarPlaySongDto> SongList { get; set; } = new();
}
