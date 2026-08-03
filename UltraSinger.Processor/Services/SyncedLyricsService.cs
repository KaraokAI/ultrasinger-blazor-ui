using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using UltraSinger.Processor.Configuration;

namespace UltraSinger.Processor.Services;

/// <param name="Text">Conditioned lyrics, possibly carrying <c>[mm:ss.xx]</c> tags.</param>
/// <param name="IsSynced">Whether the provider returned time-synced lyrics.</param>
public record LyricsResult(string Text, bool IsSynced);

/// <summary>
/// Fetches lyrics via the <c>syncedlyrics</c> Python CLI.
///
/// Three behaviours of that tool shape this code, all confirmed by running it:
/// <list type="bullet">
/// <item>It writes the lyrics to stdout *and* to the <c>-o</c> file.</item>
/// <item>It exits <c>0</c> even when nothing is found, so the exit code tells us nothing —
/// success is "stdout was not empty".</item>
/// <item>Without <c>-o</c> it drops a <c>{search_term}.lrc</c> file in the working
/// directory, so <c>-o</c> is always passed to a temp path we then delete.</item>
/// </list>
/// </summary>
public partial class SyncedLyricsService(
    EnvironmentalValuesService environmentalValues,
    ILogger<SyncedLyricsService> logger)
{
    [GeneratedRegex(@"\s*\([^)]*\)\s*", RegexOptions.Compiled)]
    private static partial Regex ParentheticalRegex();

    [GeneratedRegex(@"\s*\[[^\]]*\]\s*", RegexOptions.Compiled)]
    private static partial Regex BracketedRegex();

    public async Task<LyricsResult?> TryGetLyricsAsync(string title, CancellationToken cancellationToken = default)
    {
        var executable = environmentalValues.SyncedLyricsPath;

        if (string.IsNullOrWhiteSpace(executable))
        {
            logger.LogWarning("ProcessorOptions:SyncedLyricsPath is not configured; skipping lyric lookup.");
            return null;
        }

        var query = BuildQuery(title);

        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        // Only written so the CLI doesn't default to dumping a .lrc into our working
        // directory. The content we actually use comes from stdout.
        var scratchFile = Path.Combine(Path.GetTempPath(), $"ultrasinger-lyrics-{Guid.NewGuid():N}.lrc");

        try
        {
            var stdout = await RunAsync(executable, BuildArguments(query, scratchFile), cancellationToken);

            if (string.IsNullOrWhiteSpace(stdout))
            {
                logger.LogInformation("No lyrics found for '{Query}'.", query);
                return null;
            }

            var isSynced = LyricsConditioner.IsSynced(stdout);
            var conditioned = LyricsConditioner.Condition(
                stdout,
                keepTimestamps: isSynced && environmentalValues.SendTimestampsToOpenAI);

            if (string.IsNullOrWhiteSpace(conditioned))
            {
                logger.LogInformation("Lyrics for '{Query}' were empty after conditioning.", query);
                return null;
            }

            logger.LogInformation(
                "Found {Kind} lyrics for '{Query}' ({Lines} lines).",
                isSynced ? "synced" : "plain", query, conditioned.Split('\n').Length);

            return new LyricsResult(conditioned, isSynced);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lyric lookup failed for '{Query}'.", query);
            return null;
        }
        finally
        {
            TryDelete(scratchFile);
        }
    }

    private List<string> BuildArguments(string query, string scratchFile)
    {
        var arguments = new List<string> { query, "-o", scratchFile };

        switch (environmentalValues.SyncedLyricsMode)
        {
            case SyncedLyricsMode.SyncedOnly:
                arguments.Add("--synced-only");
                break;
            case SyncedLyricsMode.PlainOnly:
                arguments.Add("--plain-only");
                break;
            case SyncedLyricsMode.PreferSynced:
            default:
                // The CLI already prefers synced and falls back to plain.
                break;
        }

        if (environmentalValues.SyncedLyricsEnhanced && environmentalValues.SyncedLyricsMode != SyncedLyricsMode.PlainOnly)
        {
            arguments.Add("--enhanced");
        }

        if (!string.IsNullOrWhiteSpace(environmentalValues.SyncedLyricsLanguage))
        {
            arguments.Add("-l");
            arguments.Add(environmentalValues.SyncedLyricsLanguage);
        }

        var providers = environmentalValues.SyncedLyricsProviders;

        if (!string.IsNullOrWhiteSpace(providers))
        {
            arguments.Add("-p");
            arguments.AddRange(providers.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return arguments;
    }

    private async Task<string> RunAsync(string executable, List<string> arguments, CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Providers can return non-ASCII lyrics; without this they arrive mangled.
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetTempPath()
            }
        };

        // ArgumentList quotes each value for us — song titles routinely contain spaces and
        // quotes, and building one command-line string by hand gets that wrong.
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null) stdout.AppendLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null) stderr.AppendLine(args.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(environmentalValues.SyncedLyricsTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // There is a single worker on this queue — a provider hanging must not wedge it.
            logger.LogWarning(
                "syncedlyrics exceeded {Timeout}; killing it.",
                environmentalValues.SyncedLyricsTimeout);

            TryKill(process);
            return string.Empty;
        }

        if (stderr.Length > 0)
        {
            logger.LogDebug("syncedlyrics stderr: {Stderr}", stderr.ToString().Trim());
        }

        return stdout.ToString();
    }

    /// <summary>
    /// Turns a YouTube video title into a search term. The CLI documents a preference for
    /// "[TRACK] [ARTIST]", so an "Artist - Title" heading is flipped around.
    /// </summary>
    internal static string BuildQuery(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        // Drop "(Official Video)", "[HD]" and friends — they only confuse providers.
        var cleaned = BracketedRegex().Replace(ParentheticalRegex().Replace(title, " "), " ");
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();

        var parts = cleaned.Split('-', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 2
            ? $"{parts[1]} {parts[0]}"
            : cleaned;
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not kill the syncedlyrics process.");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not delete scratch lyric file {Path}", path);
        }
    }
}
