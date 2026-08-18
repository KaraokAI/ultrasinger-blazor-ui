using System.Diagnostics;
using UltraSinger.Processor.Entities;

namespace UltraSinger.Processor.Services;

/// <summary>
/// Runs an external vocal-separation tool (demucs by default) against a downloaded USDB
/// audio file, configured the same way as the main UltraSinger Python process: an
/// executable + argument prefix that can wrap the call in WSL/conda/etc.
/// </summary>
public class VocalSeparationService(
    EnvironmentalValuesService environmentalValues,
    ILogger<VocalSeparationService> logger)
{
    public async Task<(string? VocalsFileName, string? InstrumentalFileName)> SeparateAsync(
        SongRecord song, JobDirectories jobDirectories, string audioFileName)
    {
        var wslInputPath = SongProcessingJob.SanitisePath($"{jobDirectories.WslPath.TrimEnd('/')}/{audioFileName}");
        var wslOutputDir = SongProcessingJob.SanitisePath(jobDirectories.WslPath);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = environmentalValues.VocalSeparationExecutable,
                Arguments = $"{environmentalValues.VocalSeparationArguments} {environmentalValues.VocalSeparationAdditionalArgs} -o {wslOutputDir} {wslInputPath}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };

        logger.LogInformation("Using argument: {FileName} {Arguments}", process.StartInfo.FileName, process.StartInfo.Arguments);

        process.OutputDataReceived += (_, args) => LogLine(song, args.Data, isError: false);
        process.ErrorDataReceived += (_, args) => LogLine(song, args.Data, isError: true);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new ApplicationException($"demucs exited with code {process.ExitCode}.");
        }

        var (vocalsFileName, instrumentalFileName) = LocateAndRenameStems(jobDirectories.LocalPath, audioFileName, song);

        vocalsFileName = await EnsureMp3Async(jobDirectories.LocalPath, vocalsFileName, song);
        instrumentalFileName = await EnsureMp3Async(jobDirectories.LocalPath, instrumentalFileName, song);

        return (vocalsFileName, instrumentalFileName);
    }

    /// <summary>
    /// demucs writes stems as .wav, which UltraStar Deluxe handles poorly - convert to .mp3
    /// with ffmpeg and remove the .wav so nothing but the .mp3 reaches the bundle.
    /// </summary>
    private async Task<string?> EnsureMp3Async(string localDir, string? fileName, SongRecord song)
    {
        if (fileName == null || string.Equals(Path.GetExtension(fileName), ".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        var sourcePath = Path.Combine(localDir, fileName);
        var mp3FileName = Path.GetFileNameWithoutExtension(fileName) + ".mp3";
        var mp3Path = Path.Combine(localDir, mp3FileName);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = environmentalValues.FfmpegPath,
                Arguments = $"-y -i \"{sourcePath}\" -codec:a libmp3lame -qscale:a 2 \"{mp3Path}\"",
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

        if (process.ExitCode != 0 || !File.Exists(mp3Path))
        {
            throw new ApplicationException($"ffmpeg failed to convert '{fileName}' to mp3 (exit code {process.ExitCode}).");
        }

        File.Delete(sourcePath);
        song.AppendLog($"[ffmpeg] Converted {fileName} to {mp3FileName}");

        return mp3FileName;
    }

    private void LogLine(SongRecord song, string? line, bool isError)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var prefixed = $"[demucs] {line}";

        if (isError)
        {
            song.AppendError(prefixed);
        }
        else
        {
            song.AppendLog(prefixed);
        }

        logger.LogInformation("{Line}", prefixed);
    }

    /// <summary>
    /// demucs writes to &lt;out&gt;/&lt;model&gt;/&lt;track_name&gt;/{vocals,no_vocals}.&lt;ext&gt;.
    /// Searching by filename avoids having to parse the model name back out of
    /// VocalSeparationAdditionalArgs.
    /// </summary>
    private (string? VocalsFileName, string? InstrumentalFileName) LocateAndRenameStems(
        string localJobDir, string audioFileName, SongRecord song)
    {
        var vocalsSource = Directory.GetFiles(localJobDir, "vocals.*", SearchOption.AllDirectories).FirstOrDefault();
        var instrumentalSource = Directory.GetFiles(localJobDir, "no_vocals.*", SearchOption.AllDirectories).FirstOrDefault();

        if (vocalsSource == null || instrumentalSource == null)
        {
            song.AppendLog("[demucs] Separation completed but output stems were not found; skipping.");
            return (null, null);
        }

        var trackName = Path.GetFileNameWithoutExtension(audioFileName);
        var ext = Path.GetExtension(vocalsSource);

        var vocalsFileName = $"{trackName} [Vocals]{ext}";
        var instrumentalFileName = $"{trackName} [Instrumental]{ext}";

        var vocalsTarget = Path.Combine(localJobDir, vocalsFileName);
        var instrumentalTarget = Path.Combine(localJobDir, instrumentalFileName);

        File.Move(vocalsSource, vocalsTarget, overwrite: true);
        File.Move(instrumentalSource, instrumentalTarget, overwrite: true);

        // demucs' <out>/<model>/<track_name>/ subdirectory is now empty; remove its topmost
        // ancestor still inside localJobDir so the bundle only contains flat files.
        var modelDir = Path.GetDirectoryName(Path.GetDirectoryName(vocalsSource));
        if (modelDir != null && Directory.Exists(modelDir) && !string.Equals(modelDir, localJobDir, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(modelDir, recursive: true);
        }

        song.AppendLog($"[demucs] Wrote vocals/instrumental stems: {vocalsFileName}, {instrumentalFileName}");

        return (vocalsFileName, instrumentalFileName);
    }
}
