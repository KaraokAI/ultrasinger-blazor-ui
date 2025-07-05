using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using UltraSingerUI.Entities;
using UltraSingerUI.Entities.Youtube;
using UltraSingerUI.Services;

namespace UltraSingerUI.Components.Pages.Components;

public partial class YouTubeSearch
{
    [Inject]
    private YouTubeAPIService YouTubeApiService { get; set; } = null!;

    [Inject]
    private SongProcessorService SongProcessorService { get; set; } = null!;

    private string SearchQuery { get; set; } = string.Empty;
    private bool IsLoading { get; set; } = false;
    private List<YouTubeVideoResult>? Results { get; set; }

    private async void PerformSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        IsLoading = true;
        Results = await YouTubeApiService.SearchVideosAsync(SearchQuery);
        IsLoading = false;
        StateHasChanged();
    }

    private void OnSelectVideo(YouTubeVideoResult video)
    {
        Console.WriteLine($"Triggered queue add for {video.VideoId}");
    
        SongProcessorService.ProcessSong(new Song
        {
            Url = $"https://www.youtube.com/watch?v={video.VideoId}",
            Title = video.Title,
        });
    }
}