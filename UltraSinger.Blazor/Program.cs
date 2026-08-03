using UltraSinger.Blazor.Components;
using UltraSinger.Blazor.Configuration;
using UltraSinger.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<YouTubeAPIConfiguration>(opts =>
{
    var ytSection = builder.Configuration.GetSection("YouTubeAPI");
    opts.ApiKey = ytSection.GetValue<string>("ApiKey");
    opts.BackupKey = ytSection.GetValue<string>("BackupKey");
});

builder.Services.Configure<LibraryConfiguration>(builder.Configuration.GetSection("Library"));

var processorConfiguration = builder.Configuration.GetSection("Processor").Get<ProcessorConfiguration>()
                             ?? new ProcessorConfiguration();

if (string.IsNullOrWhiteSpace(processorConfiguration.BaseUrl))
{
    throw new InvalidOperationException(
        "Processor:BaseUrl is not configured. Point it at the machine running UltraSinger.Processor, e.g. http://localhost:5209.");
}

builder.Services.AddSingleton<ProcessorConnectionState>();

builder.Services.AddHttpClient<ProcessorApiClient>(client =>
{
    client.BaseAddress = new Uri(processorConfiguration.BaseUrl!.TrimEnd('/') + "/");
    // Long enough to cover a title lookup via yt-dlp on the processor, short enough that a
    // dead backend surfaces as an "unreachable" banner rather than a hung page.
    client.Timeout = TimeSpan.FromSeconds(30);
    
    if (!string.IsNullOrWhiteSpace(processorConfiguration.ApiKey))
    {
        client.DefaultRequestHeaders.Add("X-Api-Key", processorConfiguration.ApiKey);
    }
});

builder.Services.AddScoped<YouTubeAPIService>();
builder.Services.AddHostedService<BundleFetchService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
