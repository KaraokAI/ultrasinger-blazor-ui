using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using UltraSingerUI.Entities;
using UltraSingerUI.Entities.Youtube;
using UltraSingerUI.Exceptions;
using UltraSingerUI.Services;
using Timer = System.Timers.Timer;

namespace UltraSingerUI.Components.Pages.Components;

public partial class YouTubeSearch
{
    [Parameter]
    public Action? ClearResults { get; set; } = null;
    
    [Inject]
    private YouTubeAPIService YouTubeApiService { get; set; } = null!;

    [Inject]
    private SongProcessorService SongProcessorService { get; set; } = null!;

    private string SearchQuery { get; set; } = string.Empty;
    private bool IsLoading { get; set; } = false;
    private bool HasQueued { get; set; } = false;
    private bool IsRateLimited { get; set; }
    private List<YouTubeVideoResult>? Results { get; set; }
    
    private Timer RateLimitTimer { get; set; } = new (TimeSpan.FromSeconds(60));

    protected override void OnInitialized()
    {
        RateLimitTimer.Elapsed += (_, _) => IsRateLimited = false;
    }

    private async void PerformSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        HasQueued = false;

        IsLoading = true;

        try
        {
            Results = await YouTubeApiService.SearchVideosAsync(SearchQuery);
        }
        catch (YoutubeRateLimitException)
        {
            IsRateLimited = true;
            RateLimitTimer.Start();
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
        }
    }

    private void OnSelectVideo(YouTubeVideoResult video)
    {
        HasQueued = true;
        Console.WriteLine($"Triggered queue add for {video.VideoId}");
        Results?.Clear();
        StateHasChanged();
        
        SongProcessorService.ProcessSong(new Song
        {
            Url = $"https://www.youtube.com/watch?v={video.VideoId}",
            Title = video.Title,
        });
    }
}