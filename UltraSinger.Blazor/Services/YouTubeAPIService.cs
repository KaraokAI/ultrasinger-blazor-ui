using Microsoft.Extensions.Options;
using UltraSinger.Blazor.Configuration;
using UltraSinger.Blazor.Entities.Youtube;
using UltraSinger.Blazor.Exceptions;

namespace UltraSinger.Blazor.Services;

public class YouTubeAPIService(IOptions<YouTubeAPIConfiguration> ytCreds)
{
    private readonly HttpClient _httpClient = new();
    private const string ApiUrl = "https://www.googleapis.com/youtube/v3/search";

    public async Task<List<YouTubeVideoResult>> SearchVideosAsync(string query, bool tryBackup = false)
    {
        var url = $"{ApiUrl}?part=snippet&type=video&q={Uri.EscapeDataString(query)}&maxResults=10&key={(tryBackup ? ytCreds.Value.BackupKey : ytCreds.Value.ApiKey)}";
        
        var rawResponse = await _httpClient.GetAsync(url);

        if (rawResponse.IsSuccessStatusCode)
        {
            var response = await rawResponse.Content.ReadFromJsonAsync<YouTubeSearchResponse>();
        
            if (response?.Items == null) return new List<YouTubeVideoResult>();

            return response.Items
                .Where(item => item.Id?.VideoId != null)
                .Select(item => new YouTubeVideoResult
                {
                    VideoId = item.Id.VideoId!,
                    Title = item.Snippet.Title,
                    ThumbnailUrl = item.Snippet.Thumbnails.Default.Url
                })
                .ToList();
        }

        var code = (int)rawResponse.StatusCode;
        if (code > 400 && code < 500)
        {
            if (tryBackup)
            {
                // We tried both the main and backup API key, but both failed.
                throw new YoutubeRateLimitException();
            }
            
            return await SearchVideosAsync(query, true);
        }

        return [];
    }

    // Classes for the deserialization
    private class YouTubeSearchResponse
    {
        public List<YouTubeItem> Items { get; set; } = new();
    }

    private class YouTubeItem
    {
        public required YouTubeId Id { get; set; }
        public required YouTubeSnippet Snippet { get; set; }
    }

    private class YouTubeId
    {
        public string? VideoId { get; set; }
    }

    private class YouTubeSnippet
    {
        public required string Title { get; set; }
        public required YouTubeThumbnails Thumbnails { get; set; }
    }

    private class YouTubeThumbnails
    {
        public required YouTubeThumbnail Default { get; set; }
    }

    private class YouTubeThumbnail
    {
        public required string Url { get; set; }
    }
}