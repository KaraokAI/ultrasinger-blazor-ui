namespace UltraSinger.Processor.Services;

/// <summary>
/// Flushes dirty song records to SQLite on a timer.
///
/// Write-behind rather than write-through: UltraSinger emits a log line at a time, and a
/// database write per line would tie disk traffic to how chatty the run is. Flushing on a
/// fixed interval bounds it instead. The cost is losing at most one interval's worth of log
/// text if the processor is hard-killed — state changes matter more than log tails, and
/// those are recovered on startup anyway.
/// </summary>
public class SongPersistenceService(
    SqliteSongStore store,
    SongDatabase database,
    ILogger<SongPersistenceService> logger) : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Flush();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            // Final flush so a clean shutdown never loses the tail of a run.
            Flush();
        }
    }

    private void Flush()
    {
        try
        {
            var dirty = store.GetDirty();

            if (dirty.Count == 0)
            {
                return;
            }

            // Snapshotting clears the dirty flag under the record's own lock, so anything
            // written during the save is simply picked up next time round.
            database.Save(dirty.Select(x => x.TakeSnapshotAndClean()).ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to flush song records to disk.");
        }
    }
}
