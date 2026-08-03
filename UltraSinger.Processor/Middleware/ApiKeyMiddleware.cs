using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using UltraSinger.Processor.Configuration;

namespace UltraSinger.Processor.Middleware;

/// <summary>
/// Guards <c>/api</c> with a shared secret supplied in the <c>X-Api-Key</c> header.
///
/// When <c>Processor:ApiKey</c> is not configured the guard is skipped entirely so local
/// development needs no setup. That also means an unconfigured processor is wide open —
/// set the key before putting it on any network you do not control.
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next, IOptionsMonitor<ProcessorApiConfiguration> options)
{
    public const string HeaderName = "X-Api-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        var expected = options.CurrentValue.ApiKey;

        if (string.IsNullOrWhiteSpace(expected) || !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !FixedTimeEquals(provided.ToString(), expected))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = $"Missing or invalid {HeaderName} header." });
            return;
        }

        await next(context);
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
}
