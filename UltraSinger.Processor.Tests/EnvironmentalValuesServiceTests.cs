using UltraSinger.Processor.Configuration;
using UltraSinger.Processor.Tests.Infrastructure;

namespace UltraSinger.Processor.Tests;

public sealed class EnvironmentalValuesServiceTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    public void Validate_PassesForACompleteSandbox()
    {
        Assert.Empty(_env.CreateValues().Validate());
    }

    [Fact]
    public void ToolPaths_DefaultToTheVenvRelativeToTheContentRoot()
    {
        _env.Configuration = new UltraSingerConfiguration { UltraStarDeluxeLocalLibraryPath = _env.LibraryPath };

        var values = _env.CreateValues(contentRoot: "/srv/processor");

        Assert.Equal("/srv/processor/python/.venv", values.PythonEnvironmentPath);
        Assert.Equal("/srv/processor/python/.venv/bin/python", values.PythonExecutable);
        Assert.Equal("/srv/processor/python/.venv/bin/yt-dlp", values.YTDLPPath);
        Assert.Equal("/srv/processor/python/.venv/bin/demucs", values.VocalSeparationExecutable);
        Assert.Equal("/srv/processor/python/.venv/bin/syncedlyrics", values.SyncedLyricsPath);
        Assert.Equal("/srv/processor/UltraSinger/src/UltraSinger.py", values.UltraSingerScriptPath);
        Assert.Equal("ffmpeg", values.FfmpegPath);
        Assert.Equal("/srv/processor/bundles", values.BundleStoragePath);
    }

    [Fact]
    public void ToolPaths_FollowACustomVenv_RelativePathsResolveAndBareNamesUsePath()
    {
        _env.Configuration = new UltraSingerConfiguration
        {
            PythonEnvironmentPath = "/opt/venv",
            YTDLPPath = "yt-dlp",
            VocalSeparationExecutable = "tools/demucs",
        };

        var values = _env.CreateValues(contentRoot: "/srv/processor");

        Assert.Equal("/opt/venv/bin/python", values.PythonExecutable);
        Assert.Equal("yt-dlp", values.YTDLPPath);
        Assert.Equal("/srv/processor/tools/demucs", values.VocalSeparationExecutable);
    }

    [Fact]
    public void AdditionalArgs_SplitOnAnyWhitespace()
    {
        _env.Configuration = _env.Configuration with
        {
            UltraSingerAdditionalArgs = "  --demucs htdemucs_ft\t--whisper   large-v3 ",
            VocalSeparationAdditionalArgs = "",
        };

        var values = _env.CreateValues();

        Assert.Equal(["--demucs", "htdemucs_ft", "--whisper", "large-v3"], values.UltraSingerAdditionalArgs);
        Assert.Empty(values.VocalSeparationAdditionalArgs);
    }

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    [InlineData("A=1;B=two", new[] { "A=1", "B=two" })]
    [InlineData(" A = 1 ; ;B=;", new[] { "A=1", "B=" })]
    [InlineData("OPTS=--x=1 --y=2", new[] { "OPTS=--x=1 --y=2" })]
    public void AdditionalEnvironmentVariables_IgnoreBlankSegments(string? setting, string[] expected)
    {
        _env.Configuration = _env.Configuration with { UltraSingerAdditionalEnvVars = setting };

        var parsed = _env.CreateValues().GetUltraSingerAdditionalEnvironmentVariables()
            .Select(x => $"{x.Item1}={x.Item2}");

        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void Validate_ReportsMissingToolsWithAHint()
    {
        _env.Configuration = _env.Configuration with
        {
            PythonExecutable = _env.Root.Combine("missing", "python"),
            YTDLPPath = "definitely-not-a-real-command",
        };

        var problems = _env.CreateValues().Validate();

        Assert.Contains(problems, p => p.StartsWith("PythonExecutable not found") && p.Contains("uv sync"));
        Assert.Contains(problems, p => p.StartsWith("YTDLPPath not found"));
    }

    [Fact]
    public void Validate_ReportsMissingLibraryAndUltraSingerCheckout()
    {
        _env.Configuration = _env.Configuration with
        {
            UltraStarDeluxeLocalLibraryPath = _env.Root.Combine("nope"),
            UltraSingerPath = _env.Root.Combine("no-submodule"),
        };

        var problems = _env.CreateValues().Validate();

        Assert.Contains(problems, p => p.StartsWith("UltraStar library path does not exist"));
        Assert.Contains(problems, p => p.Contains("git submodule update --init"));
    }

    [Fact]
    public void Validate_OnlyChecksOptionalToolsWhenTheirFeatureIsOn()
    {
        _env.Configuration = _env.Configuration with
        {
            VocalSeparationExecutable = _env.Root.Combine("missing-demucs"),
            SyncedLyricsPath = _env.Root.Combine("missing-syncedlyrics"),
            EnableVocalSeparation = false,
            EnableOpenAICorrections = false,
        };
        Assert.Empty(_env.CreateValues().Validate());

        _env.Configuration = _env.Configuration with { EnableVocalSeparation = true, EnableOpenAICorrections = true };
        var problems = _env.CreateValues().Validate();

        Assert.Contains(problems, p => p.StartsWith("VocalSeparationExecutable not found"));
        Assert.Contains(problems, p => p.StartsWith("SyncedLyricsPath not found"));
        Assert.Contains(problems, p => p.Contains("OpenAIKey is not set"));
    }
}
