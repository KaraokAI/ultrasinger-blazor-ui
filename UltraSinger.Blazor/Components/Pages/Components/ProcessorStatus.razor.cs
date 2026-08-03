using Microsoft.AspNetCore.Components;
using UltraSinger.Contracts;
using UltraSinger.Blazor.Services;

namespace UltraSinger.Blazor.Components.Pages.Components;

/// <summary>
/// Surfaces backend trouble that the UI can no longer discover implicitly: with processing
/// on another machine, an unreachable or misconfigured processor would otherwise just look
/// like nothing ever happening.
/// </summary>
public partial class ProcessorStatus : ComponentBase, IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    [Inject]
    private ProcessorApiClient Processor { get; set; } = null!;

    [Inject]
    private ProcessorConnectionState ConnectionState { get; set; } = null!;

    private readonly CancellationTokenSource _cts = new();

    private Task? _pollTask;

    private ProcessorHealthDto? Health { get; set; }

    private string? Error => ConnectionState.LastError;

    protected override void OnAfterRender(bool firstRender)
    {
        if (!firstRender) return;

        _pollTask = PollAsync();
    }

    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            do
            {
                Health = await Processor.GetHealthAsync(_cts.Token);
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
