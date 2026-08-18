using System.Text;
using Microsoft.Extensions.Options;
using UltraSinger.Blazor.Configuration;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Services;

public class LocalLibraryService
{
    private readonly LibraryConfiguration _configuration;
    private readonly ILogger<LocalLibraryService> _logger;
    private readonly List<LocalSongResult> _cachedSongs = new();
    private readonly object _lock = new();
    private DateTime? _lastScanned;

    public LocalLibraryService(IOptions<LibraryConfiguration> options, ILogger<LocalLibraryService> logger)
    {
        _configuration = options.Value;
        _logger = logger;
    }

    public string? LibraryPath => _configuration.LocalPath;

    public IReadOnlyList<LocalSongResult> GetAll()
    {
        lock (_lock)
        {
            if (_lastScanned == null || DateTime.UtcNow - _lastScanned > TimeSpan.FromMinutes(5))
            {
                ScanLibrary();
            }

            return _cachedSongs.ToList();
        }
    }

    public void Refresh()
    {
        lock (_lock)
        {
            ScanLibrary();
        }
    }

    public List<LocalSongResult> Search(string query)
    {
        var songs = GetAll();
        if (string.IsNullOrWhiteSpace(query))
        {
            return songs.ToList();
        }

        var terms = query.Split(new[] { ' ', '-', ':', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return songs
            .Where(song =>
            {
                var matchTitle = terms.All(t => song.Title.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                                song.Artist.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                                (song.Genre != null && song.Genre.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                                                (song.Edition != null && song.Edition.Contains(t, StringComparison.OrdinalIgnoreCase)));
                return matchTitle;
            })
            .OrderBy(s => s.Artist)
            .ThenBy(s => s.Title)
            .ToList();
    }

    private void ScanLibrary()
    {
        var path = _configuration.LocalPath;
        _cachedSongs.Clear();
        _lastScanned = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            _logger.LogWarning("Local UltraStar library path is not configured or does not exist: {Path}", path);
            return;
        }

        try
        {
            var txtFiles = Directory.EnumerateFiles(path, "*.txt", SearchOption.AllDirectories);
            foreach (var txtFile in txtFiles)
            {
                try
                {
                    var parsed = ParseUltraStarTxtFile(txtFile);
                    if (parsed != null)
                    {
                        _cachedSongs.Add(parsed);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Failed to parse UltraStar file {Path}", txtFile);
                }
            }

            _logger.LogInformation("Scanned local library at {Path}, found {Count} songs", path, _cachedSongs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning local library at {Path}", path);
        }
    }

    public static LocalSongResult? ParseUltraStarTxtFile(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        var dir = Path.GetDirectoryName(filePath);
        var lines = File.ReadLines(filePath, Encoding.UTF8);

        string artist = string.Empty;
        string title = string.Empty;
        string? mp3 = null;
        string? video = null;
        string? cover = null;
        string? language = null;
        string? year = null;
        string? genre = null;
        string? edition = null;
        bool isUltraStar = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith('#'))
            {
                if (line.Length > 0 && ":*FRG-E".Contains(line[0]))
                {
                    isUltraStar = true;
                }
                break;
            }

            isUltraStar = true;
            if (line.StartsWith("#ARTIST:", StringComparison.OrdinalIgnoreCase))
            {
                artist = line["#ARTIST:".Length..].Trim();
            }
            else if (line.StartsWith("#TITLE:", StringComparison.OrdinalIgnoreCase))
            {
                title = line["#TITLE:".Length..].Trim();
            }
            else if (line.StartsWith("#MP3:", StringComparison.OrdinalIgnoreCase))
            {
                mp3 = line["#MP3:".Length..].Trim();
            }
            else if (line.StartsWith("#VIDEO:", StringComparison.OrdinalIgnoreCase))
            {
                video = line["#VIDEO:".Length..].Trim();
            }
            else if (line.StartsWith("#COVER:", StringComparison.OrdinalIgnoreCase))
            {
                cover = line["#COVER:".Length..].Trim();
            }
            else if (line.StartsWith("#LANGUAGE:", StringComparison.OrdinalIgnoreCase))
            {
                language = line["#LANGUAGE:".Length..].Trim();
            }
            else if (line.StartsWith("#YEAR:", StringComparison.OrdinalIgnoreCase))
            {
                year = line["#YEAR:".Length..].Trim();
            }
            else if (line.StartsWith("#GENRE:", StringComparison.OrdinalIgnoreCase))
            {
                genre = line["#GENRE:".Length..].Trim();
            }
            else if (line.StartsWith("#EDITION:", StringComparison.OrdinalIgnoreCase))
            {
                edition = line["#EDITION:".Length..].Trim();
            }
        }

        if (!isUltraStar || (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title)))
        {
            return null;
        }

        bool hasMp3 = !string.IsNullOrWhiteSpace(mp3) && dir != null && File.Exists(Path.Combine(dir, mp3));
        bool hasVideo = !string.IsNullOrWhiteSpace(video) && dir != null && File.Exists(Path.Combine(dir, video));
        bool hasCover = !string.IsNullOrWhiteSpace(cover) && dir != null && File.Exists(Path.Combine(dir, cover));

        return new LocalSongResult
        {
            Artist = artist,
            Title = title,
            DirectoryPath = dir,
            TxtFilePath = filePath,
            Language = language,
            Year = year,
            Genre = genre,
            Edition = edition,
            HasMp3 = hasMp3,
            HasVideo = hasVideo,
            HasCover = hasCover
        };
    }
}
