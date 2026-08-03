using Microsoft.AspNetCore.Components;
using UltraSinger.Contracts;
using UltraSinger.Blazor.Services;

namespace UltraSinger.Blazor.Components.Pages.Components;

public partial class History : ComponentBase, IAsyncDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    [Inject]
    private ProcessorApiClient Processor { get; set; } = null!;

    private readonly CancellationTokenSource _cts = new();

    private Task? _pollTask;

    /// <summary>
    /// Newest first, as ordered by the processor. Job state comes down with the list — the
    /// UI no longer reaches into Hangfire to work it out for itself.
    /// </summary>
    private IReadOnlyList<SongDto> Songs { get; set; } = [];

    protected override void OnAfterRender(bool firstRender)
    {
        if (!firstRender) return;

        _pollTask = PollAsync();
    }

    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        try
        {
            do
            {
                Songs = await Processor.GetSongsAsync(_cts.Token);
                await InvokeAsync(StateHasChanged);
            }
            while (await timer.WaitForNextTickAsync(_cts.Token));
        }
        catch (OperationCanceledException)
        {
            // Component is going away.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        if (_pollTask is not null)
        {
            try
            {
                await _pollTask;
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
