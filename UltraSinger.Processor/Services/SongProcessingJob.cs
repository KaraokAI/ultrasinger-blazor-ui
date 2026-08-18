using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UltraSinger.Contracts;
using UltraSinger.Processor.Entities;

namespace UltraSinger.Processor.Services;

/// <summary>
/// Where a single job's raw UltraSinger output lives, nested under the existing WSL/local
/// root pair rather than a separate temp filesystem — no new path translation is needed, and
/// because the directory belongs to exactly one job, everything under it is by definition
/// "the new files" for that job.
/// </summary>
public readonly record struct JobDirectories(string LocalPath, string WslPath);

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

        var jobDirectories = new JobDirectories(
            LocalPath: Path.Combine(environmentalValues.UltraStarDeluxeLocalLibraryPath, "..", "_jobs", song.Id.ToString("N")),
            WslPath: $"{environmentalValues.UltraStarDeluxeWSLPath.TrimEnd('/')}/../_jobs/{song.Id:N}");

        try
        {
            if (song.Source == SongSource.USDB)
            {
                await ProcessUsdbSongAsync(song, jobDirectories);
            }
            else
            {
                await RunUltraSingerAsync(song, jobDirectories);

                try
                {
                    // Attempt OpenAI-based improvement of UltraStar file
                    await TryImproveUltraStarWithOpenAI(song, jobDirectories);
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
            await BundleSongAsync(song, jobDirectories);

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

    private async Task RunUltraSingerAsync(SongRecord song, JobDirectories jobDirectories)
    {
        Directory.CreateDirectory(jobDirectories.LocalPath);

        var process = new Process();
        var fileName = environmentalValues.PythonExecutable;

        var ultraStarDeluxePath = SanitisePath($"{environmentalValues.UltraSingerPath.TrimEnd('/', '\\')}/src/UltraSinger.py");
        var wslOutputPath = SanitisePath(jobDirectories.WslPath);

        var arguments = $"""{environmentalValues.PythonArguments} {ultraStarDeluxePath} -i {song.Url} -o {wslOutputPath} --language "{environmentalValues.KaraokeLanguage}" {environmentalValues.UltraSingerAdditionalArgs}""";

        logger.LogInformation("Using argument: {FileName} {Arguments}", fileName, arguments);

        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var (key, value) in environmentalValues.GetUltraSingerAdditionalEnvironmentVariables())
        {
            process.StartInfo.Environment[key] = value;
        }

        process.OutputDataReceived += (_, args) =>
        {
            ParseOutput(song, jobDirectories, args.Data, isError: false);
            logger.LogInformation("{Line}", args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            ParseOutput(song, jobDirectories, args.Data, isError: true);
            logger.LogInformation("{Line}", args.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new ApplicationException($"Failed to process URL: {song.ErrorText}");
        }
    }

    /// <summary>
    /// Filters raw process output down to the <c>[UltraSinger]</c> lines worth showing, and
    /// picks the produced .txt path out of the "Parse ultrastar txt -> " line.
    /// </summary>
    private void ParseOutput(SongRecord song, JobDirectories jobDirectories, string? source, bool isError)
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

        var rootPathIndex = source.IndexOf(jobDirectories.WslPath, StringComparison.Ordinal);

        if (rootPathIndex == -1)
        {
            return;
        }

        var relativePathIndex = jobDirectories.WslPath.Length + rootPathIndex;
        song.UltraStarTxtPath = source[relativePathIndex..].TrimStart('/');
    }

    private async Task TryImproveUltraStarWithOpenAI(SongRecord song, JobDirectories jobDirectories)
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

        var localRoot = jobDirectories.LocalPath.TrimEnd('\\', '/');
        var relative = song.UltraStarTxtPath!.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(localRoot, relative);

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

    private async Task ProcessUsdbSongAsync(SongRecord song, JobDirectories jobDirectories)
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
        // The folder itself is named "song" - NOT the title - for exactly the same reason the
        // files inside it are named "yt.*": every path in here gets passed through a shell to
        // demucs, and SanitisePath only escapes spaces. A title containing parens/quotes/&
        // baked into the *directory* name would break that shell call just as surely as it
        // would in a filename. Only once demucs is done with this path do we rename the folder
        // itself to the real title, below.
        var sanitizedBaseTitle = SanitiseFileNameComponent(!string.IsNullOrWhiteSpace(song.Title) ? song.Title : "Song");
        var songDirectories = new JobDirectories(
            LocalPath: Path.Combine(jobDirectories.LocalPath, "song"),
            WslPath: $"{jobDirectories.WslPath.TrimEnd('/')}/song");

        Directory.CreateDirectory(songDirectories.LocalPath);

        song.AppendLog($"[USDB] Downloading media from {song.Url} via yt-dlp...");

        // Deliberately not "%(title)s.%(ext)s": YouTube titles routinely contain
        // parens/ampersands/etc. that aren't shell-safe, and SanitisePath only escapes
        // spaces. Every downstream tool (demucs) is invoked through a shell, so the file
        // stays as this fixed, boring name until it's renamed to the real title below.
        var outputTemplate = Path.Combine(songDirectories.LocalPath, "yt.%(ext)s");

        // UltraStar Deluxe (and the bundled video player) need H.264: bias yt-dlp towards an
        // avc1 stream up front so the ffprobe/ffmpeg fallback below is rarely needed, rather
        // than routinely re-encoding a VP9/AV1 download after the fact.
        const string formatSelector = "bestvideo[vcodec^=avc1]+bestaudio/best[vcodec^=avc1]/bestvideo+bestaudio/best";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = environmentalValues.YTDLPPath,
                Arguments = $"-f \"{formatSelector}\" --merge-output-format mp4 --extract-audio --cookies-from-browser firefox --audio-format mp3 --audio-quality 0 --keep-video -o \"{outputTemplate}\" \"{song.Url}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = songDirectories.LocalPath
            }
        };

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
        var allFiles = Directory.GetFiles(songDirectories.LocalPath);
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
            videoFileName = await EnsureH264Async(song, songDirectories.LocalPath, videoFileName);
        }

        string? vocalsFileName = null;
        string? instrumentalFileName = null;

        if (environmentalValues.EnableVocalSeparation && audioFileName != null)
        {
            try
            {
                (vocalsFileName, instrumentalFileName) = await vocalSeparationService.SeparateAsync(song, songDirectories, audioFileName);
            }
            catch (Exception ex)
            {
                var msg = $"[demucs] Vocal separation failed: {ex.Message}";
                song.AppendLog(msg);
                logger.LogError(ex, "Vocal separation failed for {SongId}", song.Id);
            }
        }

        // Only now that every shell-invoked tool (demucs) is done with the "song/yt.*" path do
        // we rename the folder and its files to the real title; anything shell-unsafe in the
        // title can no longer break a command line.
        var titledLocalPath = Path.Combine(jobDirectories.LocalPath, sanitizedBaseTitle);
        Directory.Move(songDirectories.LocalPath, titledLocalPath);
        songDirectories = songDirectories with { LocalPath = titledLocalPath };

        audioFileName = RenameToTitledFile(songDirectories.LocalPath, audioFileName, sanitizedBaseTitle);
        videoFileName = RenameToTitledFile(songDirectories.LocalPath, videoFileName, sanitizedBaseTitle);
        vocalsFileName = RenameToTitledFile(songDirectories.LocalPath, vocalsFileName, sanitizedBaseTitle, " [Vocals]");
        instrumentalFileName = RenameToTitledFile(songDirectories.LocalPath, instrumentalFileName, sanitizedBaseTitle, " [Instrumental]");

        // Once vocals/instrumental exist, the full mix is redundant background noise for the
        // client bundle - the instrumental is what UltraStar Deluxe should actually play back.
        var hasSeparatedStems = vocalsFileName != null && instrumentalFileName != null;
        var mp3FileName = hasSeparatedStems ? instrumentalFileName : audioFileName;

        var txtContent = song.UltraStarTxt ?? string.Empty;
        var updatedTxt = UpdateUltraStarTxtHeaders(txtContent, mp3FileName, videoFileName, vocalsFileName, instrumentalFileName);

        var sanitizedFileName = sanitizedBaseTitle + ".txt";
        var txtFilePath = Path.Combine(songDirectories.LocalPath, sanitizedFileName);

        await File.WriteAllTextAsync(txtFilePath, updatedTxt, Encoding.UTF8);
        song.UltraStarTxtPath = sanitizedFileName;

        song.AppendLog($"[USDB] Created UltraStar song file: {sanitizedFileName}");

        // Only the files actually referenced by the .txt (plus the .txt itself) should reach
        // the client - not the full mix once it's superseded, and not any other yt-dlp/demucs
        // leftovers.
        var filesToKeep = new[] { mp3FileName, videoFileName, hasSeparatedStems ? vocalsFileName : null, sanitizedFileName }
            .Where(f => f != null)
            .Select(f => f!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(songDirectories.LocalPath))
        {
            if (!filesToKeep.Contains(Path.GetFileName(file)))
            {
                File.Delete(file);
            }
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
        string? instrumentalFileName = null)
    {
        return UltraStarTxtHelper.UpdateUltraStarTxtHeaders(txtContent, audioFileName, videoFileName, vocalsFileName, instrumentalFileName);
    }

    /// <summary>
    /// Zips the job's per-job output directory (no compression — audio/video are already
    /// compressed) into <see cref="EnvironmentalValuesService.BundleStoragePath"/>, then
    /// deletes the raw directory. The zip becomes the artifact of record; unlike the OpenAI
    /// step, a failure here is rethrown so the job ends up <see cref="SongState.FAILED"/>
    /// rather than "completed" with nothing for the UI to fetch.
    /// </summary>
    private Task BundleSongAsync(SongRecord song, JobDirectories jobDirectories)
    {
        if (!Directory.Exists(jobDirectories.LocalPath))
        {
            throw new ApplicationException($"No output directory found to bundle at {jobDirectories.LocalPath}.");
        }

        Directory.CreateDirectory(environmentalValues.BundleStoragePath);

        var zipPath = Path.Combine(environmentalValues.BundleStoragePath, $"{song.Id:N}.zip");

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(jobDirectories.LocalPath, zipPath, CompressionLevel.NoCompression, includeBaseDirectory: false);

        song.BundlePath = zipPath;

        Directory.Delete(jobDirectories.LocalPath, recursive: true);

        var msg = $"[UltraSinger][Bundle] Bundled output into {zipPath}";
        song.AppendLog(msg);
        logger.LogInformation("{Message}", msg);

        return Task.CompletedTask;
    }

    public static string SanitisePath(string source) => source.Replace(" ", "\\ ");

    private static string SanitiseFileNameComponent(string source) =>
        string.Join("_", source.Split(Path.GetInvalidFileNameChars()));

    /// <summary>
    /// Renames a "yt.*"-style download (or a demucs stem derived from one) to the real,
    /// human-readable title now that no further shell command will see its path.
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
