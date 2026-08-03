using System.IO.Compression;
using Microsoft.Extensions.Options;
using UltraSinger.Contracts;
using UltraSinger.Blazor.Configuration;

namespace UltraSinger.Blazor.Services;

/// <summary>
/// Polls the processor for songs that are <see cref="SongState.COMPLETED"/> but not yet
/// fetched, downloads each bundle, extracts it into
/// <see cref="LibraryConfiguration.LocalPath"/>, and acks it so it isn't fetched again.
///
/// A no-op while <c>LocalPath</c> is unset, matching the "unconfigured means skipped"
/// convention <c>ApiKeyMiddleware</c> already uses on the processor side — a pure viewer
/// instance needs no extra configuration.
/// </summary>
public class BundleFetchService(
    ProcessorApiClient processor,
    IOptionsMonitor<LibraryConfiguration> options,
    ILogger<BundleFetchService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var config = options.CurrentValue;

            if (!string.IsNullOrWhiteSpace(config.LocalPath))
            {
                try
                {
                    await FetchPendingBundlesAsync(config.LocalPath, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Bundle fetch pass failed.");
                }
            }

            await DelayAsync(config.PollIntervalSeconds, stoppingToken);
        }
    }

    private async Task FetchPendingBundlesAsync(string localPath, CancellationToken cancellationToken)
    {
        var songs = await processor.GetSongsAsync(cancellationToken);
        var pending = songs.Where(s => s.State == SongState.COMPLETED && s.FetchedAt == null);

        foreach (var song in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await FetchOneAsync(song, localPath, cancellationToken);
        }
    }

    /// <summary>
    /// Downloads, extracts and acks one song. Any failure just skips this song for this
    /// pass — <see cref="SongDto.FetchedAt"/> stays unset, so the next poll retries, and
    /// extraction always overwrites so a partial retry is safe.
    /// </summary>
    private async Task FetchOneAsync(SongDto song, string localPath, CancellationToken cancellationToken)
    {
        var tempZip = await processor.DownloadBundleAsync(song.Id, cancellationToken);

        if (tempZip is null)
        {
            logger.LogWarning("Could not download bundle for {SongId}; will retry next poll.", song.Id);
            return;
        }

        try
        {
            Directory.CreateDirectory(localPath);
            ZipFile.ExtractToDirectory(tempZip, localPath, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to extract bundle for {SongId}; will retry next poll.", song.Id);
            return;
        }
        finally
        {
            File.Delete(tempZip);
        }

        if (!await processor.AckBundleFetchedAsync(song.Id, cancellationToken))
        {
            logger.LogWarning("Extracted bundle for {SongId} but ack failed; will retry next poll.", song.Id);
        }
    }

    private static async Task DelayAsync(int pollIntervalSeconds, CancellationToken cancellationToken)
    {
        var seconds = pollIntervalSeconds > 0 ? pollIntervalSeconds : 15;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
