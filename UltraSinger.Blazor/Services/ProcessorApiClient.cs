using System.Net;
using System.Net.Http.Json;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Services;

public enum EnqueueResult
{
    Queued,
    Duplicate,
    Failed
}

/// <summary>
/// The UI's only link to the processing backend. Everything the components used to do
/// in-process against <c>SongProcessorService</c> now goes over HTTP through here.
///
/// Transport failures are recorded on <see cref="ProcessorConnectionState"/> rather than
/// thrown, because these are called from timer ticks where an escaping exception would
/// tear down the render loop. Genuine cancellation still propagates so that a component
/// being disposed does not get reported as an outage.
/// </summary>
public class ProcessorApiClient(
    HttpClient http,
    ProcessorConnectionState connectionState,
    ILogger<ProcessorApiClient> logger)
{
    public async Task<EnqueueResult> EnqueueAsync(
        string url,
        string? title = null,
        SongSource source = SongSource.YouTube,
        int? usdbSongId = null,
        string? ultraStarTxt = null,
        CancellationToken cancellationToken = default)
    {
        return await EnqueueAsync(new EnqueueSongRequest
        {
            Url = url,
            Title = title,
            Source = source,
            UsdbSongId = usdbSongId,
            UltraStarTxt = ultraStarTxt
        }, cancellationToken);
    }

    public async Task<EnqueueResult> EnqueueAsync(EnqueueSongRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync(
                "api/songs",
                request,
                cancellationToken);

            connectionState.MarkReachable();

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return EnqueueResult.Duplicate;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Enqueue of {Url} failed with {StatusCode}", request.Url, response.StatusCode);
                connectionState.MarkUnreachable($"Processor rejected the request ({(int)response.StatusCode}).");
                return EnqueueResult.Failed;
            }

            return EnqueueResult.Queued;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            MarkUnreachable(ex);
            return EnqueueResult.Failed;
        }
    }

    public async Task<IReadOnlyList<SongDto>> GetSongsAsync(CancellationToken cancellationToken = default) =>
        await GetAsync<List<SongDto>>("api/songs", cancellationToken) ?? [];

    public Task<CurrentActivityDto?> GetActivityAsync(int sinceOffset, CancellationToken cancellationToken = default) =>
        GetAsync<CurrentActivityDto>($"api/activity?sinceOffset={sinceOffset}", cancellationToken);

    /// <summary>
    /// Reads one specific song's log. Used to collect the tail of a job that finished
    /// between two activity polls, which <c>api/activity</c> can no longer report because
    /// nothing is "current" any more.
    /// </summary>
    public Task<LogChunkDto?> GetSongLogAsync(Guid songId, int sinceOffset, CancellationToken cancellationToken = default) =>
        GetAsync<LogChunkDto>($"api/songs/{songId}/log?sinceOffset={sinceOffset}", cancellationToken);

    public Task<ProcessorHealthDto?> GetHealthAsync(CancellationToken cancellationToken = default) =>
        GetAsync<ProcessorHealthDto>("api/health", cancellationToken);

    /// <summary>
    /// Downloads a finished song's bundle to a temp file and returns its path, or
    /// <c>null</c> if it isn't ready yet or the call failed.
    /// </summary>
    public async Task<string?> DownloadBundleAsync(Guid songId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(
                $"api/songs/{songId}/bundle", HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            connectionState.MarkReachable();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Bundle download for {SongId} failed with {StatusCode}", songId, response.StatusCode);
                return null;
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"{songId:N}.zip");
            await using var fileStream = File.Create(tempPath);
            await response.Content.CopyToAsync(fileStream, cancellationToken);
            return tempPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            MarkUnreachable(ex);
            return null;
        }
    }

    /// <summary>
    /// Tells the processor a bundle has been fetched. Idempotent — safe to retry.
    /// </summary>
    public async Task<bool> AckBundleFetchedAsync(Guid songId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await http.PostAsync($"api/songs/{songId}/bundle/ack", content: null, cancellationToken);
            connectionState.MarkReachable();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Bundle ack for {SongId} failed with {StatusCode}", songId, response.StatusCode);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            MarkUnreachable(ex);
            return false;
        }
    }

    private async Task<T?> GetAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        try
        {
            var result = await http.GetFromJsonAsync<T>(requestUri, cancellationToken);
            connectionState.MarkReachable();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            MarkUnreachable(ex);
            return default;
        }
    }

    private void MarkUnreachable(Exception ex)
    {
        connectionState.MarkUnreachable(ex.Message);
        logger.LogWarning(ex, "Processor call failed against {BaseAddress}", http.BaseAddress);
    }
}
