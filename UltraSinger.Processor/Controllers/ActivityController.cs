using Microsoft.AspNetCore.Mvc;
using UltraSinger.Contracts;
using UltraSinger.Processor.Services;

namespace UltraSinger.Processor.Controllers;

[ApiController]
[Route("api/activity")]
public class ActivityController(SongQueueService queue) : ControllerBase
{
    /// <summary>
    /// What the processor is working on right now plus any new log output, in one call —
    /// this is the endpoint the output pane polls.
    /// </summary>
    [HttpGet]
    public ActionResult<CurrentActivityDto> Get([FromQuery] int sinceOffset = 0)
    {
        var current = queue.GetCurrent();

        if (current == null)
        {
            return Ok(new CurrentActivityDto());
        }

        return Ok(new CurrentActivityDto
        {
            Current = current.ToDto(),
            Log = current.ReadLogFrom(sinceOffset)
        });
    }
}
