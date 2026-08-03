using Hangfire;
using UltraSinger.Contracts;

namespace UltraSinger.Processor.Services;

/// <summary>
/// Reconciles song records against Hangfire's own view of their jobs.
///
/// <see cref="SongProcessingJob"/> is authoritative for the normal lifecycle; this only
/// exists to catch the cases it cannot report on itself — a job deleted from the Hangfire
/// dashboard, or one that never got to run. It replaces the old arrangement where the
/// Blazor UI polled Hangfire storage directly on every render tick.
/// </summary>
public class JobStateReconciler(ISongStore store, ILogger<JobStateReconciler> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private static readonly SongState[] Unsettled =
    [
        SongState.NOT_STARTED, SongState.IN_PROGRESS, SongState.UNKNOWN
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Reconcile();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Job state reconciliation pass failed.");
            }
        }
    }

    private void Reconcile()
    {
        using var connection = JobStorage.Current.GetConnection();

        foreach (var song in store.GetAll().Where(x => Unsettled.Contains(x.State)))
        {
            if (string.IsNullOrWhiteSpace(song.JobId))
            {
                continue;
            }

            var job = connection.GetJobData(song.JobId);

            if (job == null)
            {
                // GetJobData is [CanBeNull] — the job has aged out of storage or was removed.
                song.State = SongState.UNKNOWN;
                continue;
            }

            var mapped = job.State switch
            {
                "Succeeded" => SongState.COMPLETED,
                "Failed" or "Deleted" => SongState.FAILED,
                "Processing" => SongState.IN_PROGRESS,
                "Enqueued" or "Scheduled" or "Awaiting" => SongState.NOT_STARTED,
                _ => SongState.UNKNOWN,
            };

            if (mapped == song.State)
            {
                continue;
            }

            // Don't walk a record backwards from IN_PROGRESS to NOT_STARTED just because
            // Hangfire's state write hasn't landed yet.
            if (song.State == SongState.IN_PROGRESS && mapped == SongState.NOT_STARTED)
            {
                continue;
            }

            song.State = mapped;

            if (mapped is SongState.COMPLETED or SongState.FAILED)
            {
                song.CompletedAt ??= DateTime.Now;
            }
        }
    }
}
