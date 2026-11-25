using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UltraSinger.Blazor.Configuration;
using UltraSinger.Blazor.Services;

namespace UltraSinger.Blazor.Tests.Infrastructure;

/// <summary>
/// bUnit context with the UI's real services registered, all talking to in-memory fakes:
/// <see cref="Processor"/> for the processor API, <see cref="Usdb"/> and
/// <see cref="YouTube"/> for the external sites, and <see cref="Library"/> on disk.
/// </summary>
public abstract class UiTestContext : BunitContext
{
    protected FakeProcessor Processor { get; } = new();

    protected RoutingHandler Usdb { get; } = new();

    protected RoutingHandler YouTube { get; } = new();

    protected LibraryFixture Library { get; } = new();

    protected ProcessorConnectionState ConnectionState { get; } = new();

    protected UiTestContext()
    {
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        Services.AddSingleton(ConnectionState);
        Services.AddSingleton(_ => Processor.CreateClient(ConnectionState));
        Services.AddSingleton<BlazorSongQueueService>();
        Services.AddSingleton(_ => new LocalLibraryService(
            Options.Create(new LibraryConfiguration { LocalPath = Library.Path }),
            NullLogger<LocalLibraryService>.Instance));
        Services.AddSingleton(_ => new UsdbService(
            Options.Create(new UsdbConfiguration { BaseUrl = "http://usdb.test" }),
            NullLogger<UsdbService>.Instance,
            Usdb));
        Services.AddSingleton(_ => new YouTubeAPIService(
            Options.Create(new YouTubeApiConfiguration { ApiKey = "main", BackupKey = "backup" }),
            new HttpClient(YouTube)));
        Services.AddSingleton<UnifiedSearchService>();
    }

    protected CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    /// <summary>
    /// For conditions that don't cause a re-render (bUnit's WaitForAssertion only re-checks
    /// when the component renders).
    /// </summary>
    protected async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for condition");
            await Task.Delay(25, Ct);
        }
    }

    /// <summary>The play queue service only learns the queue by polling; start it like the host would.</summary>
    protected async Task<BlazorSongQueueService> StartQueueServiceAsync()
    {
        var service = Services.GetRequiredService<BlazorSongQueueService>();
        var refreshed = new TaskCompletionSource();
        service.OnQueueChanged += () => refreshed.TrySetResult();

        await service.StartAsync(Ct);
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        return service;
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (Services.GetService<BlazorSongQueueService>() is { } queue)
        {
            await queue.StopAsync(CancellationToken.None);
        }

        await base.DisposeAsyncCore();
        Library.Dispose();
    }
}
