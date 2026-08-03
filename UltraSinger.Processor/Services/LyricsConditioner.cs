using System.Text;
using System.Text.RegularExpressions;

namespace UltraSinger.Processor.Services;

/// <summary>
/// Cleans raw LRC output from syncedlyrics before it is shown to the model.
///
/// Providers pad their results with metadata headers, credit lines, blank spacers and
/// instrumental markers. None of that is lyrics, and all of it is material the model might
/// otherwise copy into the UltraStar file. Pure functions, no I/O, so this is trivially
/// testable in isolation.
/// </summary>
public static partial class LyricsConditioner
{
    /// <summary>Line timestamp, e.g. <c>[01:23.45]</c> or <c>[01:23.456]</c>.</summary>
    [GeneratedRegex(@"^\s*\[(?<mm>\d{1,3}):(?<ss>\d{2})(?:[.:](?<frac>\d{1,3}))?\]", RegexOptions.Compiled)]
    private static partial Regex TimestampRegex();

    /// <summary>LRC ID tags such as <c>[ar:Artist]</c> — metadata, never lyrics.</summary>
    [GeneratedRegex(@"^\s*\[(ar|ti|al|au|by|offset|length|re|ve|tool|#)\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex IdTagRegex();

    /// <summary>Word-level timings from <c>--enhanced</c>, e.g. <c>&lt;00:12.34&gt;</c>.</summary>
    [GeneratedRegex(@"<\d{1,3}:\d{2}(?:[.:]\d{1,3})?>", RegexOptions.Compiled)]
    private static partial Regex WordTimingRegex();

    private static readonly string[] InstrumentalMarkers =
    [
        "[instrumental]", "(instrumental)", "[interlude]", "(interlude)",
        "[music]", "(music)", "♪", "♫"
    ];

    /// <summary>True when the text carries <c>[mm:ss.xx]</c> line timestamps.</summary>
    public static bool IsSynced(string lyrics) =>
        lyrics.Split('\n').Any(line => TimestampRegex().IsMatch(line));

    /// <summary>
    /// Conditions raw provider output.
    /// </summary>
    /// <param name="lyrics">Raw stdout from syncedlyrics.</param>
    /// <param name="keepTimestamps">
    /// Keep <c>[mm:ss.xx]</c> tags. They let the model align reference lines to UltraStar
    /// lines; strip them if that ever proves counterproductive.
    /// </param>
    public static string Condition(string lyrics, bool keepTimestamps)
    {
        if (string.IsNullOrWhiteSpace(lyrics))
        {
            return string.Empty;
        }

        var output = new List<string>();
        string? previousBody = null;

        foreach (var rawLine in lyrics.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();

            if (IdTagRegex().IsMatch(line))
            {
                continue;
            }

            // Enhanced mode interleaves word timings into the text; they add nothing for
            // lyric correction and make lines hard to read.
            line = WordTimingRegex().Replace(line, string.Empty);

            var match = TimestampRegex().Match(line);
            var timestamp = match.Success ? match.Value.Trim() : null;
            var body = (match.Success ? line[match.Length..] : line).Trim();

            body = NormaliseText(body);

            if (body.Length == 0 || IsInstrumentalMarker(body))
            {
                continue;
            }

            // Providers frequently repeat a line at consecutive timestamps.
            if (previousBody is not null && string.Equals(previousBody, body, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            previousBody = body;

            output.Add(keepTimestamps && timestamp is not null
                ? $"{timestamp} {body}"
                : body);
        }

        return string.Join('\n', output);
    }

    private static bool IsInstrumentalMarker(string body)
    {
        var lowered = body.ToLowerInvariant();
        return InstrumentalMarkers.Any(marker => lowered == marker || lowered.Trim('[', ']', '(', ')') == marker.Trim('[', ']', '(', ')'));
    }

    /// <summary>
    /// Folds the typographic characters providers like to use into plain ASCII equivalents,
    /// so they don't end up in the UltraStar file where they render inconsistently.
    /// </summary>
    private static string NormaliseText(string input)
    {
        var builder = new StringBuilder(input.Length);

        foreach (var character in input)
        {
            builder.Append(character switch
            {
                '‘' or '’' or 'ʼ' => '\'',
                '“' or '”' => '"',
                '–' or '—' => '-',
                ' ' => ' ',
                _ => character
            });
        }

        // Collapse runs of whitespace introduced by stripping word timings.
        return Regex.Replace(builder.ToString(), @"\s{2,}", " ").Trim();
    }
}
