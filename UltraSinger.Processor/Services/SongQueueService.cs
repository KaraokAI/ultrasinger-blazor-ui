using Hangfire;
using UltraSinger.Contracts;
using UltraSinger.Processor.Constants;
using UltraSinger.Processor.Entities;

namespace UltraSinger.Processor.Services;

public enum EnqueueOutcome
{
    Queued,
    Duplicate
}

/// <summary>
/// Turns an enqueue request into a stored record plus a Hangfire job.
/// The public half of what <c>SongProcessorService</c> used to do.
/// </summary>
public class SongQueueService(
    ISongStore store,
    IBackgroundJobClient jobClient,
    YoutubeMetadataService youtubeMetadataService,
    ILogger<SongQueueService> logger)
{
    public async Task<(EnqueueOutcome Outcome, SongDto Song)> EnqueueAsync(EnqueueSongRequest request)
    {
        if (store.HasActiveOrCompleted(request.Url))
        {
            // Already processed/processing/will be processed.
            var existing = store.GetAll().First(x => x.Url == request.Url);
            logger.LogInformation("Rejected duplicate enqueue for {Url}", request.Url);
            return (EnqueueOutcome.Duplicate, existing.ToDto());
        }

        var title = request.Title;

        if (string.IsNullOrWhiteSpace(title))
        {
            // The UI no longer has yt-dlp, so resolving the title is our job now.
            title = await TryResolveTitleAsync(request.Url);
        }

        var record = store.Add(request.Url, title);

        var songId = record.Id;
        record.JobId = jobClient.Enqueue<SongProcessingJob>(Queues.SongQueue, job => job.ProcessAsync(songId));
        record.State = SongState.NOT_STARTED;

        logger.LogInformation("Queued {Url} as song {SongId} (job {JobId})", record.Url, record.Id, record.JobId);

        return (EnqueueOutcome.Queued, record.ToDto());
    }

    private async Task<string?> TryResolveTitleAsync(string url)
    {
        try
        {
            return await youtubeMetadataService.GetTitleOfVideo(url);
        }
        catch (Exception ex)
        {
            // A missing title is cosmetic — the URL is shown instead. Never block the queue for it.
            logger.LogWarning(ex, "Could not resolve title for {Url}", url);
            return null;
        }
    }

    public SongRecord? Get(Guid id) => store.Get(id);

    public IReadOnlyList<SongRecord> GetAll() => store.GetAll();

    public SongRecord? GetCurrent() => store.GetCurrent();
}
