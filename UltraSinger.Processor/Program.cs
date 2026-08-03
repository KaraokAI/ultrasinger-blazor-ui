using Hangfire;
using Hangfire.Storage.SQLite;
using UltraSinger.Processor.Configuration;
using UltraSinger.Processor.Constants;
using UltraSinger.Processor.Middleware;
using UltraSinger.Processor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.Configure<UltraSingerConfiguration>(builder.Configuration.GetSection("ProcessorOptions"));
builder.Services.Configure<ProcessorApiConfiguration>(builder.Configuration.GetSection("Processor"));

builder.Services
    .AddSingleton<SongDatabase>()
    .AddSingleton<SqliteSongStore>()
    .AddSingleton<ISongStore>(provider => provider.GetRequiredService<SqliteSongStore>())
    .AddScoped<YoutubeMetadataService>()
    .AddScoped<YTDLPService>()
    .AddScoped<SyncedLyricsService>()
    .AddScoped<OpenAIImproverService>()
    .AddScoped<SongQueueService>()
    // Resolved by Hangfire for each job execution.
    .AddScoped<SongProcessingJob>();

var setup = new EnvironmentalValuesService(
    new SimpleOptionsMonitor<UltraSingerConfiguration>(
        builder.Configuration.GetSection("ProcessorOptions").Get<UltraSingerConfiguration>() ??
        new UltraSingerConfiguration()),
    builder.Environment);

builder.Services.AddSingleton(setup);

if (setup.Validate().Any())
{
    throw new ArgumentException("Invalid configuration: " + string.Join(", ", setup.Validate()));
}

// The song store must exist and be loaded before Hangfire can start handing it jobs.
var databaseDirectory = setup
    .DatabaseDirectory;

Directory.CreateDirectory(databaseDirectory);

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSQLiteStorage(Path.Combine(databaseDirectory, "hangfire.db"))
    // Retained deliberately: a transcription interrupted by a restart should surface as
    // failed rather than silently re-running for another half hour.
    .UseFilter(new AutomaticRetryAttribute { Attempts = 0 }));

builder.Services.AddHangfireServer(conf =>
{
    conf.Queues = new[] { Queues.SongQueue };
    conf.WorkerCount = 1;
});

builder.Services.AddHostedService<SongPersistenceService>();
builder.Services.AddHostedService<JobStateReconciler>();

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Configuration validated");

// Load persisted songs (and fail any that were mid-flight last time) before serving traffic.
app.Services.GetRequiredService<SqliteSongStore>().Load();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseMiddleware<ApiKeyMiddleware>();

app.MapControllers();

// The jobs live here now, so the dashboard does too.
app.MapHangfireDashboard();

app.Run();

/// <summary>
/// Minimal <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/> so configuration can
/// be read during startup, before the service provider exists.
/// </summary>
internal sealed class SimpleOptionsMonitor<T>(T value) : Microsoft.Extensions.Options.IOptionsMonitor<T>
{
    public T CurrentValue { get; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
