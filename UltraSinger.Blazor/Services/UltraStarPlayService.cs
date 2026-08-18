using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UltraSinger.Blazor.Configuration;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Services;

public class UltraStarPlayService : IUltraStarPlayService
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<UltraStarPlayConfiguration> _options;
    private readonly ILogger<UltraStarPlayService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = null
    };

    public UltraStarPlayService(
        HttpClient httpClient,
        IOptionsMonitor<UltraStarPlayConfiguration> options,
        ILogger<UltraStarPlayService> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    private string BaseUrl
    {
        get
        {
            var config = _options.CurrentValue;
            var host = string.IsNullOrWhiteSpace(config.Host) ? "127.0.0.1" : config.Host;
            var port = config.HttpPort > 0 ? config.HttpPort : 6789;
            return $"http://{host}:{port}";
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var config = _options.CurrentValue;
        var relativePath = path.StartsWith('/') ? path[1..] : path;
        var request = new HttpRequestMessage(method, $"{BaseUrl}/{relativePath}");
        
        request.Headers.TryAddWithoutValidation("client-id", config.ClientId);
        request.Headers.TryAddWithoutValidation("client-name", config.ClientName);
        
        if (content != null)
        {
            request.Content = content;
        }

        return request;
    }

    public async Task<UltraStarPlayStatus> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        var config = _options.CurrentValue;
        var status = new UltraStarPlayStatus
        {
            ServerAddress = config.Host,
            HttpPort = config.HttpPort,
            LastCheckedAt = DateTime.UtcNow
        };

        if (!config.Enabled)
        {
            status.IsConnected = false;
            status.ErrorMessage = "UltraStar Play integration is disabled in settings.";
            return status;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, config.TimeoutSeconds)));

            using var request = CreateRequest(HttpMethod.Get, "api/rest/hello/UltraSingerUI");
            using var response = await _httpClient.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                status.IsConnected = true;

                // Also try to probe songs count and available players
                try
                {
                    var songs = await GetLoadedSongsAsync(cts.Token);
                    status.LoadedSongsCount = songs.Count;

                    var players = await GetAvailablePlayersAsync(cts.Token);
                    status.AvailablePlayers = players.ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "UltraStar Play connected, but secondary probes failed.");
                }
            }
            else
            {
                status.IsConnected = false;
                status.ErrorMessage = $"Server responded with HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            }
        }
        catch (Exception ex)
        {
            status.IsConnected = false;
            status.ErrorMessage = ex.Message;
        }

        return status;
    }

    public async Task<IReadOnlyList<UltraStarPlaySongDto>> GetLoadedSongsAsync(CancellationToken cancellationToken = default)
    {
        var config = _options.CurrentValue;
        if (!config.Enabled)
        {
            return Array.Empty<UltraStarPlaySongDto>();
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(2, config.TimeoutSeconds)));

            using var request = CreateRequest(HttpMethod.Get, "api/rest/songs");
            using var response = await _httpClient.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch songs from UltraStar Play: HTTP {StatusCode}", response.StatusCode);
                return Array.Empty<UltraStarPlaySongDto>();
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var loadedSongs = JsonSerializer.Deserialize<UltraStarPlayLoadedSongsDto>(json, JsonOptions);
            return loadedSongs?.SongList ?? (IReadOnlyList<UltraStarPlaySongDto>)Array.Empty<UltraStarPlaySongDto>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception while fetching songs from UltraStar Play companion API");
            return Array.Empty<UltraStarPlaySongDto>();
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailablePlayersAsync(CancellationToken cancellationToken = default)
    {
        var config = _options.CurrentValue;
        if (!config.Enabled)
        {
            return Array.Empty<string>();
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(2, config.TimeoutSeconds)));

            using var request = CreateRequest(HttpMethod.Get, "api/rest/availablePlayers");
            using var response = await _httpClient.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<string>();
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var playerList = JsonSerializer.Deserialize<UltraStarPlayPlayerListDto>(json, JsonOptions);
            return playerList?.Items ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not fetch available players from UltraStar Play companion API");
            return Array.Empty<string>();
        }
    }

    public async Task<IReadOnlyList<UltraStarPlayQueueEntryDto>> GetSongQueueAsync(CancellationToken cancellationToken = default)
    {
        var config = _options.CurrentValue;
        if (!config.Enabled)
        {
            return Array.Empty<UltraStarPlayQueueEntryDto>();
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(2, config.TimeoutSeconds)));

            using var request = CreateRequest(HttpMethod.Get, "api/rest/songQueue");
            using var response = await _httpClient.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<UltraStarPlayQueueEntryDto>();
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var songQueue = JsonSerializer.Deserialize<UltraStarPlaySongQueueDto>(json, JsonOptions);
            return songQueue?.SongQueueEntries ?? (IReadOnlyList<UltraStarPlayQueueEntryDto>)Array.Empty<UltraStarPlayQueueEntryDto>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception while fetching song queue from UltraStar Play companion API");
            return Array.Empty<UltraStarPlayQueueEntryDto>();
        }
    }

    public async Task<bool> EnqueueSongAsync(
        string artist,
        string title,
        string? txtFilePath = null,
        string? playerProfile = null,
        CancellationToken cancellationToken = default)
    {
        var config = _options.CurrentValue;
        if (!config.Enabled)
        {
            _logger.LogInformation("UltraStar Play is disabled in configuration. Skipping enqueue for {Artist} - {Title}.", artist, title);
            return false;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(3, config.TimeoutSeconds)));

            // 1. Resolve Song Hash
            string songHash = string.Empty;
            try
            {
                var loadedSongs = await GetLoadedSongsAsync(cts.Token);
                var match = loadedSongs.FirstOrDefault(s =>
                    string.Equals(s.Artist, artist, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase));

                if (match != null && !string.IsNullOrWhiteSpace(match.Hash))
                {
                    songHash = match.Hash;
                }
            }
            catch
            {
                // Fallback to computing hash
            }

            if (string.IsNullOrWhiteSpace(songHash))
            {
                songHash = ComputeSongHash(artist, title, txtFilePath);
            }

            // 2. Resolve Player Profile Name
            var playerName = playerProfile;
            if (string.IsNullOrWhiteSpace(playerName))
            {
                try
                {
                    var players = await GetAvailablePlayersAsync(cts.Token);
                    playerName = players.FirstOrDefault() ?? config.DefaultPlayerProfile ?? "Player 1";
                }
                catch
                {
                    playerName = config.DefaultPlayerProfile ?? "Player 1";
                }
            }

            // 3. Build Queue Entry DTO
            var entry = new UltraStarPlayQueueEntryDto
            {
                SongDto = new UltraStarPlaySongDto
                {
                    Artist = artist,
                    Title = title,
                    Hash = songHash
                },
                SingScenePlayerDataDto = new SingScenePlayerDataDto
                {
                    PlayerProfileNames = new List<string> { playerName },
                    PlayerProfileToMicProfileMap = new Dictionary<string, string>(),
                    PlayerProfileToVoiceIdMap = new Dictionary<string, string>()
                },
                GameRoundSettingsDto = null,
                IsMedleyWithPreviousEntry = false
            };

            var jsonBody = JsonSerializer.Serialize(entry, JsonOptions);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            using var request = CreateRequest(HttpMethod.Post, "api/rest/songQueue/entry", content);

            _logger.LogInformation("Enqueueing song '{Artist} - {Title}' (Hash: {Hash}) to UltraStar Play...", artist, title, songHash);

            using var response = await _httpClient.SendAsync(request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully enqueued '{Artist} - {Title}' in UltraStar Play.", artist, title);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cts.Token);
            _logger.LogWarning("Failed to enqueue song in UltraStar Play: HTTP {StatusCode} - {Error}", response.StatusCode, errorBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while attempting to enqueue song '{Artist} - {Title}' in UltraStar Play.", artist, title);
            return false;
        }
    }

    public async Task<bool> DeleteQueueEntryAsync(int index, CancellationToken cancellationToken = default)
    {
        var config = _options.CurrentValue;
        if (!config.Enabled)
        {
            return false;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(2, config.TimeoutSeconds)));

            using var request = CreateRequest(HttpMethod.Delete, $"api/rest/songQueue/entry/{index}");
            using var response = await _httpClient.SendAsync(request, cts.Token);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete song queue entry {Index} in UltraStar Play", index);
            return false;
        }
    }

    public async Task<bool> DeleteAllQueueEntriesAsync(CancellationToken cancellationToken = default)
    {
        var config = _options.CurrentValue;
        if (!config.Enabled)
        {
            return false;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(2, config.TimeoutSeconds)));

            using var request = CreateRequest(HttpMethod.Delete, "api/rest/songQueue");
            using var response = await _httpClient.SendAsync(request, cts.Token);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear song queue in UltraStar Play");
            return false;
        }
    }

    public string ComputeSongHash(string artist, string title, string? txtFilePath = null)
    {
        var artistAndTitleHash = Md5Hash($"{artist}:{title}");

        if (!string.IsNullOrWhiteSpace(txtFilePath))
        {
            try
            {
                var fullPath = Path.GetFullPath(txtFilePath);
                var fileHash = Md5Hash(fullPath);
                return $"{artistAndTitleHash}:{fileHash}";
            }
            catch
            {
                // Fall back to just artist and title hash
            }
        }

        return artistAndTitleHash;
    }

    private static string Md5Hash(string input)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}
