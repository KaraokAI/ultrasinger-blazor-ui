using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Services;

public class UnifiedSearchService
{
    private readonly LocalLibraryService _localLibraryService;
    private readonly UsdbService _usdbService;
    private readonly YouTubeAPIService _youtubeApiService;
    private readonly ILogger<UnifiedSearchService> _logger;

    public UnifiedSearchService(
        LocalLibraryService localLibraryService,
        UsdbService usdbService,
        YouTubeAPIService youtubeApiService,
        ILogger<UnifiedSearchService> logger)
    {
        _localLibraryService = localLibraryService;
        _usdbService = usdbService;
        _youtubeApiService = youtubeApiService;
        _logger = logger;
    }

    public async Task<List<UnifiedSearchResult>> SearchAsync(string query, bool includeLocal = true, bool includeUsdb = true, bool includeYoutube = true)
    {
        var unifiedResults = new List<UnifiedSearchResult>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return unifiedResults;
        }

        // Used to flag USDB/YouTube rows as "you already have this" even though they're
        // separate rows from any matching Local row - matched independently of `includeLocal`
        // so the flag still works when local results are hidden from the list itself.
        var allLocalSongs = _localLibraryService.Search(query);
        var localKeys = allLocalSongs
            .Select(s => NormaliseKey(s.Artist, s.Title))
            .ToHashSet();

        // 1. Local Library (Highest Priority)
        if (includeLocal)
        {
            try
            {
                var localMatches = allLocalSongs;
                foreach (var local in localMatches)
                {
                    unifiedResults.Add(new UnifiedSearchResult
                    {
                        Source = SongSource.Local,
                        Artist = local.Artist,
                        Title = local.Title,
                        ExtraInfo = string.Join(" • ", new[] { local.Language, local.Genre, local.Year, local.Edition }.Where(x => !string.IsNullOrWhiteSpace(x))),
                        LocalSong = local
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching local library for query '{Query}'", query);
            }
        }

        // 2. USDB Online Library (Second Priority)
        if (includeUsdb)
        {
            try
            {
                var usdbMatches = await _usdbService.SearchAsync(query);
                foreach (var usdb in usdbMatches)
                {
                    unifiedResults.Add(new UnifiedSearchResult
                    {
                        Source = SongSource.USDB,
                        Artist = usdb.Artist,
                        Title = usdb.Title,
                        Rating = usdb.Rating,
                        HasGoldenNotes = usdb.GoldenNotes,
                        UsdbSongId = usdb.Id,
                        ExtraInfo = string.Join(" • ", new[] { usdb.Language, usdb.Genre, usdb.Year?.ToString(), usdb.Edition }.Where(x => !string.IsNullOrWhiteSpace(x))),
                        UsdbSong = usdb,
                        AlreadyLocal = localKeys.Contains(NormaliseKey(usdb.Artist, usdb.Title))
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching USDB for query '{Query}'", query);
            }
        }

        // 3. YouTube (Third Priority)
        if (includeYoutube)
        {
            try
            {
                var ytMatches = await _youtubeApiService.SearchVideosAsync(query);
                foreach (var yt in ytMatches)
                {
                    unifiedResults.Add(new UnifiedSearchResult
                    {
                        Source = SongSource.YouTube,
                        Title = yt.Title,
                        Artist = string.Empty,
                        Url = $"https://www.youtube.com/watch?v={yt.VideoId}",
                        YouTubeVideoId = yt.VideoId,
                        ThumbnailUrl = yt.ThumbnailUrl,
                        Description = yt.Title,
                        // YouTube titles are freeform (not "Artist - Title"), so an exact key
                        // match is too strict - fall back to "does the local song's
                        // artist+title both appear in this video's title" instead.
                        AlreadyLocal = allLocalSongs.Any(local =>
                            ContainsWords(yt.Title, local.Artist) && ContainsWords(yt.Title, local.Title))
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching YouTube for query '{Query}'", query);
            }
        }

        return unifiedResults;
    }

    private static string NormaliseKey(string artist, string title) =>
        Normalise(artist) + "|" + Normalise(title);

    private static string Normalise(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static bool ContainsWords(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
        {
            return false;
        }

        return Normalise(haystack).Contains(Normalise(needle), StringComparison.Ordinal);
    }
}
