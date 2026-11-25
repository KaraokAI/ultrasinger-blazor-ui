namespace UltraSinger.Processor.Configuration;

public record UltraSingerConfiguration
{
    /// <summary>
    /// The uv-managed virtualenv holding UltraSinger's dependencies, demucs, syncedlyrics and
    /// yt-dlp (see <c>python/pyproject.toml</c>). Tool paths below default to binaries inside
    /// it. Relative paths resolve against the content root. Defaults to <c>python/.venv</c>.
    /// </summary>
    public string? PythonEnvironmentPath { get; init; }

    /// <summary>Defaults to <c>bin/python</c> inside <see cref="PythonEnvironmentPath"/>.</summary>
    public string? PythonExecutable { get; init; }

    /// <summary>UltraSinger repo root; <c>src/UltraSinger.py</c> is appended. Defaults to the <c>UltraSinger</c> submodule.</summary>
    public string? UltraSingerPath { get; init; }

    public string? UltraStarDeluxeLocalLibraryPath { get; init; }
    public string? KaraokeLanguage { get; init; }

    /// <summary>Whitespace-separated extra arguments. Quoting isn't supported.</summary>
    public string UltraSingerAdditionalArgs { get; init; } = "";

    /// Additional variables need to be in the form:
    /// MYVAR=value;OTHERVAR=othervalue
    ///
    /// Values including semicolons (;) are not supported at current time.
    public string? UltraSingerAdditionalEnvVars { get; init; }

    /// <summary>Defaults to <c>bin/yt-dlp</c> inside <see cref="PythonEnvironmentPath"/>.</summary>
    public string? YTDLPPath { get; init; }

    /// <summary>
    /// Passed to yt-dlp's <c>--cookies-from-browser</c> (e.g. <c>firefox</c>, or
    /// <c>firefox:/path/to/profile</c>). Empty disables it.
    /// </summary>
    public string? YTDLPCookiesFromBrowser { get; init; } = "firefox";

    /// <summary>
    /// ffmpeg/ffprobe executable used to verify and, if necessary, re-encode USDB video
    /// downloads to H.264 before bundling. Defaults to "ffmpeg"/"ffprobe" on PATH.
    /// </summary>
    public string? FfmpegPath { get; init; }
    public string? FfprobePath { get; init; }

    // Vocal separation configuration (USDB downloads only).
    public bool EnableVocalSeparation { get; init; } = false;

    /// <summary>Defaults to <c>bin/demucs</c> inside <see cref="PythonEnvironmentPath"/>.</summary>
    public string? VocalSeparationExecutable { get; init; }

    /// <summary>Whitespace-separated extra arguments. Quoting isn't supported.</summary>
    public string VocalSeparationAdditionalArgs { get; init; } = "-n htdemucs_ft --two-stems=vocals";

    /// <summary>
    /// Directory holding <c>songs.db</c> and <c>hangfire.db</c>. Defaults to the content root.
    /// </summary>
    public string? DatabasePath { get; init; }

    /// <summary>
    /// Directory holding the zipped song bundles the UI downloads. Defaults to a
    /// <c>bundles</c> subdirectory of <see cref="DatabasePath"/>.
    /// </summary>
    public string? BundleStoragePath { get; init; }

    // OpenAI configuration
    public string? OpenAIKey { get; init; }
    public string? OpenAIModel { get; init; }
    public bool EnableOpenAICorrections { get; init; } = false;

    /// <summary>Appended verbatim to the OpenAI system prompt. Per-deployment tweaks.</summary>
    public string? OpenAIAdditionalInstructions { get; init; }

    /// <summary>
    /// When true the reference lyrics keep their <c>[mm:ss.xx]</c> tags so the model can align
    /// lines. Set false to strip them if that ever confuses the model.
    /// </summary>
    public bool SendTimestampsToOpenAI { get; init; } = true;

    // syncedlyrics CLI configuration

    /// <summary>Defaults to <c>bin/syncedlyrics</c> inside <see cref="PythonEnvironmentPath"/>.</summary>
    public string? SyncedLyricsPath { get; init; }

    public SyncedLyricsMode SyncedLyricsMode { get; init; } = SyncedLyricsMode.PreferSynced;

    /// <summary>Space-separated provider names, e.g. <c>lrclib musixmatch</c>. Empty means all.</summary>
    public string? SyncedLyricsProviders { get; init; }

    public string? SyncedLyricsLanguage { get; init; }

    public bool SyncedLyricsEnhanced { get; init; } = false;

    public int SyncedLyricsTimeoutSeconds { get; init; } = 60;
}
