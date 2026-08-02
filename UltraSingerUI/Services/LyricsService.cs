using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UltraSingerUI.Services;

public static class LyricsService
{
    private static readonly HttpClient Http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static LyricsService()
    {
        try
        {
            // Set a simple User-Agent to avoid being blocked by some providers
            if (!Http.DefaultRequestHeaders.UserAgent.Any())
            {
                Http.DefaultRequestHeaders.UserAgent.ParseAdd("UltraSingerUI/1.0");
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Attempts to fetch lyrics for a given song title using configured providers.
    /// Order:
    /// 1) Some-Random-API (requires token configured)
    /// 2) lyrics.ovh (fallback; anonymous)
    /// If the input contains an artist in the form "Artist - Title", providers will be queried with both.
    /// Returns null if nothing is found or any error occurs on all providers.
    /// </summary>
    public static async Task<string?> TryGetLyricsAsync(string title)
    {
        try
        {
            // Clean common cruft like "(Official Video)" from titles first
            var cleanedTitle = CleanTitle(title);

            // Try to parse "Artist - Title"
            var (artist, songTitle) = ParseTitle(cleanedTitle);
            // 1) Some-Random-API (title or "artist - title")
            var srTitle = !string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(songTitle)
                ? $"{artist} - {songTitle}"
                : cleanedTitle;
            var someRandom = await TrySomeRandomApiAsync(srTitle);
            if (!string.IsNullOrWhiteSpace(someRandom)) return someRandom;

            // 2) lyrics.ovh fallback (artist + title only)
            if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(songTitle))
            {
                var ovh = await TryLyricsOvhAsync(artist!, songTitle!);
                if (!string.IsNullOrWhiteSpace(ovh)) return ovh;
            }
        }
        catch
        {
            // ignored – return null below
        }

        return null;
    }

    private static string CleanTitle(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        // Remove any parenthetical segments like "(Official Video)" including surrounding spaces
        var withoutParens = Regex.Replace(input, "\\s*\\([^)]*\\)\\s*", " ");
        // Collapse excess whitespace and trim
        var normalized = Regex.Replace(withoutParens, "\\s{2,}", " ").Trim();
        return normalized;
    }

    private static (string? artist, string? songTitle) ParseTitle(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return (null, null);

        // Common format: "Artist - Song Title"
        var parts = input.Split('-', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            return (parts[0], parts[1]);
        }

        return (null, input.Trim());
    }

    private static async Task<string?> TrySomeRandomApiAsync(string title)
    {
        try
        {
            Console.WriteLine($"Trying Some-Random-API for lyrics of {title}");
            // Requires API token
            var env = new EnvironmentalValuesService();
            var token = env.SomeRandomApiToken;
            if (string.IsNullOrWhiteSpace(token)) return null;

            var url = $"https://some-random-api.com/lyrics?title={Uri.EscapeDataString(title)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            // Most Some-Random-API endpoints accept X-API-Key header for auth
            req.Headers.TryAddWithoutValidation("Authorization", token);
            using var resp = await Http.SendAsync(req);
            Console.WriteLine($"Some-Random-API returned {resp.StatusCode}");
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("lyrics", out var prop))
            {
                var lyrics = prop.GetString();
                return string.IsNullOrWhiteSpace(lyrics) ? null : lyrics;
            }
        }
        catch { }
        return null;
    }

    private static async Task<string?> TryLyricsOvhAsync(string artist, string title)
    {
        try
        {
            var url = $"https://api.lyrics.ovh/v1/{Uri.EscapeDataString(artist)}/{Uri.EscapeDataString(title)}";
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("lyrics", out var prop))
            {
                var lyrics = prop.GetString();
                return string.IsNullOrWhiteSpace(lyrics) ? null : lyrics;
            }
        }
        catch { }
        return null;
    }
}
