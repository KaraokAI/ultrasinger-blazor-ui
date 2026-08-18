using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UltraSinger.Blazor.Configuration;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Services;

public class UsdbDownloadService(
    UsdbService usdbService,
    IOptions<LibraryConfiguration> libraryOptions,
    LocalLibraryService localLibraryService,
    BlazorSongQueueService songQueueService,
    ILogger<UsdbDownloadService> logger)
{
    public async Task<bool> DownloadSongAsync(int songId, CancellationToken cancellationToken = default)
    {
        var localPath = libraryOptions.Value.LocalPath;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            logger.LogError("Cannot download USDB song {SongId}: Library:LocalPath is not configured.", songId);
            return false;
        }

        Directory.CreateDirectory(localPath);

        logger.LogInformation("Fetching USDB details for song {SongId}...", songId);
        var details = await usdbService.GetSongDetailsAsync(songId);
        if (details == null)
        {
            logger.LogError("Could not fetch details for USDB song {SongId}", songId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(details.UltraStarTxt))
        {
            var txt = await usdbService.TryFetchUltraStarTxtAsync(songId);
            if (!string.IsNullOrWhiteSpace(txt))
            {
                details.UltraStarTxt = txt;
            }
        }

        if (string.IsNullOrWhiteSpace(details.UltraStarTxt))
        {
            logger.LogError("No UltraStar txt content found for USDB song {SongId}", songId);
            return false;
        }

        var artist = !string.IsNullOrWhiteSpace(details.Artist) ? details.Artist : "Unknown";
        var title = !string.IsNullOrWhiteSpace(details.Title) ? details.Title : $"Song_{songId}";
        var folderName = SanitizeFileName($"{artist} - {title}");
        var songDir = Path.Combine(localPath, folderName);
        Directory.CreateDirectory(songDir);

        string? audioFileName = null;
        string? videoFileName = null;

        if (!string.IsNullOrWhiteSpace(details.YoutubeUrl))
        {
            logger.LogInformation("Downloading media for {Artist} - {Title} from {Url} via yt-dlp...", artist, title, details.YoutubeUrl);
            var ytdlpPath = ResolveYtDlpPath(libraryOptions.Value.YTDLPPath);

            var ytdlpArgs = $"-f \"[height<=720]+bestaudio/bestvideo+bestaudio/best[vcodec^=avc1][height<=720]/bestvideo[height<=720]+bestaudio/best[height<=720]/best\" --merge-output-format mp4 --extract-audio --audio-format mp3 --keep-video --cookies-from-browser firefox -o \"%(title)s.%(ext)s\" \"{details.YoutubeUrl}\"";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ytdlpPath,
                    Arguments = ytdlpArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = songDir,
                    UseShellExecute = false
                }
            };

            var outputSb = new StringBuilder();
            var errorSb = new StringBuilder();

            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    outputSb.AppendLine(args.Data);
                    logger.LogDebug("[yt-dlp] {Data}", args.Data);
                }
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    errorSb.AppendLine(args.Data);
                    logger.LogDebug("[yt-dlp err] {Data}", args.Data);
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    logger.LogWarning("yt-dlp exited with code {Code} for song {SongId}. Output: {Error}", process.ExitCode, songId, errorSb.ToString());
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to run yt-dlp for USDB song {SongId}", songId);
            }

            var allFiles = Directory.GetFiles(songDir);
            var audioFile = allFiles.FirstOrDefault(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                            ?? allFiles.FirstOrDefault(f => f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                                                            f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                                                            f.EndsWith(".opus", StringComparison.OrdinalIgnoreCase));

            var videoFile = allFiles.FirstOrDefault(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                            ?? allFiles.FirstOrDefault(f => f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                                                            f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase));

            audioFileName = audioFile != null ? Path.GetFileName(audioFile) : null;
            videoFileName = videoFile != null ? Path.GetFileName(videoFile) : null;
        }

        var updatedTxt = UltraStarTxtHelper.UpdateUltraStarTxtHeaders(details.UltraStarTxt, audioFileName, videoFileName);
        var txtPath = Path.Combine(songDir, $"{folderName}.txt");
        await File.WriteAllTextAsync(txtPath, updatedTxt, Encoding.UTF8, cancellationToken);

        logger.LogInformation("USDB song {Artist} - {Title} downloaded and saved to {Path}", artist, title, txtPath);

        localLibraryService.Refresh();

        // Automatically add to Blazor song queue
        songQueueService.Enqueue(new SongQueueItem
        {
            Title = title,
            Artist = artist,
            Source = SongSource.USDB,
            ExtraInfo = details.Year?.ToString(),
            FilePath = txtPath,
            QueuedAt = DateTime.Now
        });

        return true;
    }

    private static string ResolveYtDlpPath(string configured)
    {
        if (File.Exists(configured))
        {
            return configured;
        }

        string[] searchPaths =
        [
            configured,
            "/opt/homebrew/bin/yt-dlp",
            "/usr/local/bin/yt-dlp",
            "/usr/bin/yt-dlp"
        ];

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return configured;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
