using Microsoft.AspNetCore.Mvc;
using UltraSinger.Contracts;
using UltraSinger.Processor.Services;

namespace UltraSinger.Processor.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController(
    EnvironmentalValuesService environmentalValues,
    SongQueueService queue) : ControllerBase
{
    [HttpGet]
    public ActionResult<ProcessorHealthDto> Get()
    {
        var problems = environmentalValues.Validate();
        var songs = queue.GetAll();

        return Ok(new ProcessorHealthDto
        {
            Healthy = problems.Count == 0,
            Problems = problems,
            OpenAICorrectionsEnabled = environmentalValues.EnableOpenAICorrections,
            QueuedCount = songs.Count(x => x.State == SongState.NOT_STARTED),
            InProgressCount = songs.Count(x => x.State == SongState.IN_PROGRESS)
        });
    }
}
