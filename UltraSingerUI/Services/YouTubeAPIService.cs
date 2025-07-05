using Microsoft.Extensions.Options;
using UltraSingerUI.Configuration;
using UltraSingerUI.Entities.Youtube;

namespace UltraSingerUI.Services;

public class YouTubeAPIService(IOptions<YouTubeAPIConfiguration> ytCreds)
{
    private readonly HttpClient _httpClient = new();
    private const string ApiUrl = "https://www.googleapis.com/youtube/v3/search";

    public async Task<List<YouTubeVideoResult>> SearchVideosAsync(string query)
    {
        var url = $"{ApiUrl}?part=snippet&type=video&q={Uri.EscapeDataString(query)}&maxResults=10&key={ytCreds.Value.ApiKey}";

        var response = await _httpClient.GetFromJsonAsync<YouTubeSearchResponse>(url);
        if (response?.Items == null) return new List<YouTubeVideoResult>();

        return response.Items
            .Where(item => item.Id?.VideoId != null)
            .Select(item => new YouTubeVideoResult
            {
                VideoId = item.Id.VideoId,
                Title = item.Snippet.Title,
                ThumbnailUrl = item.Snippet.Thumbnails.Default.Url
            })
            .ToList();
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
        public required string VideoId { get; set; }
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