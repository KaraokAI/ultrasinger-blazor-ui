using UltraSinger.Blazor.Services;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Components.Pages.Components;

public partial class SongLibraryView
{
    private string SearchQuery { get; set; } = string.Empty;
    private List<LocalSongResult> FilteredSongs { get; set; } = new();
    private bool IsRefreshing { get; set; } = false;
    private string? FeedbackMessage { get; set; }
    private bool IsFeedbackError { get; set; }
    private string? QueuingSongPath { get; set; }

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