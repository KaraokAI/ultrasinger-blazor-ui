using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using UltraSinger.Blazor.Services;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Tests.Infrastructure;

/// <summary>
/// An in-memory stand-in for UltraSinger.Processor's HTTP API, implementing just enough of
/// each endpoint for the UI's real <see cref="ProcessorApiClient"/> to talk to.
/// </summary>
public sealed class FakeProcessor : HttpMessageHandler
{
    public static readonly Uri BaseAddress = new("http://processor.test/");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly List<RecordedRequest> _requests = [];

    public List<SongDto> Songs { get; } = [];

    public List<SongQueueItem> PlayQueue { get; } = [];

    public Dictionary<Guid, byte[]> Bundles { get; } = [];

    public HashSet<Guid> AckedBundles { get; } = [];

    public ProcessorHealthDto Health { get; set; } = new() { Healthy = true };

    /// <summary>Response for <c>api/activity</c>, given the requested offset.</summary>
    public Func<int, CurrentActivityDto> Activity { get; set; } = _ => new CurrentActivityDto();

    /// <summary>Response for <c>api/songs/{id}/log</c>, given the requested offset.</summary>
    public Func<Guid, int, LogChunkDto> SongLog { get; set; } = (_, offset) => new LogChunkDto { Offset = offset, Text = "" };

    public HttpStatusCode EnqueueStatus { get; set; } = HttpStatusCode.Accepted;

    /// <summary>When set, every request fails as if the processor were down.</summary>
    public bool Unreachable { get; set; }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get { lock (_gate) return _requests.ToList(); }
    }

    public IEnumerable<T> RequestBodies<T>(HttpMethod method, string path) =>
        Requests
            .Where(r => r.Method == method && r.PathAndQuery == path && r.Body != null)
            .Select(r => JsonSerializer.Deserialize<T>(r.Body!, Json)!);

    public ProcessorApiClient CreateClient(ProcessorConnectionState? state = null) =>
        new(new HttpClient(this) { BaseAddress = BaseAddress },
            state ?? new ProcessorConnectionState(),
            NullLogger<ProcessorApiClient>.Instance);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.AbsolutePath.Trim('/');
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        var segments = path.Split('/');

        lock (_gate)
        {
            _requests.Add(new RecordedRequest(request.Method, request.RequestUri.PathAndQuery, body));

            if (Unreachable)
            {
                throw new HttpRequestException("Connection refused (processor.test:80)");
            }

            return Handle(request.Method, segments, query, body);
        }
    }

    private HttpResponseMessage Handle(HttpMethod method, string[] s, System.Collections.Specialized.NameValueCollection query, string? body)
    {
        var offset = int.TryParse(query["sinceOffset"], out var o) ? o : 0;

        switch (s)
        {
            case ["api", "health"]:
                return Ok(Health);

            case ["api", "activity"]:
                return Ok(Activity(offset));

            case ["api", "songs"] when method == HttpMethod.Get:
                return Ok(Songs);

            case ["api", "songs"] when method == HttpMethod.Post:
                return new HttpResponseMessage(EnqueueStatus) { Content = JsonContent.Create(new SongDto { Id = Guid.NewGuid(), Url = "u" }) };

            case ["api", "songs", var id, "log"]:
                return Ok(SongLog(Guid.Parse(id), offset));

            case ["api", "songs", var id, "bundle"] when Bundles.TryGetValue(Guid.Parse(id), out var zip):
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };

            case ["api", "songs", var id, "bundle", "ack"]:
                AckedBundles.Add(Guid.Parse(id));
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            case ["api", "playqueue"] when method == HttpMethod.Get:
                return Ok(PlayQueue);

            case ["api", "playqueue"] when method == HttpMethod.Post:
            {
                var item = JsonSerializer.Deserialize<SongQueueItem>(body!, Json)!;
                item.Id = Guid.NewGuid();
                PlayQueue.Add(item);
                return Ok(item);
            }

            case ["api", "playqueue"] when method == HttpMethod.Delete:
                PlayQueue.Clear();
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            case ["api", "playqueue", var id] when method == HttpMethod.Delete:
                return Command(PlayQueue.RemoveAll(x => x.Id == Guid.Parse(id)) > 0);

            case ["api", "playqueue", var id, "up"]:
                return Command(Swap(Guid.Parse(id), -1));

            case ["api", "playqueue", var id, "down"]:
                return Command(Swap(Guid.Parse(id), +1));

            case ["api", "playqueue", var id, "sung"]:
            {
                var item = PlayQueue.FirstOrDefault(x => x.Id == Guid.Parse(id));
                if (item == null) return Command(false);
                item.IsSung = JsonDocument.Parse(body!).RootElement.GetProperty("isSung").GetBoolean();
                item.SungAt = item.IsSung ? DateTime.Now : null;
                return Command(true);
            }

            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private bool Swap(Guid id, int direction)
    {
        var index = PlayQueue.FindIndex(x => x.Id == id);
        var other = index + direction;
        if (index < 0 || other < 0 || other >= PlayQueue.Count) return false;
        (PlayQueue[index], PlayQueue[other]) = (PlayQueue[other], PlayQueue[index]);
        return true;
    }

    private static HttpResponseMessage Ok<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static HttpResponseMessage Command(bool found) =>
        new(found ? HttpStatusCode.NoContent : HttpStatusCode.NotFound);
}
