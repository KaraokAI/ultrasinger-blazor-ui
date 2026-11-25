using Microsoft.Extensions.Options;
using UltraSinger.Processor.Configuration;

namespace UltraSinger.Processor.Services;

/// <summary>
/// Typed access to the <c>ProcessorOptions</c> configuration section.
/// Uses <see cref="IOptionsMonitor{T}"/> so edits to appsettings are picked up without a
/// restart, matching the re-read-per-access behaviour this had when it was static.
/// </summary>
public class EnvironmentalValuesService(
    IOptionsMonitor<UltraSingerConfiguration> options,
    IHostEnvironment environment)
{
    private UltraSingerConfiguration Configuration => options.CurrentValue;

    /// <summary>The uv virtualenv from <c>python/pyproject.toml</c>; tool defaults live under its <c>bin/</c>.</summary>
    public string PythonEnvironmentPath => ResolvePath(
        string.IsNullOrWhiteSpace(Configuration.PythonEnvironmentPath) ? Path.Combine("python", ".venv") : Configuration.PythonEnvironmentPath);

    public string UltraSingerPath => ResolvePath(
        string.IsNullOrWhiteSpace(Configuration.UltraSingerPath) ? "UltraSinger" : Configuration.UltraSingerPath);

    public string UltraSingerScriptPath => Path.Combine(UltraSingerPath, "src", "UltraSinger.py");

    public string PythonExecutable => ResolveTool(Configuration.PythonExecutable, "python");

    public IReadOnlyList<string> UltraSingerAdditionalArgs => SplitArguments(Configuration.UltraSingerAdditionalArgs);

    public string UltraStarDeluxeLocalLibraryPath => Configuration.UltraStarDeluxeLocalLibraryPath ?? throw new InvalidOperationException("Required configuration value ProcessorOptions:UltraStarDeluxeLocalLibraryPath is not set.");

    public string KaraokeLanguage => Configuration.KaraokeLanguage ?? "en";

    public string YTDLPPath => ResolveTool(Configuration.YTDLPPath, "yt-dlp");

    public string? YTDLPCookiesFromBrowser =>
        string.IsNullOrWhiteSpace(Configuration.YTDLPCookiesFromBrowser) ? null : Configuration.YTDLPCookiesFromBrowser;

    public string FfmpegPath => string.IsNullOrWhiteSpace(Configuration.FfmpegPath) ? "ffmpeg" : Configuration.FfmpegPath;

    public string FfprobePath => string.IsNullOrWhiteSpace(Configuration.FfprobePath) ? "ffprobe" : Configuration.FfprobePath;

    // Vocal separation settings (USDB downloads only)
    public bool EnableVocalSeparation => Configuration.EnableVocalSeparation;
    public string VocalSeparationExecutable => ResolveTool(Configuration.VocalSeparationExecutable, "demucs");
    public IReadOnlyList<string> VocalSeparationAdditionalArgs => SplitArguments(Configuration.VocalSeparationAdditionalArgs);

    // OpenAI settings
    public string? OpenAIKey => Configuration.OpenAIKey;
    public string OpenAIModel => Configuration.OpenAIModel ?? "gpt-5-nano";
    public bool EnableOpenAICorrections => Configuration.EnableOpenAICorrections;
    public string? OpenAIAdditionalInstructions => Configuration.OpenAIAdditionalInstructions;
    public bool SendTimestampsToOpenAI => Configuration.SendTimestampsToOpenAI;

    // syncedlyrics CLI settings
    public string SyncedLyricsPath => ResolveTool(Configuration.SyncedLyricsPath, "syncedlyrics");
    public SyncedLyricsMode SyncedLyricsMode => Configuration.SyncedLyricsMode;
    public string? SyncedLyricsProviders => Configuration.SyncedLyricsProviders;
    public string? SyncedLyricsLanguage => Configuration.SyncedLyricsLanguage;
    public bool SyncedLyricsEnhanced => Configuration.SyncedLyricsEnhanced;

    public TimeSpan SyncedLyricsTimeout =>
        TimeSpan.FromSeconds(Configuration.SyncedLyricsTimeoutSeconds > 0
            ? Configuration.SyncedLyricsTimeoutSeconds
            : 60);

    /// <summary>
    /// Directory for the SQLite databases. Falls back to the content root so the processor
    /// works with no extra configuration.
    /// </summary>
    public string DatabaseDirectory =>
        string.IsNullOrWhiteSpace(Configuration.DatabasePath)
            ? environment.ContentRootPath
            : Configuration.DatabasePath;

    public string SongDatabasePath => Path.Combine(DatabaseDirectory, "songs.db");

    public string HangfireDatabasePath => Path.Combine(DatabaseDirectory, "hangfire.db");

    /// <summary>
    /// Directory for finished song bundles awaiting pickup by the UI. Falls back to a
    /// subdirectory of <see cref="DatabaseDirectory"/> so it moves with the rest of the
    /// processor's persisted state by default.
    /// </summary>
    public string BundleStoragePath =>
        string.IsNullOrWhiteSpace(Configuration.BundleStoragePath)
            ? Path.Combine(DatabaseDirectory, "bundles")
            : Configuration.BundleStoragePath;

    /// <summary>
    /// ULTRASINGER_ADDITIONAL_VARIABLES needs to be in the form:
    /// MYVAR=value;OTHERVAR=othervalue
    ///
    /// Values including semicolons (;) are not supported at current time.
    ///
    /// Blank segments are discarded. An empty setting, or a trailing semicolon, would
    /// otherwise yield a variable with an empty name and break process start-up.
    /// </summary>
    public List<(string, string?)> GetUltraSingerAdditionalEnvironmentVariables()
    {
        var environmentVarList = Configuration.UltraSingerAdditionalEnvVars;
        if (string.IsNullOrWhiteSpace(environmentVarList))
        {
            return new List<(string, string?)>();
        }

        return environmentVarList
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            // Only the first '=' separates name from value; values may contain '=' themselves.
            .Select(envKeypair => envKeypair.Split('=', 2, StringSplitOptions.TrimEntries))
            .Select(envKeypair => (Name: envKeypair[0], Value: envKeypair.ElementAtOrDefault(1)))
            .Where(envKeypair => !string.IsNullOrWhiteSpace(envKeypair.Name))
            .ToList();
    }

    /// <summary>
    /// Validates everything a job will need up front, so a misconfigured processor can be
    /// reported through /api/health instead of only blowing up mid-run.
    /// </summary>
    public List<string> Validate()
    {
        var problems = new List<string>();

        void Require(string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"ProcessorOptions:{name} is not set.");
            }
        }

        Require(nameof(Configuration.UltraStarDeluxeLocalLibraryPath), Configuration.UltraStarDeluxeLocalLibraryPath);

        if (!string.IsNullOrWhiteSpace(Configuration.UltraStarDeluxeLocalLibraryPath)
            && !Directory.Exists(Configuration.UltraStarDeluxeLocalLibraryPath))
        {
            problems.Add($"UltraStar library path does not exist: {Configuration.UltraStarDeluxeLocalLibraryPath}");
        }

        if (!File.Exists(UltraSingerScriptPath))
        {
            problems.Add($"UltraSinger script not found at {UltraSingerScriptPath} (run `git submodule update --init`).");
        }

        void RequireExecutable(string name, string path)
        {
            if (!ExecutableExists(path))
            {
                problems.Add($"{name} not found at '{path}' (run `uv sync` in UltraSinger.Processor/python, or set ProcessorOptions:{name}).");
            }
        }

        RequireExecutable(nameof(Configuration.PythonExecutable), PythonExecutable);
        RequireExecutable(nameof(Configuration.YTDLPPath), YTDLPPath);
        RequireExecutable(nameof(Configuration.FfmpegPath), FfmpegPath);
        RequireExecutable(nameof(Configuration.FfprobePath), FfprobePath);

        if (Configuration.EnableOpenAICorrections)
        {
            if (string.IsNullOrWhiteSpace(Configuration.OpenAIKey))
            {
                problems.Add("OpenAI corrections are enabled but ProcessorOptions:OpenAIKey is not set.");
            }

            // Surfaced up front rather than at the end of a long transcription.
            RequireExecutable(nameof(Configuration.SyncedLyricsPath), SyncedLyricsPath);
        }

        if (Configuration.EnableVocalSeparation)
        {
            RequireExecutable(nameof(Configuration.VocalSeparationExecutable), VocalSeparationExecutable);
        }

        if (!Directory.Exists(DatabaseDirectory))
        {
            problems.Add($"Database directory does not exist: {DatabaseDirectory}");
        }

        return problems;
    }

    private string ResolvePath(string path) => Path.GetFullPath(path, environment.ContentRootPath);

    /// <summary>
    /// A configured value is used as-is when it's a bare command name (looked up on PATH) and
    /// resolved against the content root when it's a path. Unset falls back to the venv.
    /// </summary>
    private string ResolveTool(string? configured, string venvBinary)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(PythonEnvironmentPath, "bin", venvBinary);
        }

        return configured.Contains(Path.DirectorySeparatorChar) ? ResolvePath(configured) : configured;
    }

    private static bool ExecutableExists(string path)
    {
        if (path.Contains(Path.DirectorySeparatorChar))
        {
            return File.Exists(path);
        }

        var searchPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        return searchPath
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, path)));
    }

    private static IReadOnlyList<string> SplitArguments(string? arguments) =>
        string.IsNullOrWhiteSpace(arguments)
            ? []
            : arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
