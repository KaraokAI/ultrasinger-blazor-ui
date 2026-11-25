using Microsoft.AspNetCore.Components;
using UltraSinger.Blazor.Services;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Components.Pages.Components;

public partial class SongLibraryView
{
    [Inject]
    private BlazorSongQueueService SongQueueService { get; set; } = null!;

    private string SearchQuery { get; set; } = string.Empty;
    private List<LocalSongResult> FilteredSongs { get; set; } = new();
    private bool IsRefreshing { get; set; } = false;
    private string? FeedbackMessage { get; set; }
    private bool IsFeedbackError { get; set; }

    protected override void OnInitialized()
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredSongs = LocalLibraryService.Search(SearchQuery);
    }

    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        ApplyFilter();
    }

    private void QueueSong(LocalSongResult song)
    {
        SongQueueService.Enqueue(new SongQueueItem
        {
            Title = song.Title,
            Artist = song.Artist,
            Source = SongSource.Local,
            ExtraInfo = song.Year,
            FilePath = song.TxtFilePath,
            QueuedAt = DateTime.Now
        });
        FeedbackMessage = $"Queued: {(string.IsNullOrWhiteSpace(song.Artist) ? song.Title : $"{song.Artist} - {song.Title}")}";
        IsFeedbackError = false;
    }

    private void RefreshLibrary()
    {
        IsRefreshing = true;
        try
        {
            LocalLibraryService.Refresh();
            ApplyFilter();
            FeedbackMessage = $"Library refreshed: {FilteredSongs.Count} songs found.";
            IsFeedbackError = false;
        }
        catch (Exception ex)
        {
            FeedbackMessage = $"Failed to scan library: {ex.Message}";
            IsFeedbackError = true;
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}