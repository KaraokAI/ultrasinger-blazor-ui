using Microsoft.AspNetCore.Components;
using UltraSinger.Blazor.Exceptions;
using UltraSinger.Blazor.Services;
using UltraSinger.Contracts;
using Timer = System.Timers.Timer;

namespace UltraSinger.Blazor.Components.Pages.Components;

public partial class YouTubeSearch
{
    [Parameter]
    public Action? ClearResults { get; set; } = null;

    [Inject]
    private UnifiedSearchService UnifiedSearchService { get; set; } = null!;

    [Inject]
    private UsdbService UsdbService { get; set; } = null!;

    [Inject]
    private BlazorSongQueueService SongQueueService { get; set; } = null!;

    [Inject]
    private ProcessorApiClient Processor { get; set; } = null!;
    
    private string SearchQuery { get; set; } = string.Empty;
    private bool IsLoading { get; set; } = false;
    private bool HasQueued { get; set; } = false;
    private bool IsRateLimited { get; set; }
    private string? FeedbackMessage { get; set; }
    private int? ProcessingSongId { get; set; }
    private string? QueuingSongTitle { get; set; }
    private List<UnifiedSearchResult>? Results { get; set; }

    private Timer RateLimitTimer { get; set; } = new(TimeSpan.FromSeconds(60));

    protected override void OnInitialized()
    {
        RateLimitTimer.Elapsed += (_, _) =>
        {
            IsRateLimited = false;
            InvokeAsync(StateHasChanged);
        };
    }

    private async Task PerformSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        HasQueued = false;
        FeedbackMessage = null;
        IsLoading = true;

        try
        {
            Results = await UnifiedSearchService.SearchAsync(SearchQuery);
        }
        catch (YoutubeRateLimitException)
        {
            IsRateLimited = true;
            RateLimitTimer.Start();
        }
        catch (Exception ex)
        {
            FeedbackMessage = $"Search error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
        }
    }

    private async Task OnQueueInGame(UnifiedSearchResult result)
    {
        QueuingSongTitle = result.DisplayTitle;
        FeedbackMessage = null;
        StateHasChanged();

        try
        {
            SongQueueService.Enqueue(new SongQueueItem
            {
                Title = result.Title,
                Artist = result.Artist,
                Source = SongSource.Local,
                ExtraInfo = result.LocalSong?.Year ?? result.ExtraInfo,
                FilePath = result.LocalSong?.TxtFilePath,
                QueuedAt = DateTime.Now
            });

            FeedbackMessage = $"Queued local song: {result.DisplayTitle}";
        }
        finally
        {
            QueuingSongTitle = null;
            StateHasChanged();
        }
    }

    private async Task OnSelectResult(UnifiedSearchResult result)
    {
        if (result.Source == SongSource.Local)
        {
            return; // Already in local library
        }

        if (result.AlreadyLocal)
        {
            FeedbackMessage = $"You already have this locally: {result.DisplayTitle}";
            StateHasChanged();
            return;
        }

        FeedbackMessage = null;

        if (result.Source == SongSource.USDB && result.UsdbSongId.HasValue)
        {
            ProcessingSongId = result.UsdbSongId.Value;
            StateHasChanged();

            try
            {
                var request = await UsdbService.TryBuildEnqueueRequestAsync(result.UsdbSongId.Value);
                if (request == null)
                {
                    FeedbackMessage = $"Could not fetch USDB details for: {result.DisplayTitle}";
                }
                else
                {
                    var enqueueResult = await Processor.EnqueueAsync(request);

                    if (enqueueResult == EnqueueResult.Queued)
                    {
                        HasQueued = true;
                        Results?.Clear();
                        FeedbackMessage = $"Queued USDB song: {result.DisplayTitle}";
                    }
                    else if (enqueueResult == EnqueueResult.Duplicate)
                    {
                        FeedbackMessage = $"Song is already in queue or processing: {result.DisplayTitle}";
                    }
                    else
                    {
                        FeedbackMessage = $"Failed to queue song: {result.DisplayTitle}";
                    }
                }
            }
            catch (Exception ex)
            {
                FeedbackMessage = $"Error queuing USDB song: {ex.Message}";
            }
            finally
            {
                ProcessingSongId = null;
                StateHasChanged();
            }
        }
        else if (result.Source == SongSource.YouTube)
        {
            var url = result.Url ?? (!string.IsNullOrWhiteSpace(result.YouTubeVideoId)
                ? $"https://www.youtube.com/watch?v={result.YouTubeVideoId}"
                : null);

            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            var enqueueResult = await Processor.EnqueueAsync(
                url,
                result.Title,
                SongSource.YouTube);

            if (enqueueResult == EnqueueResult.Queued)
            {
                HasQueued = true;
                Results?.Clear();
                FeedbackMessage = $"Queued YouTube song: {result.Title}";
            }
            else if (enqueueResult == EnqueueResult.Duplicate)
            {
                FeedbackMessage = $"Song is already in queue or processing: {result.Title}";
            }
            else
            {
                FeedbackMessage = $"Failed to queue song: {result.Title}";
            }

            StateHasChanged();
        }
    }
}
