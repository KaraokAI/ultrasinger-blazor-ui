using Microsoft.AspNetCore.Components;
using UltraSinger.Contracts;
using UltraSinger.Blazor.Services;

namespace UltraSinger.Blazor.Components.Pages.Components;

public partial class OutputReading : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// 500ms rather than the old 100ms: this is a network round trip now. Only bytes
    /// written since <see cref="_offset"/> come back, so it still reads as live.
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

    [Inject]
    private ProcessorApiClient Processor { get; set; } = null!;

    private readonly CancellationTokenSource _cts = new();

    private readonly List<string> _lines = [];

    private Task? _pollTask;

    /// <summary>Total log length already consumed; sent back so we only fetch new output.</summary>
    private int _offset;

    private Guid? _currentSongId;

    private string OutputLog { get; set; } = string.Empty;

    private SongDto? CurrentSong { get; set; }

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
                await RefreshAsync();
            }
            while (await timer.WaitForNextTickAsync(_cts.Token));
        }
        catch (OperationCanceledException)
        {
            // Component is going away.
        }
    }

    private async Task RefreshAsync()
    {
        var activity = await Processor.GetActivityAsync(_offset, _cts.Token);

        if (activity is null)
        {
            // Processor unreachable — leave the last known output on screen rather than
            // blanking it. ProcessorStatus renders the banner.
            return;
        }

        if (activity.Current is null)
        {
            await GoIdleAsync();
            return;
        }

        if (activity.Current.Id != _currentSongId)
        {
            // A different song is running. Drop what we have and let the next tick pull
            // that song's log from offset zero.
            _currentSongId = activity.Current.Id;
            _lines.Clear();
            _offset = 0;
            CurrentSong = activity.Current;
            OutputLog = string.Empty;
            await InvokeAsync(StateHasChanged);
            return;
        }

        CurrentSong = activity.Current;
        Append(activity.Log);

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// The queue has gone quiet. The finished run's output stays on screen — it is the only
    /// record of what just happened — but we first collect anything written between the last
    /// poll and the job ending, which includes the post-processing messages.
    /// </summary>
    private async Task GoIdleAsync()
    {
        if (_currentSongId is { } finishedSongId)
        {
            var tail = await Processor.GetSongLogAsync(finishedSongId, _offset, _cts.Token);

            if (tail is not null)
            {
                Append(tail);
            }

            _currentSongId = null;
        }
        else if (CurrentSong is null)
        {
            // Already idle and nothing changed.
            return;
        }

        CurrentSong = null;
        await InvokeAsync(StateHasChanged);
    }

    private void Append(LogChunkDto chunk)
    {
        if (!string.IsNullOrEmpty(chunk.Text))
        {
            _lines.AddRange(chunk.Text
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => !string.IsNullOrWhiteSpace(line)));

            // Newest first, matching how the log used to be built by prepending each line.
            OutputLog = string.Join('\n', Enumerable.Reverse(_lines));
        }

        _offset = chunk.Offset;
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
