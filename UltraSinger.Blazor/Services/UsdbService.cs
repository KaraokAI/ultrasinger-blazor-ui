using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using UltraSinger.Blazor.Configuration;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Services;

public class UsdbService
{
    private class UsdbTableIndices
    {
        public const int 
            ARTIST = 0,
            TITLE = 1,
            GENRE = 2,
            YEAR = 3,
            EDITION = 4,
            GOLDEN_NOTES = 5,
            LANGUAGE = 6,
            CREATOR = 7,
            RATING = 8,
            VIEWS = 9;
    }
    
    private readonly UsdbConfiguration _configuration;
    private readonly ILogger<UsdbService> _logger;
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private bool _isLoggedIn;
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    private static readonly Regex YouTubeUrlRegex = new(
        @"(?:https?:\/\/)?(?:www\.)?(?:youtube\.com\/(?:watch\?v=|embed\/|v\/|shorts\/)|youtu\.be\/)([a-zA-Z0-9_-]{11})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public UsdbService(IOptions<UsdbConfiguration> options, ILogger<UsdbService> logger)
    {
        _configuration = options.Value;
        _logger = logger;
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_configuration.BaseUrl.TrimEnd('/') + "/")
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "UltraSingerUI/1.0 (USDB Client)");
    }

    public async Task<bool> EnsureAuthenticatedAsync()
    {
        if (string.IsNullOrWhiteSpace(_configuration.Username) || string.IsNullOrWhiteSpace(_configuration.Password))
        {
            return false;
        }

        if (_isLoggedIn)
        {
            return true;
        }

        await _loginLock.WaitAsync();
        try
        {
            if (_isLoggedIn)
            {
                return true;
            }

            var form = new Dictionary<string, string>
            {
                ["user"] = _configuration.Username,
                ["pass"] = _configuration.Password,
                ["login"] = "Login"
            };

            using var content = new FormUrlEncodedContent(form);
            var response = await _httpClient.PostAsync("?link=login", content);
            var html = await response.Content.ReadAsStringAsync();

            if (html.Contains("logout") || html.Contains("link=logout"))
            {
                _isLoggedIn = true;
                _logger.LogInformation("Successfully logged into USDB as {User}", _configuration.Username);
                return true;
            }

            _logger.LogWarning("USDB login failed for user {User}", _configuration.Username);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while attempting USDB login");
            return false;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    public async Task<List<UsdbSongResult>> SearchAsync(string query, int limit = 30)
    {
        var results = new List<UsdbSongResult>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        await EnsureAuthenticatedAsync();

        string artistQuery = string.Empty;
        string titleQuery = string.Empty;

        if (query.Contains(" - "))
        {
            var parts = query.Split(new[] { " - " }, 2, StringSplitOptions.TrimEntries);
            artistQuery = parts[0];
            titleQuery = parts.Length > 1 ? parts[1] : string.Empty;
        }
        else if (query.Contains(':'))
        {
            var parts = query.Split(new[] { ':' }, 2, StringSplitOptions.TrimEntries);
            artistQuery = parts[0];
            titleQuery = parts.Length > 1 ? parts[1] : string.Empty;
        }
        else
        {
            // Search artist or title
            titleQuery = query.Trim();
        }

        try
        {
            // Search by POST or GET against ?link=list
            var form = new Dictionary<string, string>
            {
                ["interpret"] = artistQuery,
                ["title"] = titleQuery,
                ["order"] = "views",
                ["ud"] = "desc",
                ["limit"] = limit.ToString()
            };

            using var content = new FormUrlEncodedContent(form);
            var response = await _httpClient.PostAsync("?link=list", content);
            var html = await response.Content.ReadAsStringAsync();

            results = ParseSearchResultsHtml(html);

            // If empty and we searched with titleQuery only, also try searching interpret
            if (results.Count == 0 && string.IsNullOrWhiteSpace(artistQuery) && !string.IsNullOrWhiteSpace(titleQuery))
            {
                var altForm = new Dictionary<string, string>
                {
                    ["interpret"] = titleQuery,
                    ["title"] = string.Empty,
                    ["order"] = "views",
                    ["ud"] = "desc",
                    ["limit"] = limit.ToString()
                };
                using var altContent = new FormUrlEncodedContent(altForm);
                var altResponse = await _httpClient.PostAsync("?link=list", altContent);
                var altHtml = await altResponse.Content.ReadAsStringAsync();
                results = ParseSearchResultsHtml(altHtml);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search USDB for query '{Query}'", query);
        }

        return results;
    }

    public static List<UsdbSongResult> ParseSearchResultsHtml(string html)
    {
        var list = new List<UsdbSongResult>();
        if (string.IsNullOrWhiteSpace(html))
        {
            return list;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class, 'list')]")
                    ?? doc.DocumentNode.SelectSingleNode("//table");

        if (table == null)
        {
            return list;
        }

        var rows = table.SelectNodes(".//tr");
        if (rows == null)
        {
            return list;
        }

        foreach (var row in rows)
        {
            var cells = row.SelectNodes(".//td");
            if (cells == null || cells.Count <= UsdbTableIndices.TITLE)
            {
                continue;
            }

            var linkNode = cells[UsdbTableIndices.TITLE].SelectSingleNode(".//a") ?? row.SelectSingleNode(".//a[contains(@href, 'id=')]");
            if (linkNode == null)
            {
                continue;
            }

            var href = linkNode.GetAttributeValue("href", "");
            var idMatch = Regex.Match(href, @"id=(\d+)");
            if (!idMatch.Success || !int.TryParse(idMatch.Groups[1].Value, out var songId))
            {
                continue;
            }

            string? GetUsdbResultTableValue(HtmlNodeCollection collection, int index)
            {
                return index < collection.Count ? HtmlEntity.DeEntitize(collection[index].InnerText).Trim() : null;
            }

            string artist = GetUsdbResultTableValue(cells, UsdbTableIndices.ARTIST) ?? string.Empty;
            string title = GetUsdbResultTableValue(cells, UsdbTableIndices.TITLE) ?? string.Empty;

            string? edition = null;
            bool? goldenNotes = null;
            string? language = null;
            int? year = null;
            string? genre = null;
            double rating = 0;
            int views = 0;

            if (cells.Count > UsdbTableIndices.EDITION)
            {
                edition = GetUsdbResultTableValue(cells, UsdbTableIndices.EDITION);
            }

            if (cells.Count > UsdbTableIndices.GOLDEN_NOTES)
            {
                var goldenCell = cells[UsdbTableIndices.GOLDEN_NOTES];
                var hasGoldImg = goldenCell.SelectSingleNode(".//img[contains(@src, 'gold')]") != null;
                var hasGoldenNotes = GetUsdbResultTableValue(cells, UsdbTableIndices.GOLDEN_NOTES);
                goldenNotes = hasGoldImg || (hasGoldenNotes?.Contains("yes", StringComparison.OrdinalIgnoreCase) == true) || (hasGoldenNotes?.Contains("ja", StringComparison.OrdinalIgnoreCase) == true);
            }

            if (cells.Count > UsdbTableIndices.LANGUAGE)
            {
                language = GetUsdbResultTableValue(cells, UsdbTableIndices.LANGUAGE);
            }

            if (cells.Count > UsdbTableIndices.YEAR)
            {
                var yearStr = GetUsdbResultTableValue(cells, UsdbTableIndices.YEAR);
                if (int.TryParse(yearStr, out var y))
                {
                    year = y;
                }
            }

            if (cells.Count > UsdbTableIndices.GENRE)
            {
                genre = GetUsdbResultTableValue(cells, UsdbTableIndices.GENRE);
            }

            if (cells.Count > UsdbTableIndices.RATING)
            {
                // Rating stars or text
                var ratingText = GetUsdbResultTableValue(cells, UsdbTableIndices.RATING);
                if (double.TryParse(ratingText, System.Globalization.CultureInfo.InvariantCulture, out var r))
                {
                    rating = r;
                }
                else
                {
                    var starCount = cells[UsdbTableIndices.RATING].SelectNodes(".//img[contains(@src, 'star')]")?.Count ?? 0;
                    if (starCount > 0)
                    {
                        rating = starCount;
                    }
                }
            }

            if (cells.Count > UsdbTableIndices.VIEWS)
            {
                var viewStr = GetUsdbResultTableValue(cells, UsdbTableIndices.VIEWS)?.Replace(",", "").Replace(".", "");
                if (int.TryParse(viewStr, out var v))
                {
                    views = v;
                }
            }

            list.Add(new UsdbSongResult
            {
                Id = songId,
                Artist = artist,
                Title = title,
                Edition = string.IsNullOrWhiteSpace(edition) ? null : edition,
                GoldenNotes = goldenNotes,
                Language = string.IsNullOrWhiteSpace(language) ? null : language,
                Year = year,
                Genre = string.IsNullOrWhiteSpace(genre) ? null : genre,
                Rating = rating,
                Views = views
            });
        }

        return list;
    }

    public async Task<UsdbSongDetails?> GetSongDetailsAsync(int songId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var detailResponse = await _httpClient.GetAsync($"?link=detail&id={songId}");
            var detailHtml = await detailResponse.Content.ReadAsStringAsync();

            var details = ParseSongDetailsHtml(detailHtml, songId);

            // Fetch txt file
            var txtContent = await TryFetchUltraStarTxtAsync(songId);
            if (!string.IsNullOrWhiteSpace(txtContent))
            {
                details.UltraStarTxt = txtContent;
            }
            else
            {
                // Fallback to player.usdb.eu JSON if direct txt isn't available
                var fallbackJson = await TryFetchPlayerUsdbJsonAsync(songId);
                if (fallbackJson != null)
                {
                    if (string.IsNullOrWhiteSpace(details.YoutubeUrl) && !string.IsNullOrWhiteSpace(fallbackJson.Value.YoutubeId))
                    {
                        details.YoutubeUrl = $"https://www.youtube.com/watch?v={fallbackJson.Value.YoutubeId}";
                    }
                    if (string.IsNullOrWhiteSpace(details.UltraStarTxt) && !string.IsNullOrWhiteSpace(fallbackJson.Value.UltraStarTxt))
                    {
                        details.UltraStarTxt = fallbackJson.Value.UltraStarTxt;
                    }
                }
            }

            return details;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get USDB song details for id {SongId}", songId);
            return null;
        }
    }

    public static UsdbSongDetails ParseSongDetailsHtml(string html, int songId)
    {
        var details = new UsdbSongDetails { Id = songId };
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Find YouTube links
        var ytMatch = YouTubeUrlRegex.Match(html);
        if (ytMatch.Success)
        {
            var videoId = ytMatch.Groups[1].Value;
            details.YoutubeUrl = $"https://www.youtube.com/watch?v={videoId}";
        }

        // Extract metadata from detail tables
        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables != null)
        {
            foreach (var table in tables)
            {
                var rows = table.SelectNodes(".//tr");
                if (rows == null) continue;

                foreach (var row in rows)
                {
                    var cells = row.SelectNodes(".//td|.//th");
                    if (cells == null || cells.Count < 2) continue;

                    var key = HtmlEntity.DeEntitize(cells[0].InnerText).Trim().ToLowerInvariant();
                    var val = HtmlEntity.DeEntitize(cells[1].InnerText).Trim();

                    if (key.Contains("artist") || key.Contains("interpret"))
                    {
                        details.Artist = val;
                    }
                    else if (key.Contains("title") || key.Contains("titel"))
                    {
                        details.Title = val;
                    }
                    else if (key.Contains("bpm"))
                    {
                        if (double.TryParse(val, System.Globalization.CultureInfo.InvariantCulture, out var bpm))
                        {
                            details.Bpm = bpm;
                        }
                    }
                    else if (key.Contains("gap"))
                    {
                        if (double.TryParse(val, System.Globalization.CultureInfo.InvariantCulture, out var gap))
                        {
                            details.Gap = gap;
                        }
                    }
                    else if (key.Contains("year") || key.Contains("jahr"))
                    {
                        if (int.TryParse(val, out var year))
                        {
                            details.Year = year;
                        }
                    }
                    else if (key.Contains("genre"))
                    {
                        details.Genre = string.IsNullOrWhiteSpace(val) ? null : val;
                    }
                    else if (key.Contains("language") || key.Contains("sprache"))
                    {
                        details.Language = string.IsNullOrWhiteSpace(val) ? null : val;
                    }
                    else if (key.Contains("video") || key.Contains("youtube") || key.Contains("link"))
                    {
                        var cellYt = YouTubeUrlRegex.Match(cells[1].InnerHtml);
                        if (cellYt.Success)
                        {
                            details.YoutubeUrl = $"https://www.youtube.com/watch?v={cellYt.Groups[1].Value}";
                        }
                    }
                }
            }
        }

        // USDB's detail page doesn't label Artist/Title with a key - they're the two
        // bare <td>s in the "list_head" row that heads the metadata table.
        if (string.IsNullOrWhiteSpace(details.Artist) || string.IsNullOrWhiteSpace(details.Title))
        {
            var headRow = doc.DocumentNode.SelectNodes("//tr[@class='list_head']")
                ?.FirstOrDefault(r => r.SelectNodes(".//td")?.Count == 2);
            if (headRow != null)
            {
                var headCells = headRow.SelectNodes(".//td");
                if (string.IsNullOrWhiteSpace(details.Artist))
                {
                    details.Artist = HtmlEntity.DeEntitize(headCells[0].InnerText).Trim();
                }
                if (string.IsNullOrWhiteSpace(details.Title))
                {
                    details.Title = HtmlEntity.DeEntitize(headCells[1].InnerText).Trim();
                }
            }
        }

        return details;
    }

    public async Task<string?> TryFetchUltraStarTxtAsync(int songId)
    {
        try
        {
            var form = new Dictionary<string, string>
            {
                ["wd"] = "1"
            };
            using var content = new FormUrlEncodedContent(form);
            var response = await _httpClient.PostAsync($"?link=gettxt&id={songId}", content);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync();
            return ExtractUltraStarTxtFromGettxtHtml(html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch USDB txt file for song {SongId}", songId);
            return null;
        }
    }

    // ?link=gettxt doesn't return a raw txt file - it returns an HTML page with the
    // actual content embedded in a <textarea name="txt"> (mirrored in a hidden <input>
    // used by the page's own download button). The 30s wait USDB mentions gates that
    // button client-side only; the full content is already present in this response.
    private static string? ExtractUltraStarTxtFromGettxtHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var textarea = doc.DocumentNode.SelectSingleNode("//textarea[@name='txt']");
        string raw;
        if (textarea != null)
        {
            raw = HtmlEntity.DeEntitize(textarea.InnerText);
        }
        else
        {
            var hiddenValue = doc.DocumentNode.SelectSingleNode("//input[@name='txt']")?.Attributes["value"]?.Value;
            raw = hiddenValue != null ? HtmlEntity.DeEntitize(hiddenValue) : html;
        }

        return raw.Contains("#TITLE:") && raw.Contains("#ARTIST:") ? raw.Trim() : null;
    }

    private async Task<(string? YoutubeId, string? UltraStarTxt)?> TryFetchPlayerUsdbJsonAsync(int songId)
    {
        try
        {
            using var playerClient = new HttpClient();
            var url = $"https://player.usdb.eu/2.0.0/json.php?songFile={songId}";
            var jsonString = await playerClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            string? ytId = null;
            if (root.TryGetProperty("youtube", out var ytProp))
            {
                ytId = ytProp.GetString();
            }

            // Synthesize UltraStar txt if notes exist
            var sb = new StringBuilder();
            if (root.TryGetProperty("artist", out var artistProp))
            {
                sb.AppendLine($"#ARTIST:{artistProp.GetString()}");
            }
            if (root.TryGetProperty("title", out var titleProp))
            {
                sb.AppendLine($"#TITLE:{titleProp.GetString()}");
            }
            if (root.TryGetProperty("bpm", out var bpmProp))
            {
                sb.AppendLine($"#BPM:{bpmProp}");
            }
            if (root.TryGetProperty("gap", out var gapProp))
            {
                sb.AppendLine($"#GAP:{gapProp}");
            }
            if (root.TryGetProperty("language", out var langProp))
            {
                sb.AppendLine($"#LANGUAGE:{langProp.GetString()}");
            }
            if (root.TryGetProperty("genre", out var genreProp))
            {
                sb.AppendLine($"#GENRE:{genreProp.GetString()}");
            }
            if (root.TryGetProperty("year", out var yearProp))
            {
                sb.AppendLine($"#YEAR:{yearProp}");
            }

            if (root.TryGetProperty("notes", out var notesProp) && notesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var note in notesProp.EnumerateArray())
                {
                    if (note.TryGetProperty("type", out var typeProp) &&
                        note.TryGetProperty("start", out var startProp) &&
                        note.TryGetProperty("length", out var lengthProp) &&
                        note.TryGetProperty("pitch", out var pitchProp) &&
                        note.TryGetProperty("lyric", out var lyricProp))
                    {
                        var type = typeProp.GetString() switch
                        {
                            "freestyle" => "F",
                            "golden" => "*",
                            "rap" => "R",
                            "rapgolden" => "G",
                            "line" => "-",
                            _ => ":"
                        };

                        if (type == "-")
                        {
                            sb.AppendLine($"- {startProp}");
                        }
                        else
                        {
                            sb.AppendLine($"{type} {startProp} {lengthProp} {pitchProp} {lyricProp.GetString()}");
                        }
                    }
                }
                sb.AppendLine("E");
            }

            var txt = sb.Length > 0 ? sb.ToString() : null;
            return (ytId, txt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch player.usdb.eu json for id {SongId}", songId);
            return null;
        }
    }
}
