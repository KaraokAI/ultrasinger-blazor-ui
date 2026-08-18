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

        // 1. Local Library (Highest Priority)
        if (includeLocal)
        {
            try
            {
                var localMatches = _localLibraryService.Search(query);
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
                        UsdbSong = usdb
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
                        Description = yt.Title
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
}
