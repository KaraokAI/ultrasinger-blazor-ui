using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UltraSinger.Contracts;
using UltraSinger.Processor.Entities;

namespace UltraSinger.Processor.Services;

/// <summary>
/// Runs UltraSinger for a single song. Replaces the old static
/// <c>SongProcessorService.InternalWorker</c>: it is resolved from DI and enqueued by song
/// id, so everything it writes lands on the shared <see cref="ISongStore"/> record rather
/// than on a Hangfire-deserialised copy of the song that nobody else can see.
/// </summary>
public class SongProcessingJob(
    ISongStore store,
    EnvironmentalValuesService environmentalValues,
    SyncedLyricsService lyricsService,
    OpenAIImproverService openAIImproverService,
    VocalSeparationService vocalSeparationService,
    ILogger<SongProcessingJob> logger)
{
    private const string OutputLeadingText = "Parse ultrastar txt -> ";

    private static readonly Regex AnsiColourCodeRegex = new(@"\[[0-9]{1,2}m", RegexOptions.Compiled);

    private static readonly HttpClient CoverHttpClient = new();

    public async Task ProcessAsync(Guid songId)
    {
        var song = store.Get(songId);

        if (song == null)
        {
            // The record went away (processor restarted, or it was deleted). Nothing to do.
            logger.LogWarning("Job started for unknown song {SongId}; skipping.", songId);
            return;
        }

        song.State = SongState.IN_PROGRESS;
        song.BeganProcessingAt = DateTime.Now;

        // Each job's raw output lives in its own directory beside the library, so everything
        // under it is by definition "the new files" for that job. Normalised so it matches the
        // path UltraSinger echoes back in its output (see ParseOutput).
        var jobDirectory = Path.GetFullPath(
            Path.Combine(environmentalValues.UltraStarDeluxeLocalLibraryPath, "..", "_jobs", song.Id.ToString("N")));

        try
        {
            if (song.Source == SongSource.USDB)
            {
                await ProcessUsdbSongAsync(song, jobDirectory);
            }
            else
            {
                await RunUltraSingerAsync(song, jobDirectory);

                try
                {
                    // Attempt OpenAI-based improvement of UltraStar file
                    await TryImproveUltraStarWithOpenAI(song, jobDirectory);
                }
                catch (Exception ex)
                {
                    // Do not fail the job if post-processing fails; just log it.
                    var msg = $"[UltraSinger][PostProcess] Improvement step failed: {ex.Message}";
                    song.AppendLog(msg);
                    logger.LogError(ex, "Post-processing failed for {SongId}", songId);
                }
            }

            // Unlike the OpenAI step above, bundling is the actual deliverable — a failure
            // here must fail the job rather than leave a bundle-less "COMPLETED" song.
            await BundleSongAsync(song, jobDirectory);

            song.State = SongState.COMPLETED;
        }
        catch
        {
            song.State = SongState.FAILED;
            throw;
        }
        finally
        {
            // Stamped once the job is genuinely finished, post-processing included, which is
            // what the wall clock in the UI has always effectively shown.
            song.CompletedAt = DateTime.Now;
        }
    }

    private async Task RunUltraSingerAsync(SongRecord song, string jobDirectory)
    {
        Directory.CreateDirectory(jobDirectory);

        var process = new Process();

        process.StartInfo = new ProcessStartInfo
        {
            FileName = environmentalValues.PythonExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // UltraSinger resolves some of its own resources relative to its src directory.
            WorkingDirectory = Path.GetDirectoryName(environmentalValues.UltraSingerScriptPath),
        };

        // Unbuffered, so progress lines reach the UI as they happen rather than in bursts.
        process.StartInfo.Environment["PYTHONUNBUFFERED"] = "1";

        foreach (var argument in (IEnumerable<string>)
                 [
                     environmentalValues.UltraSingerScriptPath,
                     "-i", song.Url,
                     "-o", jobDirectory,
                     "--language", environmentalValues.KaraokeLanguage,
                     .. environmentalValues.UltraSingerAdditionalArgs
                 ])
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        logger.LogInformation("Running: {FileName} {Arguments}", process.StartInfo.FileName, string.Join(' ', process.StartInfo.ArgumentList));

        foreach (var (key, value) in environmentalValues.GetUltraSingerAdditionalEnvironmentVariables())
        {
            process.StartInfo.Environment[key] = value;
        }

        // Python tracebacks don't carry the [UltraSinger] tag ParseOutput filters on, so keep
        // the raw tail of stderr to explain a crash.
        const int stderrTailLength = 30;
        var stderrTail = new Queue<string>(stderrTailLength);

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data == null) return;
            ParseOutput(song, jobDirectory, args.Data, isError: false);
            logger.LogInformation("{Line}", args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data == null) return;
            ParseOutput(song, jobDirectory, args.Data, isError: true);
            logger.LogInformation("{Line}", args.Data);

            lock (stderrTail)
            {
                if (stderrTail.Count == stderrTailLength) stderrTail.Dequeue();
                stderrTail.Enqueue(AnsiColourCodeRegex.Replace(args.Data, ""));
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            string tail;
            lock (stderrTail)
            {
                tail = string.Join('\n', stderrTail);
            }

            song.AppendError($"[UltraSinger] Exited with code {process.ExitCode}. Last output:\n{tail}");
            throw new ApplicationException($"UltraSinger exited with code {process.ExitCode}:\n{tail}");
        }
    }

    /// <summary>
    /// Filters raw process output down to the <c>[UltraSinger]</c> lines worth showing, and
    /// picks the produced .txt path out of the "Parse ultrastar txt -> " line.
    /// </summary>
    private void ParseOutput(SongRecord song, string jobDirectory, string? source, bool isError)
    {
        if (source == null)
        {
            return;
        }

        source = AnsiColourCodeRegex.Replace(source, "");

        if (source.Contains("[UltraSinger]"))
        {
            if (isError)
            {
                song.AppendError(source);
            }
            else
            {
                song.AppendLog(source);
            }
        }

        if (!source.Contains(OutputLeadingText))
        {
            return;
        }

        var rootPathIndex = source.IndexOf(jobDirectory, StringComparison.Ordinal);

        if (rootPathIndex == -1)
        {
            return;
        }

        var relativePathIndex = jobDirectory.Length + rootPathIndex;
        song.UltraStarTxtPath = source[relativePathIndex..].TrimStart('/');
    }

    private async Task TryImproveUltraStarWithOpenAI(SongRecord song, string jobDirectory)
    {
        if (!environmentalValues.EnableOpenAICorrections)
        {
            song.AppendLog("[UltraSinger][PostProcess] OpenAI corrections disabled by configuration. Skipping.");
            return;
        }

        logger.LogInformation("[UltraSinger][PostProcess] Attempting AI-based lyric improvement.");

        if (string.IsNullOrWhiteSpace(song.UltraStarTxtPath))
        {
            song.AppendLog("[UltraSinger][PostProcess] UltraStar txt path not found in output. Skipping improvement.");
            return;
        }

        if (string.IsNullOrWhiteSpace(environmentalValues.OpenAIKey))
        {
            song.AppendLog("[UltraSinger][PostProcess] OpenAI API key not configured. Skipping.");
            return;
        }

        var fullPath = Path.Combine(jobDirectory, song.UltraStarTxtPath!.TrimStart('/'));

        if (!File.Exists(fullPath))
        {
            song.AppendLog($"[UltraSinger][PostProcess] UltraStar file not found at expected location: {fullPath}");
            return;
        }

        var originalTxt = await File.ReadAllTextAsync(fullPath);

        // Fetch lyrics using title. The service handles conditioning, so what comes back is
        // ready to hand to the model.
        var title = song.Title ?? song.Url;
        var lyrics = await lyricsService.TryGetLyricsAsync(title);

        if (lyrics is null)
        {
            var noLyrics = $"[UltraSinger][PostProcess] Could not fetch lyrics for '{title}'. Skipping improvement.";
            song.AppendLog(noLyrics);
            song.AppendError(noLyrics);
            logger.LogWarning("{Message}", noLyrics);
            return;
        }

        song.AppendLog($"[UltraSinger][PostProcess] Fetched {(lyrics.IsSynced ? "time-synced" : "plain")} lyrics for '{title}'.");

        var improved = await openAIImproverService.ImproveUltraStarAsync(originalTxt, lyrics, title);

        if (string.IsNullOrWhiteSpace(improved))
        {
            const string noResponse = "[UltraSinger][PostProcess] OpenAI returned no content. Skipping write.";
            song.AppendLog(noResponse);
            song.AppendError(noResponse);
            logger.LogWarning("{Message}", noResponse);
            return;
        }
        
        var dir = Path.GetDirectoryName(fullPath)!;
        var name = Path.GetFileNameWithoutExtension(fullPath);
        var ext = Path.GetExtension(fullPath);
        string targetPath = Path.Combine(dir, name + "-improved" + ext);
        
        await File.WriteAllTextAsync(targetPath, improved, Encoding.UTF8);
        var msg = $"[UltraSinger][PostProcess] Wrote improved UltraStar file: {targetPath}";
        song.AppendLog(msg);
        song.AppendError(msg);
        logger.LogInformation("{Message}", msg);
    }

    private async Task ProcessUsdbSongAsync(SongRecord song, string jobDirectory)
    {
        song.AppendLog("[USDB] Starting processing of USDB song package...");

        if (string.IsNullOrWhiteSpace(song.Url))
        {
            throw new ApplicationException("Cannot process USDB song without a media URL.");
        }

        // USDB songs, unlike YouTube ones, don't get a per-song folder for free from
        // UltraSinger.py's own output convention, so we nest everything under one here.
        // Because the job root ends up containing exactly this one folder, BundleSongAsync's
        // existing includeBaseDirectory:false zip naturally wraps the song in its own folder
        // too, same as the YouTube path.
        //
        // The folder starts out as "song" and the files inside as "yt.*" so every downstream
        // tool (yt-dlp, ffmpeg, demucs) sees short, predictable paths and output names; only
        // once they're all done do we rename the folder and files to the real title, below.
        var sanitizedBaseTitle = SanitiseFileNameComponent(!string.IsNullOrWhiteSpace(song.Title) ? song.Title : "Song");
        var songDirectory = Path.Combine(jobDirectory, "song");

        Directory.CreateDirectory(songDirectory);

        song.AppendLog($"[USDB] Downloading media from {song.Url} via yt-dlp...");

        // Deliberately not "%(title)s.%(ext)s": demucs names its output folder after the input
        // file, and LocateAndRenameStems is simpler with a fixed name. The file is renamed to
        // the real title below.
        var outputTemplate = Path.Combine(songDirectory, "yt.%(ext)s");

        // UltraStar Deluxe (and the bundled video player) need H.264: bias yt-dlp towards an
        // avc1 stream up front so the ffprobe/ffmpeg fallback below is rarely needed, rather
        // than routinely re-encoding a VP9/AV1 download after the fact.
        const string formatSelector = "bestvideo[vcodec^=avc1]+bestaudio/best[vcodec^=avc1]/bestvideo+bestaudio/best";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = environmentalValues.YTDLPPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = songDirectory
            }
        };

        List<string> ytdlpArguments =
        [
            "-f", formatSelector,
            "--merge-output-format", "mp4",
            "--extract-audio",
            "--audio-format", "mp3",
            "--audio-quality", "0",
            "--keep-video",
            "-o", outputTemplate,
        ];

        if (environmentalValues.YTDLPCookiesFromBrowser is { } cookiesFromBrowser)
        {
            ytdlpArguments.AddRange(["--cookies-from-browser", cookiesFromBrowser]);
        }

        ytdlpArguments.Add(song.Url);

        foreach (var argument in ytdlpArguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                song.AppendLog($"[yt-dlp] {args.Data}");
                logger.LogInformation("[yt-dlp] {Line}", args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                song.AppendError($"[yt-dlp] {args.Data}");
                logger.LogWarning("[yt-dlp error] {Line}", args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new ApplicationException($"yt-dlp failed to download media: {song.ErrorText}");
        }

        // Identify downloaded audio and video files
        var allFiles = Directory.GetFiles(songDirectory);
        var audioFile = allFiles.FirstOrDefault(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                        ?? allFiles.FirstOrDefault(f => f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                                                        f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                                                        f.EndsWith(".opus", StringComparison.OrdinalIgnoreCase));

        var videoFile = allFiles.FirstOrDefault(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                        ?? allFiles.FirstOrDefault(f => f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                                                        f.EndsWith(".webm", StringComparison.OrdinalIgnoreCase));

        var audioFileName = audioFile != null ? Path.GetFileName(audioFile) : null;
        var videoFileName = videoFile != null ? Path.GetFileName(videoFile) : null;

        song.AppendLog($"[USDB] Downloaded media files - Audio: {audioFileName ?? "None"}, Video: {videoFileName ?? "None"}");

        if (videoFileName != null)
        {
            videoFileName = await EnsureH264Async(song, songDirectory, videoFileName);
        }

        string? coverFileName = null;

        if (song.UsdbSongId is { } usdbSongId)
        {
            coverFileName = await DownloadCoverAsync(song, songDirectory, usdbSongId);
        }

        string? vocalsFileName = null;
        string? instrumentalFileName = null;

        if (environmentalValues.EnableVocalSeparation && audioFileName != null)
        {
            try
            {
                (vocalsFileName, instrumentalFileName) = await vocalSeparationService.SeparateAsync(song, songDirectory, audioFileName);
            }
            catch (Exception ex)
            {
                var msg = $"[demucs] Vocal separation failed: {ex.Message}";
                song.AppendLog(msg);
                logger.LogError(ex, "Vocal separation failed for {SongId}", song.Id);
            }
        }

        // Every external tool is done with the "song/yt.*" paths; rename to the real title.
        var titledDirectory = Path.Combine(jobDirectory, sanitizedBaseTitle);
        Directory.Move(songDirectory, titledDirectory);
        songDirectory = titledDirectory;

        audioFileName = RenameToTitledFile(songDirectory, audioFileName, sanitizedBaseTitle);
        videoFileName = RenameToTitledFile(songDirectory, videoFileName, sanitizedBaseTitle);
        vocalsFileName = RenameToTitledFile(songDirectory, vocalsFileName, sanitizedBaseTitle, " [Vocals]");
        instrumentalFileName = RenameToTitledFile(songDirectory, instrumentalFileName, sanitizedBaseTitle, " [Instrumental]");
        coverFileName = RenameToTitledFile(songDirectory, coverFileName, sanitizedBaseTitle, " [Cover]");

        // Once vocals/instrumental exist, the full mix is redundant background noise for the
        // client bundle - the instrumental is what UltraStar Deluxe should actually play back.
        var hasSeparatedStems = vocalsFileName != null && instrumentalFileName != null;
        var mp3FileName = hasSeparatedStems ? instrumentalFileName : audioFileName;

        var txtContent = song.UltraStarTxt ?? string.Empty;
        var updatedTxt = UpdateUltraStarTxtHeaders(txtContent, mp3FileName, videoFileName, vocalsFileName, instrumentalFileName, coverFileName);

        var sanitizedFileName = sanitizedBaseTitle + ".txt";
        var txtFilePath = Path.Combine(songDirectory, sanitizedFileName);

        await File.WriteAllTextAsync(txtFilePath, updatedTxt, Encoding.UTF8);
        song.UltraStarTxtPath = sanitizedFileName;

        song.AppendLog($"[USDB] Created UltraStar song file: {sanitizedFileName}");

        // Only the files actually referenced by the .txt (plus the .txt itself) should reach
        // the client - not the full mix once it's superseded, and not any other yt-dlp/demucs
        // leftovers.
        var filesToKeep = new[] { mp3FileName, videoFileName, hasSeparatedStems ? vocalsFileName : null, coverFileName, sanitizedFileName }
            .Where(f => f != null)
            .Select(f => f!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(songDirectory))
        {
            if (!filesToKeep.Contains(Path.GetFileName(file)))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>
    /// USDB hosts each song's cover at a fixed, predictable URL keyed by the USDB song id -
    /// the same convention UltraSinger.py already relies on for YouTube-sourced songs. A
    /// missing cover (song has none, or a network hiccup) just means no #COVER header; it
    /// isn't worth failing the whole job over.
    /// </summary>
    private async Task<string?> DownloadCoverAsync(SongRecord song, string localDir, int usdbSongId)
    {
        var coverUrl = $"https://usdb.animux.de/data/cover/{usdbSongId}.jpg";

        try
        {
            using var response = await CoverHttpClient.GetAsync(coverUrl);

            if (!response.IsSuccessStatusCode)
            {
                song.AppendLog($"[USDB] No cover available at {coverUrl} ({(int)response.StatusCode}).");
                return null;
            }

            const string coverFileName = "cover.jpg";
            var coverPath = Path.Combine(localDir, coverFileName);

            await using var fileStream = File.Create(coverPath);
            await response.Content.CopyToAsync(fileStream);

            song.AppendLog("[USDB] Downloaded cover image.");

            return coverFileName;
        }
        catch (Exception ex)
        {
            song.AppendLog($"[USDB] Failed to download cover: {ex.Message}");
            logger.LogWarning(ex, "Failed to download USDB cover for song {SongId}", song.Id);
            return null;
        }
    }

    /// <summary>
    /// UltraStar Deluxe requires H.264 video; yt-dlp's format selector already biases towards
    /// avc1, but some USDB sources only offer VP9/AV1, so this is the guaranteed fallback -
    /// probe the downloaded video and re-encode with ffmpeg if it isn't already H.264.
    /// </summary>
    private async Task<string> EnsureH264Async(SongRecord song, string localDir, string videoFileName)
    {
        var videoPath = Path.Combine(localDir, videoFileName);
        var codec = await ProbeVideoCodecAsync(videoPath);

        if (string.Equals(codec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            return videoFileName;
        }

        song.AppendLog($"[ffmpeg] Video codec is '{codec ?? "unknown"}', not H.264; transcoding...");

        var transcodedFileName = Path.GetFileNameWithoutExtension(videoFileName) + "_h264.mp4";
        var transcodedPath = Path.Combine(localDir, transcodedFileName);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = environmentalValues.FfmpegPath,
                Arguments = $"-y -i \"{videoPath}\" -c:v libx264 -preset medium -crf 20 -c:a aac \"{transcodedPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                logger.LogInformation("[ffmpeg] {Line}", args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || !File.Exists(transcodedPath))
        {
            throw new ApplicationException($"ffmpeg failed to transcode video to H.264 (exit code {process.ExitCode}).");
        }

        File.Delete(videoPath);
        song.AppendLog($"[ffmpeg] Transcoded video to H.264: {transcodedFileName}");

        return transcodedFileName;
    }

    private async Task<string?> ProbeVideoCodecAsync(string videoPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = environmentalValues.FfprobePath,
                Arguments = $"-v error -select_streams v:0 -show_entries stream=codec_name -of csv=p=0 \"{videoPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 ? output.Trim() : null;
    }

    public static string UpdateUltraStarTxtHeaders(
        string txtContent,
        string? audioFileName,
        string? videoFileName,
        string? vocalsFileName = null,
        string? instrumentalFileName = null,
        string? coverFileName = null)
    {
        return UltraStarTxtHelper.UpdateUltraStarTxtHeaders(txtContent, audioFileName, videoFileName, vocalsFileName, instrumentalFileName, coverFileName);
    }

    /// <summary>
    /// Zips the job's per-job output directory (no compression — audio/video are already
    /// compressed) into <see cref="EnvironmentalValuesService.BundleStoragePath"/>, then
    /// deletes the raw directory. The zip becomes the artifact of record; unlike the OpenAI
    /// step, a failure here is rethrown so the job ends up <see cref="SongState.FAILED"/>
    /// rather than "completed" with nothing for the UI to fetch.
    /// </summary>
    private Task BundleSongAsync(SongRecord song, string jobDirectory)
    {
        if (!Directory.Exists(jobDirectory))
        {
            throw new ApplicationException($"No output directory found to bundle at {jobDirectory}.");
        }

        Directory.CreateDirectory(environmentalValues.BundleStoragePath);

        var zipPath = Path.Combine(environmentalValues.BundleStoragePath, $"{song.Id:N}.zip");

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(jobDirectory, zipPath, CompressionLevel.NoCompression, includeBaseDirectory: false);

        song.BundlePath = zipPath;

        Directory.Delete(jobDirectory, recursive: true);

        var msg = $"[UltraSinger][Bundle] Bundled output into {zipPath}";
        song.AppendLog(msg);
        logger.LogInformation("{Message}", msg);

        return Task.CompletedTask;
    }

    private static string SanitiseFileNameComponent(string source) =>
        string.Join("_", source.Split(Path.GetInvalidFileNameChars()));

    /// <summary>
    /// Renames a "yt.*"-style download (or a demucs stem derived from one) to the real,
    /// human-readable title once no further external tool needs its path.
    /// </summary>
    private static string? RenameToTitledFile(string localDir, string? currentFileName, string sanitizedBaseTitle, string suffix = "")
    {
        if (currentFileName == null)
        {
            return null;
        }

        var newFileName = sanitizedBaseTitle + suffix + Path.GetExtension(currentFileName);
        var oldPath = Path.Combine(localDir, currentFileName);
        var newPath = Path.Combine(localDir, newFileName);

        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(oldPath, newPath, overwrite: true);
        }

        return newFileName;
    }
}
