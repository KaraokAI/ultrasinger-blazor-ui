namespace UltraSinger.Processor.Tests.Infrastructure;

/// <summary>
/// Writes an executable bash script standing in for an external tool (UltraSinger's
/// python, yt-dlp, demucs, ffmpeg...). The processor launches these exactly as it would the
/// real thing, so argument passing, output parsing and exit-code handling are all exercised
/// without a GPU or network.
///
/// Every script records its arguments, one per line, to <c>&lt;name&gt;.args</c> beside it.
/// </summary>
public static class FakeTool
{
    public static string Create(string directory, string name, string body)
    {
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, name);
        var argsFile = ArgsFile(path);

        File.WriteAllText(path,
            $"""
             #!/usr/bin/env bash
             set -euo pipefail
             printf '%s\n' "$@" > '{argsFile}'
             {body}

             """);

        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The fake tools are bash scripts; run these tests on Linux.");
        }

        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return path;
    }

    public static string ArgsFile(string toolPath) => toolPath + ".args";

    /// <summary>The arguments the tool was last invoked with, exactly as received.</summary>
    public static string[] ReadArgs(string toolPath) =>
        File.Exists(ArgsFile(toolPath))
            ? File.ReadAllLines(ArgsFile(toolPath))
            : [];

    public static bool WasInvoked(string toolPath) => File.Exists(ArgsFile(toolPath));
}
