using Microsoft.AspNetCore.Components;
using UltraSinger.Blazor.Entities.Youtube;
using UltraSinger.Blazor.Exceptions;
using UltraSinger.Blazor.Services;
using Timer = System.Timers.Timer;

namespace UltraSinger.Blazor.Components.Pages.Components;

public partial class YouTubeSearch
{
    [Parameter]
    public Action? ClearResults { get; set; } = null;

    [Inject]
    private YouTubeAPIService YouTubeApiService { get; set; } = null!;

    [Inject]
    private ProcessorApiClient Processor { get; set; } = null!;

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

    private async Task PerformSearch()
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

    private async Task OnSelectVideo(YouTubeVideoResult video)
    {
        HasQueued = true;
        Results?.Clear();
        StateHasChanged();

        // The search result already gives us a title, so the processor can skip yt-dlp.
        await Processor.EnqueueAsync(
            $"https://www.youtube.com/watch?v={video.VideoId}",
            video.Title);
    }
}
