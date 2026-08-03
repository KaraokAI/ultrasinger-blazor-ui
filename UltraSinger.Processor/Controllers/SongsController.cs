using Microsoft.AspNetCore.Mvc;
using UltraSinger.Contracts;
using UltraSinger.Processor.Services;

namespace UltraSinger.Processor.Controllers;

[ApiController]
[Route("api/songs")]
public class SongsController(SongQueueService queue) : ControllerBase
{
    /// <summary>Queue a song for processing.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(SongDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(SongDto), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SongDto>> Enqueue([FromBody] EnqueueSongRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new { error = "A url is required." });
        }

        var (outcome, song) = await queue.EnqueueAsync(request);

        return outcome == EnqueueOutcome.Duplicate
            ? Conflict(song)
            : Accepted(song);
    }

    /// <summary>All known songs, newest first.</summary>
    [HttpGet]
    public ActionResult<IEnumerable<SongDto>> GetAll() =>
        Ok(queue.GetAll().Select(x => x.ToDto()));

    [HttpGet("{id:guid}")]
    public ActionResult<SongDto> Get(Guid id)
    {
        var song = queue.Get(id);
        return song == null ? NotFound() : Ok(song.ToDto());
    }

    /// <summary>
    /// Incremental log tail. Pass the <c>Offset</c> from the previous response as
    /// <paramref name="sinceOffset"/> to receive only what has been written since.
    /// </summary>
    [HttpGet("{id:guid}/log")]
    public ActionResult<LogChunkDto> GetLog(Guid id, [FromQuery] int sinceOffset = 0)
    {
        var song = queue.Get(id);
        return song == null ? NotFound() : Ok(song.ReadLogFrom(sinceOffset));
    }

    /// <summary>Downloads the finished song bundle, once it exists.</summary>
    [HttpGet("{id:guid}/bundle")]
    public ActionResult GetBundle(Guid id)
    {
        var song = queue.Get(id);

        if (song == null
            || song.State != SongState.COMPLETED
            || string.IsNullOrWhiteSpace(song.BundlePath)
            || !System.IO.File.Exists(song.BundlePath))
        {
            return NotFound();
        }

        var invalid = Path.GetInvalidFileNameChars();
        var safeName = string.Concat((song.Title ?? song.Url).Select(c => invalid.Contains(c) ? '_' : c));

        return PhysicalFile(song.BundlePath!, "application/zip", $"{safeName}.zip");
    }

    /// <summary>
    /// Marks a bundle as fetched. The UI calls this once it has successfully downloaded and
    /// extracted the bundle locally — idempotent, so a retry after a dropped ack is safe.
    /// </summary>
    [HttpPost("{id:guid}/bundle/ack")]
    public ActionResult AckBundleFetched(Guid id)
    {
        var song = queue.Get(id);

        if (song == null)
        {
            return NotFound();
        }

        song.FetchedAt = DateTime.Now;
        return NoContent();
    }
}
