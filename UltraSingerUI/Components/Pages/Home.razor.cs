using Hangfire;
using Microsoft.AspNetCore.Components;
using UltraSingerUI.Constants;
using UltraSingerUI.Entities;
using UltraSingerUI.Services;
using Timer = System.Timers.Timer;

namespace UltraSingerUI.Components.Pages;

public partial class Home : ComponentBase, IDisposable
{
    [Inject]
    private SongProcessorService SongProcessorService { get; set; } = null!;
    
    [Inject]
    private YTDLPService YTDLPService { get; set; } = null!;

    private Timer RefreshInterval = new (TimeSpan.FromSeconds(5));
    
    private DateTime LastCheck { get; set; } = DateTime.Now;
    
    private string? SongUrl { get; set; }

    protected override void OnAfterRender(bool firstRender)
    {
        if (!firstRender) return;
        
        RefreshInterval.AutoReset = true;
        RefreshInterval.Enabled = true;
        RefreshInterval.Elapsed += (_, elapsedArgs) =>
        {
            LastCheck = elapsedArgs.SignalTime;
            InvokeAsync(StateHasChanged);
        };

    }

    private async void AddToQueue()
    {
        Console.WriteLine($"Triggered queue add for {SongUrl}");
        if (SongUrl == null)
        {
            return;
        }
        
        SongProcessorService.ProcessSong(new Song
        {
            Url = SongUrl,
            Title = await YTDLPService.GetTitleOfVideo(SongUrl),
        });

        StateHasChanged();
    }

    public void Dispose()
    {
        RefreshInterval.Dispose();
    }
}