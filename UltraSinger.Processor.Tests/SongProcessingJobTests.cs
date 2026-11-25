using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UltraSinger.Contracts;
using UltraSinger.Processor.Entities;
using UltraSinger.Processor.Services;
using UltraSinger.Processor.Tests.Infrastructure;

namespace UltraSinger.Processor.Tests;

/// <summary>
/// Runs the real job against fake executables (see <see cref="FakeTool"/>) standing in for
/// UltraSinger, yt-dlp, demucs and ffmpeg — no GPU, models or network involved.
/// </summary>
public sealed class SongProcessingJobTests : IDisposable
{
    private const string UsdbTxt = "#ARTIST:Artist\n#TITLE:Title\n#MP3:old.mp3\n#BPM:300\n: 0 4 5 Hi\nE";

    /// <summary>Bash that finds the value following a given flag, e.g. <c>$(arg_after -o "$@")</c>.</summary>
    private const string ArgAfter = """
        arg_after() { local flag="$1"; shift; while [ $# -gt 0 ]; do if [ "$1" = "$flag" ]; then echo "$2"; return; fi; shift; done; }
        """;

    /// <summary>Writes an UltraStar song into the -o directory and reports it like UltraSinger does.</summary>
    private const string SuccessfulUltraSinger = ArgAfter + """

        out=$(arg_after -o "$@")
        echo "${MY_VAR:-}|${PYTHONUNBUFFERED:-}" > "$0.env"
        pwd > "$0.cwd"
        mkdir -p "$out/Artist - Title"
        printf '#ARTIST:Artist\n#TITLE:Title\n: 0 1 1 x\nE\n' > "$out/Artist - Title/Artist - Title.txt"
        echo "[UltraSinger] Downloading..."
        echo "untagged noise"
        echo "[UltraSinger] Parse ultrastar txt -> $out/Artist - Title/Artist - Title.txt"
        """;

    /// <summary>Creates yt.mp3 + yt.mp4 from the -o template, like --extract-audio --keep-video.</summary>
    private const string DownloadingYtDlp = ArgAfter + """

        template=$(arg_after -o "$@")
        base=$(printf '%s' "$template" | sed 's/\.%(ext)s$//')
        echo audio > "$base.mp3"
        echo video > "$base.mp4"
        echo "[download] 100%"
        """;

    /// <summary>Writes stems where demucs does: &lt;out&gt;/&lt;model&gt;/&lt;track&gt;/{vocals,no_vocals}.wav.</summary>
    private const string SeparatingDemucs = ArgAfter + """

        out=$(arg_after -o "$@")
        input="${@: -1}"
        track=$(basename "${input%.*}")
        mkdir -p "$out/htdemucs_ft/$track"
        echo vocals > "$out/htdemucs_ft/$track/vocals.wav"
        echo instrumental > "$out/htdemucs_ft/$track/no_vocals.wav"
        """;

    /// <summary>"Converts" by copying -i to the final argument.</summary>
    private const string CopyingFfmpeg = ArgAfter + """

        cp "$(arg_after -i "$@")" "${@: -1}"
        """;

    private readonly TestEnvironment _env = new();
    private readonly ISongStore _store = Substitute.For<ISongStore>();

    public void Dispose() => _env.Dispose();

    private SongProcessingJob CreateJob()
    {
        var values = _env.CreateValues();

        return new SongProcessingJob(
            _store,
            values,
            new SyncedLyricsService(values, NullLogger<SyncedLyricsService>.Instance),
            new OpenAIImproverService(values, NullLogger<OpenAIImproverService>.Instance),
            new VocalSeparationService(values, NullLogger<VocalSeparationService>.Instance),
            NullLogger<SongProcessingJob>.Instance);
    }

    private SongRecord AddSong(SongRecord song)
    {
        _store.Get(song.Id).Returns(song);
        return song;
    }

    private SongRecord YouTubeSong() => AddSong(new SongRecord
    {
        Id = Guid.NewGuid(),
        Url = "https://www.youtube.com/watch?v=abc123",
        Title = "Artist - Title",
    });

    /// <remarks>No USDB id, so no real cover download is attempted.</remarks>
    private SongRecord UsdbSong() => AddSong(new SongRecord
    {
        Id = Guid.NewGuid(),
        Url = "https://www.youtube.com/watch?v=abc123",
        Title = "Artist - Title",
        Source = SongSource.USDB,
        UltraStarTxt = UsdbTxt,
    });

    private static string[] BundleEntries(SongRecord song)
    {
        using var zip = ZipFile.OpenRead(song.BundlePath!);
        return zip.Entries.Select(e => e.FullName).Order().ToArray();
    }

    private static string ReadBundleEntry(SongRecord song, string entry)
    {
        using var zip = ZipFile.OpenRead(song.BundlePath!);
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open());
        return reader.ReadToEnd();
    }

    // ---- YouTube / UltraSinger ------------------------------------------------------------

    [Fact]
    public async Task YouTube_RunsUltraSingerAndBundlesItsOutput()
    {
        _env.Configuration = _env.Configuration with
        {
            UltraSingerAdditionalArgs = "--demucs htdemucs_ft --whisper large-v3",
            UltraSingerAdditionalEnvVars = "MY_VAR=hello",
        };
        var python = _env.Fake("python", SuccessfulUltraSinger);
        var song = YouTubeSong();

        await CreateJob().ProcessAsync(song.Id);

        Assert.Equal(SongState.COMPLETED, song.State);
        Assert.NotNull(song.BeganProcessingAt);
        Assert.NotNull(song.CompletedAt);
        Assert.Equal("Artist - Title/Artist - Title.txt", song.UltraStarTxtPath);

        Assert.Equal(Path.Combine(_env.BundleDirectory, $"{song.Id:N}.zip"), song.BundlePath);
        Assert.Equal(["Artist - Title/Artist - Title.txt"], BundleEntries(song));
        Assert.False(Directory.Exists(_env.JobDirectory(song.Id)), "the raw job directory should be removed after bundling");

        // Paths with spaces arrive as single arguments, with no shell escaping.
        Assert.Contains(' ', _env.JobDirectory(song.Id));
        Assert.Equal(
            [
                Path.Combine(_env.UltraSingerPath, "src", "UltraSinger.py"),
                "-i", song.Url,
                "-o", _env.JobDirectory(song.Id),
                "--language", "en",
                "--demucs", "htdemucs_ft", "--whisper", "large-v3",
            ],
            FakeTool.ReadArgs(python));

        Assert.Equal("hello|1", File.ReadAllText(python + ".env").Trim());
        Assert.Equal(Path.Combine(_env.UltraSingerPath, "src"), File.ReadAllText(python + ".cwd").Trim());
    }

    [Fact]
    public async Task YouTube_OnlyTaggedOutputReachesTheVisibleLog()
    {
        _env.Fake("python", SuccessfulUltraSinger);
        var song = YouTubeSong();

        await CreateJob().ProcessAsync(song.Id);

        var log = song.ReadLogFrom(0).Text;
        Assert.Contains("[UltraSinger] Downloading...", log);
        Assert.DoesNotContain("untagged noise", log);
        Assert.Contains("OpenAI corrections disabled", log);
    }

    [Fact]
    public async Task YouTube_CrashFailsTheJobWithThePythonTraceback()
    {
        _env.Fake("python", """
            echo "[UltraSinger] Pitching with SwiftF0"
            echo "Traceback (most recent call last):" >&2
            echo "  File \"pitcher.py\", line 18, in _get_detector" >&2
            echo "TypeError: SwiftF0.__init__() got an unexpected keyword argument 'fmin'" >&2
            exit 1
            """);
        var song = YouTubeSong();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => CreateJob().ProcessAsync(song.Id));

        Assert.Contains("exited with code 1", ex.Message);
        Assert.Contains("TypeError: SwiftF0.__init__()", ex.Message);
        Assert.Contains("TypeError: SwiftF0.__init__()", song.ErrorText);
        Assert.Equal(SongState.FAILED, song.State);
        Assert.NotNull(song.CompletedAt);
        Assert.Null(song.BundlePath);
    }

    [Fact]
    public async Task UnknownSong_IsSkippedQuietly()
    {
        var python = _env.Fake("python", SuccessfulUltraSinger);

        await CreateJob().ProcessAsync(Guid.NewGuid());

        Assert.False(FakeTool.WasInvoked(python));
    }

    // ---- USDB ------------------------------------------------------------------------------

    [Fact]
    public async Task Usdb_DownloadsSeparatesAndBundlesTitledFiles()
    {
        _env.Configuration = _env.Configuration with
        {
            EnableVocalSeparation = true,
            YTDLPCookiesFromBrowser = "firefox",
        };
        var ytDlp = _env.Fake("yt-dlp", DownloadingYtDlp);
        var demucs = _env.Fake("demucs", SeparatingDemucs);
        _env.Fake("ffmpeg", CopyingFfmpeg);
        var python = _env.Fake("python", "exit 1");
        var song = UsdbSong();

        await CreateJob().ProcessAsync(song.Id);

        Assert.Equal(SongState.COMPLETED, song.State);
        Assert.False(FakeTool.WasInvoked(python), "USDB songs already have notes; UltraSinger must not run");

        // The full mix is dropped once stems exist; the instrumental becomes #MP3.
        Assert.Equal(
            [
                "Artist - Title/Artist - Title [Instrumental].mp3",
                "Artist - Title/Artist - Title [Vocals].mp3",
                "Artist - Title/Artist - Title.mp4",
                "Artist - Title/Artist - Title.txt",
            ],
            BundleEntries(song));

        var txt = ReadBundleEntry(song, "Artist - Title/Artist - Title.txt");
        Assert.Contains("#MP3:Artist - Title [Instrumental].mp3", txt);
        Assert.Contains("#VIDEO:Artist - Title.mp4", txt);
        Assert.Contains("#VOCALS:Artist - Title [Vocals].mp3", txt);
        Assert.Contains("#INSTRUMENTAL:Artist - Title [Instrumental].mp3", txt);
        Assert.DoesNotContain("old.mp3", txt);
        Assert.Equal("instrumental", ReadBundleEntry(song, "Artist - Title/Artist - Title [Instrumental].mp3").Trim());

        var ytDlpArgs = FakeTool.ReadArgs(ytDlp);
        Assert.Equal(["--cookies-from-browser", "firefox", song.Url], ytDlpArgs[^3..]);

        var songDirectory = Path.Combine(_env.JobDirectory(song.Id), "song");
        Assert.Equal(
            ["-n", "htdemucs_ft", "--two-stems=vocals", "-o", songDirectory, Path.Combine(songDirectory, "yt.mp3")],
            FakeTool.ReadArgs(demucs));
    }

    [Fact]
    public async Task Usdb_WithoutCookiesConfigured_DoesNotPassTheFlag()
    {
        var ytDlp = _env.Fake("yt-dlp", DownloadingYtDlp);
        var song = UsdbSong();

        await CreateJob().ProcessAsync(song.Id);

        Assert.DoesNotContain("--cookies-from-browser", FakeTool.ReadArgs(ytDlp));
        Assert.Equal(song.Url, FakeTool.ReadArgs(ytDlp)[^1]);
    }

    [Fact]
    public async Task Usdb_WithSeparationOff_KeepsTheFullMix()
    {
        _env.Fake("yt-dlp", DownloadingYtDlp);
        var demucs = _env.Fake("demucs", SeparatingDemucs);
        var song = UsdbSong();

        await CreateJob().ProcessAsync(song.Id);

        Assert.False(FakeTool.WasInvoked(demucs));
        Assert.Equal(
            ["Artist - Title/Artist - Title.mp3", "Artist - Title/Artist - Title.mp4", "Artist - Title/Artist - Title.txt"],
            BundleEntries(song));
        Assert.Contains("#MP3:Artist - Title.mp3", ReadBundleEntry(song, "Artist - Title/Artist - Title.txt"));
    }

    [Fact]
    public async Task Usdb_FailedSeparationIsNotFatal()
    {
        _env.Configuration = _env.Configuration with { EnableVocalSeparation = true };
        _env.Fake("yt-dlp", DownloadingYtDlp);
        _env.Fake("demucs", "echo 'CUDA out of memory' >&2; exit 1");
        var song = UsdbSong();

        await CreateJob().ProcessAsync(song.Id);

        Assert.Equal(SongState.COMPLETED, song.State);
        Assert.Contains("Vocal separation failed", song.ReadLogFrom(0).Text);
        Assert.Contains("Artist - Title/Artist - Title.mp3", BundleEntries(song));
    }

    [Fact]
    public async Task Usdb_TranscodesNonH264Video()
    {
        _env.Fake("yt-dlp", DownloadingYtDlp);
        _env.Fake("ffprobe", "echo vp9");
        var ffmpeg = _env.Fake("ffmpeg", CopyingFfmpeg);
        var song = UsdbSong();

        await CreateJob().ProcessAsync(song.Id);

        Assert.Equal(SongState.COMPLETED, song.State);
        Assert.Contains("libx264", FakeTool.ReadArgs(ffmpeg));
        Assert.Contains("Artist - Title/Artist - Title.mp4", BundleEntries(song));
        Assert.Contains("Transcoded video to H.264", song.ReadLogFrom(0).Text);
    }

    [Fact]
    public async Task Usdb_DownloadFailureFailsTheJob()
    {
        _env.Fake("yt-dlp", "echo 'ERROR: Video unavailable' >&2; exit 1");
        var song = UsdbSong();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => CreateJob().ProcessAsync(song.Id));

        Assert.Contains("Video unavailable", ex.Message);
        Assert.Equal(SongState.FAILED, song.State);
    }

    [Fact]
    public async Task Usdb_TitleWithShellMetacharactersIsSafe()
    {
        _env.Fake("yt-dlp", DownloadingYtDlp);
        var song = AddSong(new SongRecord
        {
            Id = Guid.NewGuid(),
            Url = "https://www.youtube.com/watch?v=abc123",
            Title = "Rock & Roll (Live) \"$HOME\" 'quoted'",
            Source = SongSource.USDB,
            UltraStarTxt = UsdbTxt,
        });

        await CreateJob().ProcessAsync(song.Id);

        Assert.Equal(SongState.COMPLETED, song.State);
        Assert.Contains("Rock & Roll (Live) \"$HOME\" 'quoted'/Rock & Roll (Live) \"$HOME\" 'quoted'.txt", BundleEntries(song));
    }
}
