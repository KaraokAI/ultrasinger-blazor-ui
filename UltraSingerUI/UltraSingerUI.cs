using Hangfire;
using UltraSingerUI.Components;
using UltraSingerUI.Configuration;
using UltraSingerUI.Constants;
using UltraSingerUI.Entities;
using UltraSingerUI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseInMemoryStorage()
    .UseFilter(new AutomaticRetryAttribute { Attempts = 0 }));

builder.Services
    .AddHangfireServer(conf =>
    {
        conf.Queues = new[] { Queues.SongQueue };
        conf.WorkerCount = 1;
    });

builder.Services.Configure<YouTubeAPIConfiguration>(opts =>
{
    opts.ApiKey = builder.Configuration["YT_API_KEY"];
});

EnvironmentalValuesService.Configuration = builder.Configuration;

builder.Services
    .AddSingleton<SongQueue>()
    .AddScoped<SongProcessorService>()
    .AddScoped<YoutubeMetadataService>()
    .AddScoped<YouTubeAPIService>();

var app = builder.Build();
// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHangfireDashboard();
app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapHangfireDashboard();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();