using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Services;

public interface IUltraStarPlayService
{
    Task<UltraStarPlayStatus> CheckConnectionAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UltraStarPlaySongDto>> GetLoadedSongsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAvailablePlayersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UltraStarPlayQueueEntryDto>> GetSongQueueAsync(CancellationToken cancellationToken = default);
    Task<bool> EnqueueSongAsync(string artist, string title, string? txtFilePath = null, string? playerProfile = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteQueueEntryAsync(int index, CancellationToken cancellationToken = default);
    Task<bool> DeleteAllQueueEntriesAsync(CancellationToken cancellationToken = default);
    string ComputeSongHash(string artist, string title, string? txtFilePath = null);
}
