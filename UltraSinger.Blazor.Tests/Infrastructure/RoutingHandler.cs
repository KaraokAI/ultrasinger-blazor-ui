using System.Net;
using System.Text;

namespace UltraSinger.Blazor.Tests.Infrastructure;

public record RecordedRequest(HttpMethod Method, string PathAndQuery, string? Body);

/// <summary>
/// An <see cref="HttpMessageHandler"/> answering from a route function, recording every
/// request. Unrouted requests get a 404, so nothing ever reaches the real network.
/// </summary>
public class RoutingHandler(Func<HttpRequestMessage, string?, HttpResponseMessage?>? route = null) : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];

    public Func<HttpRequestMessage, string?, HttpResponseMessage?>? Route { get; set; } = route;

    public IReadOnlyList<RecordedRequest> Requests
    {
        get { lock (_requests) return _requests.ToList(); }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        lock (_requests)
        {
            _requests.Add(new RecordedRequest(request.Method, request.RequestUri!.PathAndQuery, body));
        }

        return Route?.Invoke(request, body) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(html, Encoding.UTF8, "text/html")
    };
}
