using Microsoft.AspNetCore.Mvc;
using UltraSinger.Contracts;
using UltraSinger.Processor.Services;

namespace UltraSinger.Processor.Controllers;

public record MarkSungRequest(bool IsSung);

[ApiController]
[Route("api/playqueue")]
public class PlayQueueController(PlayQueueStore playQueue) : ControllerBase
{
    /// <summary>The "sing next" play queue, in order.</summary>
    [HttpGet]
    public ActionResult<IEnumerable<SongQueueItem>> GetAll() =>
        Ok(playQueue.GetAll().Select(x => x.ToDto()));

    [HttpPost]
    public ActionResult<SongQueueItem> Add([FromBody] SongQueueItem request)
    {
        var item = playQueue.Add(request.Title, request.Artist, request.Source, request.ExtraInfo, request.FilePath);
        return Ok(item.ToDto());
    }

    [HttpDelete("{id:guid}")]
    public ActionResult Remove(Guid id) =>
        playQueue.Remove(id) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/up")]
    public ActionResult MoveUp(Guid id) =>
        playQueue.MoveUp(id) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/down")]
    public ActionResult MoveDown(Guid id) =>
        playQueue.MoveDown(id) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/sung")]
    public ActionResult MarkSung(Guid id, [FromBody] MarkSungRequest request) =>
        playQueue.MarkSung(id, request.IsSung) ? NoContent() : NotFound();

    [HttpDelete]
    public ActionResult Clear()
    {
        playQueue.Clear();
        return NoContent();
    }
}
