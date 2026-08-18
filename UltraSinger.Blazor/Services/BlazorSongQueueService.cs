using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Services;

/// <summary>
/// Client-side cache of the processor's "sing next" play queue (see
/// <c>PlayQueueController</c>/<c>PlayQueueStore</c>). Polls the processor on a timer so the
/// queue survives a processor restart, while keeping the same synchronous-looking surface
/// (<see cref="Items"/>, <see cref="Count"/>, <see cref="OnQueueChanged"/>, and the mutation
/// methods) that callers already use from Razor <c>@onclick</c> handlers.
/// </summary>
public class BlazorSongQueueService(ProcessorApiClient processor, ILogger<BlazorSongQueueService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private IReadOnlyList<SongQueueItem> _items = [];
    private readonly object _lock = new();

    public event Action? OnQueueChanged;

    public IReadOnlyList<SongQueueItem> Items
    {
        get { lock (_lock) { return _items; } }
    }

    public int Count
    {
        get { lock (_lock) { return _items.Count; } }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        do
        {
            await RefreshAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var fetched = await processor.GetPlayQueueAsync(cancellationToken);

            lock (_lock)
            {
                _items = fetched;
            }

            OnQueueChanged?.Invoke();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh play queue from processor.");
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Enqueue(SongQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        FireAndRefresh(() => processor.EnqueuePlayQueueItemAsync(item));
    }

    public void Enqueue(string title, string artist = "", SongSource source = SongSource.Local, string? extraInfo = null, string? filePath = null) =>
        Enqueue(new SongQueueItem
        {
            Title = title,
            Artist = artist,
            Source = source,
            ExtraInfo = extraInfo,
            FilePath = filePath,
            QueuedAt = DateTime.Now
        });

    public void Remove(Guid id) => FireAndRefresh(() => processor.RemovePlayQueueItemAsync(id));

    public void MoveUp(Guid id) => FireAndRefresh(() => processor.MovePlayQueueItemUpAsync(id));

    public void MoveDown(Guid id) => FireAndRefresh(() => processor.MovePlayQueueItemDownAsync(id));

    public void MarkSung(Guid id) => FireAndRefresh(() => processor.MarkPlayQueueItemSungAsync(id, true));

    public void UnmarkSung(Guid id) => FireAndRefresh(() => processor.MarkPlayQueueItemSungAsync(id, false));

    public void Clear() => FireAndRefresh(() => processor.ClearPlayQueueAsync());

    private void FireAndRefresh(Func<Task> action)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Play queue mutation failed.");
            }

            await RefreshAsync(CancellationToken.None);
        });
    }
}
