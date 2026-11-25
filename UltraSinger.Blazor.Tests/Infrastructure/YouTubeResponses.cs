using System.Net;
using System.Net.Http.Json;

namespace UltraSinger.Blazor.Tests.Infrastructure;

public static class YouTubeResponses
{
    /// <summary>A YouTube Data API v3 search response.</summary>
    public static HttpResponseMessage Search(params (string VideoId, string Title)[] videos) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            items = videos.Select(v => new
            {
                id = new { videoId = v.VideoId },
                snippet = new { title = v.Title, thumbnails = new { @default = new { url = $"https://i.ytimg.com/vi/{v.VideoId}/default.jpg" } } }
            })
        })
    };

    public static string KeyOf(HttpRequestMessage request) =>
        System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["key"]!;
}
