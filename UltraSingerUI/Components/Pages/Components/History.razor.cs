using Microsoft.AspNetCore.Components;
using UltraSingerUI.Entities;
using UltraSingerUI.Services;
using Timer = System.Timers.Timer;

namespace UltraSingerUI.Components.Pages.Components;

public partial class History : ComponentBase, IDisposable
{
    [Inject]
    private SongProcessorService SongProcessorService { get; set; } = null!;
    
    private Timer RefreshInterval = new (TimeSpan.FromSeconds(1));
    
    private DateTime LastCheck { get; set; } = DateTime.Now;
    
    
    protected override void OnAfterRender(bool firstRender)
    {
        if (!firstRender) return;
        
        AssignStatuses();
        RefreshInterval.AutoReset = true;
        RefreshInterval.Enabled = true;
        RefreshInterval.Elapsed += (_, elapsedArgs) =>
        {
            LastCheck = elapsedArgs.SignalTime;
            InvokeAsync(AssignStatuses);
            InvokeAsync(StateHasChanged);
        };

    }

    private async void AssignStatuses()
    {
        foreach (var song in SongProcessorService
                     .GetProcessedSongList()
                     .Where(x => new [] { SongState.UNKNOWN, SongState.IN_PROGRESS, SongState.NOT_STARTED }.Contains(x.JobState)))
        {
            SongProcessorService.UpdateSongState(song);
        }
    }

    private string FormatDuration(Song song)
    {
        if (song.JobState == SongState.NOT_STARTED)
        {
            return string.Empty;
        }
        
        if (song.CompletedAt == null && new[] { SongState.COMPLETED, SongState.FAILED }.Contains(song.JobState))
        {
            song.CompletedAt = DateTime.Now;
        }
        
        var timespan = song.CreatedAt - (song.CompletedAt ?? DateTime.Now);
        return $"({timespan.Negate():mm\\:ss})";
    }
    
    public void Dispose()
    {
        RefreshInterval.Dispose();
    }
}