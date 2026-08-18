namespace UltraSinger.Contracts;

public class SingScenePlayerDataDto
{
    public List<string> PlayerProfileNames { get; set; } = new();
    public Dictionary<string, string> PlayerProfileToMicProfileMap { get; set; } = new();
    public Dictionary<string, string> PlayerProfileToVoiceIdMap { get; set; } = new();
}

public class UltraStarPlayQueueEntryDto
{
    public UltraStarPlaySongDto SongDto { get; set; } = new();
    public SingScenePlayerDataDto SingScenePlayerDataDto { get; set; } = new();
    public object? GameRoundSettingsDto { get; set; }
    public bool IsMedleyWithPreviousEntry { get; set; }
}

public class UltraStarPlaySongQueueDto
{
    public List<UltraStarPlayQueueEntryDto> SongQueueEntries { get; set; } = new();
}
