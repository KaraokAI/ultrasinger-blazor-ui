using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace UltraSinger.Processor.Tests.Infrastructure;

/// <summary>
/// Hosts the real processor in-memory, pointed at a <see cref="TestEnvironment"/> sandbox.
/// Hangfire's job client is replaced with a substitute, so enqueueing never runs a job.
/// </summary>
public sealed class ProcessorAppFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-api-key";

    public TestEnvironment Env { get; } = new();

    public IBackgroundJobClient JobClient { get; } = Substitute.For<IBackgroundJobClient>();

    public ProcessorAppFactory()
    {
        JobClient.Create(Arg.Any<Job>(), Arg.Any<IState>()).Returns("job-1");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": that would load the developer's user secrets.
        builder.UseEnvironment("Testing");

        var config = Env.Configuration;
        var settings = new Dictionary<string, string?>
        {
            ["PythonExecutable"] = config.PythonExecutable,
            ["UltraSingerPath"] = config.UltraSingerPath,
            ["UltraStarDeluxeLocalLibraryPath"] = config.UltraStarDeluxeLocalLibraryPath,
            ["DatabasePath"] = config.DatabasePath,
            ["YTDLPPath"] = config.YTDLPPath,
            ["YTDLPCookiesFromBrowser"] = "",
            ["FfmpegPath"] = config.FfmpegPath,
            ["FfprobePath"] = config.FfprobePath,
            ["VocalSeparationExecutable"] = config.VocalSeparationExecutable,
            ["SyncedLyricsPath"] = config.SyncedLyricsPath,
            ["EnableVocalSeparation"] = "false",
            ["EnableOpenAICorrections"] = "false",
        };

        foreach (var (key, value) in settings)
        {
            builder.UseSetting($"ProcessorOptions:{key}", value);
        }

        builder.UseSetting("Processor:ApiKey", ApiKey);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBackgroundJobClient>();
            services.AddSingleton(JobClient);
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            Env.Dispose();
        }
    }
}
