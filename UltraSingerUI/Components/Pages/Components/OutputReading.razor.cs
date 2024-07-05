using Microsoft.AspNetCore.Components;
using UltraSingerUI.Services;
using Timer = System.Timers.Timer;

namespace UltraSingerUI.Components.Pages.Components;

public partial class OutputReading : ComponentBase, IDisposable
{
    [CascadingParameter]
    private SongProcessorService SongProcessorService { get; set; } = null!;
    
    private Timer OutputRefreshInterval = new (TimeSpan.FromMilliseconds(100));

    private string OutputLog { get; set; } = string.Empty;

    protected override void OnAfterRender(bool firstRender)
    {
        if (!firstRender) return;
        
        OutputRefreshInterval.AutoReset = true;
        OutputRefreshInterval.Enabled = true;
        OutputRefreshInterval.Elapsed += (_, _) =>
        {
            OutputLog = SongProcessorService.GetLatestLog();
            InvokeAsync(StateHasChanged);
        };
    }

    public void Dispose()
    {
        OutputRefreshInterval.Dispose();
    }
}