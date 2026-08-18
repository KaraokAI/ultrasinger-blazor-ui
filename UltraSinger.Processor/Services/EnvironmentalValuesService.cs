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

    public string UltraSingerPath => Configuration.UltraSingerPath ?? throw new InvalidOperationException("Required configuration value ProcessorOptions:UltraSingerPath is not set.");

    public string PythonExecutable => Configuration.PythonExecutable ?? throw new InvalidOperationException("Required configuration value ProcessorOptions:PythonExecutable is not set.");

    public string UltraSingerAdditionalArgs => Configuration.UltraSingerAdditionalArgs;

    public string PythonArguments => Configuration.PythonArguments ?? "";

    public string UltraStarDeluxeLocalLibraryPath => Configuration.UltraStarDeluxeLocalLibraryPath ?? throw new InvalidOperationException("Required configuration value ProcessorOptions:UltraStarDeluxeLocalLibraryPath is not set.");

    public string UltraStarDeluxeWSLPath => Configuration.UltraStarDeluxeWSLPath ?? throw new InvalidOperationException("Required configuration value ProcessorOptions:UltraStarDeluxeWSLPath is not set.");

    public string KaraokeLanguage => Configuration.KaraokeLanguage ?? "en";

    public string YTDLPPath => Configuration.YTDLPPath ?? throw new InvalidOperationException("Required configuration value ProcessorOptions:YTDLPPath is not set.");

    public string FfmpegPath => string.IsNullOrWhiteSpace(Configuration.FfmpegPath) ? "ffmpeg" : Configuration.FfmpegPath;

    public string FfprobePath => string.IsNullOrWhiteSpace(Configuration.FfprobePath) ? "ffprobe" : Configuration.FfprobePath;

    // Vocal separation settings (USDB downloads only)
    public bool EnableVocalSeparation => Configuration.EnableVocalSeparation;
    public string? VocalSeparationExecutable => Configuration.VocalSeparationExecutable;
    public string VocalSeparationArguments => Configuration.VocalSeparationArguments ?? "";
    public string VocalSeparationAdditionalArgs => Configuration.VocalSeparationAdditionalArgs;

    // OpenAI settings
    public string? OpenAIKey => Configuration.OpenAIKey;
    public string OpenAIModel => Configuration.OpenAIModel ?? "gpt-5-nano";
    public bool EnableOpenAICorrections => Configuration.EnableOpenAICorrections;
    public string? OpenAIAdditionalInstructions => Configuration.OpenAIAdditionalInstructions;
    public bool SendTimestampsToOpenAI => Configuration.SendTimestampsToOpenAI;

    // syncedlyrics CLI settings
    public string? SyncedLyricsPath => Configuration.SyncedLyricsPath;
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
    /// otherwise yield a variable with an empty name, and Windows rejects the whole
    /// environment block for that — every job then dies with Win32 error 87 ("the parameter
    /// is incorrect") before the interpreter is even started.
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
            .Select(envKeypair => envKeypair.Split('='))
            .Select(envKeypair => (Name: envKeypair.First().Trim(), Value: envKeypair.ElementAtOrDefault(1)))
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

        Require(nameof(Configuration.PythonExecutable), Configuration.PythonExecutable);
        Require(nameof(Configuration.UltraSingerPath), Configuration.UltraSingerPath);
        Require(nameof(Configuration.UltraStarDeluxeWSLPath), Configuration.UltraStarDeluxeWSLPath);
        Require(nameof(Configuration.UltraStarDeluxeLocalLibraryPath), Configuration.UltraStarDeluxeLocalLibraryPath);
        Require(nameof(Configuration.YTDLPPath), Configuration.YTDLPPath);

        if (!string.IsNullOrWhiteSpace(Configuration.UltraStarDeluxeLocalLibraryPath)
            && !Directory.Exists(Configuration.UltraStarDeluxeLocalLibraryPath))
        {
            problems.Add($"UltraStar library path does not exist: {Configuration.UltraStarDeluxeLocalLibraryPath}");
        }

        if (Configuration.EnableOpenAICorrections)
        {
            if (string.IsNullOrWhiteSpace(Configuration.OpenAIKey))
            {
                problems.Add("OpenAI corrections are enabled but ProcessorOptions:OpenAIKey is not set.");
            }

            // Surfaced up front rather than at the end of a long transcription.
            if (string.IsNullOrWhiteSpace(Configuration.SyncedLyricsPath))
            {
                problems.Add("OpenAI corrections are enabled but ProcessorOptions:SyncedLyricsPath is not set.");
            }
            else if (!File.Exists(Configuration.SyncedLyricsPath))
            {
                problems.Add($"syncedlyrics executable not found at: {Configuration.SyncedLyricsPath}");
            }
        }

        if (!Directory.Exists(DatabaseDirectory))
        {
            problems.Add($"Database directory does not exist: {DatabaseDirectory}");
        }

        if (Configuration.EnableVocalSeparation && string.IsNullOrWhiteSpace(Configuration.VocalSeparationExecutable))
        {
            problems.Add("Vocal separation is enabled but ProcessorOptions:VocalSeparationExecutable is not set.");
        }

        return problems;
    }
}
