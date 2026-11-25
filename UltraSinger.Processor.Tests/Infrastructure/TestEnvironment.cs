using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using UltraSinger.Processor.Configuration;
using UltraSinger.Processor.Services;

namespace UltraSinger.Processor.Tests.Infrastructure;

public sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>
/// A self-contained processor sandbox: library, database and UltraSinger checkout all live
/// under one <see cref="TempDirectory"/>, and every external tool defaults to a no-op fake
/// so <see cref="EnvironmentalValuesService.Validate"/> passes out of the box.
/// </summary>
public sealed class TestEnvironment : IDisposable
{
    public TestEnvironment()
    {
        Root = new TempDirectory();

        LibraryPath = Root.CreateDirectory("Storage 2", "UltraStar", "songs");
        DatabasePath = Root.CreateDirectory("data");
        ToolsPath = Root.CreateDirectory("tools");
        UltraSingerPath = Root.CreateDirectory("UltraSinger");

        Directory.CreateDirectory(Path.Combine(UltraSingerPath, "src"));
        File.WriteAllText(Path.Combine(UltraSingerPath, "src", "UltraSinger.py"), "# never executed: python is faked\n");

        Python = FakeTool.Create(ToolsPath, "python", "exit 0");
        YtDlp = FakeTool.Create(ToolsPath, "yt-dlp", "exit 0");
        Ffmpeg = FakeTool.Create(ToolsPath, "ffmpeg", "exit 0");
        Ffprobe = FakeTool.Create(ToolsPath, "ffprobe", "echo h264");
        Demucs = FakeTool.Create(ToolsPath, "demucs", "exit 0");
        SyncedLyrics = FakeTool.Create(ToolsPath, "syncedlyrics", "exit 0");

        Configuration = new UltraSingerConfiguration
        {
            PythonEnvironmentPath = Root.Combine("venv-unused"),
            PythonExecutable = Python,
            UltraSingerPath = UltraSingerPath,
            UltraStarDeluxeLocalLibraryPath = LibraryPath,
            DatabasePath = DatabasePath,
            YTDLPPath = YtDlp,
            YTDLPCookiesFromBrowser = null,
            FfmpegPath = Ffmpeg,
            FfprobePath = Ffprobe,
            VocalSeparationExecutable = Demucs,
            SyncedLyricsPath = SyncedLyrics,
            KaraokeLanguage = "en",
        };
    }

    public TempDirectory Root { get; }
    public string LibraryPath { get; }
    public string DatabasePath { get; }
    public string ToolsPath { get; }
    public string UltraSingerPath { get; }

    public string Python { get; }
    public string YtDlp { get; }
    public string Ffmpeg { get; }
    public string Ffprobe { get; }
    public string Demucs { get; }
    public string SyncedLyrics { get; }

    public UltraSingerConfiguration Configuration { get; set; }

    /// <summary>Where a given song's job directory will be, as the processor computes it.</summary>
    public string JobDirectory(Guid songId) =>
        Path.GetFullPath(Path.Combine(LibraryPath, "..", "_jobs", songId.ToString("N")));

    public string BundleDirectory => Path.Combine(DatabasePath, "bundles");

    public EnvironmentalValuesService CreateValues(string? contentRoot = null)
    {
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.ContentRootPath.Returns(contentRoot ?? Root.Path);

        return new EnvironmentalValuesService(
            new StaticOptionsMonitor<UltraSingerConfiguration>(Configuration),
            hostEnvironment);
    }

    /// <summary>Overrides a tool with a script body for this test.</summary>
    public string Fake(string name, string body) => FakeTool.Create(ToolsPath, name, body);

    public void Dispose() => Root.Dispose();
}
