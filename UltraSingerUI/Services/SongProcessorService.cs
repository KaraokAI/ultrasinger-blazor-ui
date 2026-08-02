using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.RegularExpressions;
using Hangfire;
using Hangfire.Storage;
using UltraSingerUI.Constants;
using UltraSingerUI.Entities;

namespace UltraSingerUI.Services;

public class SongProcessorService(SongQueue songQueue)
{
    private SongQueue SongQueue { get; set; } = songQueue;

    private IStorageConnection StorageApi { get; } = JobStorage.Current.GetConnection();

    public void ProcessSong(Song newSong)
    {
        if (SongQueue.SongList.Any(x =>
                x.Url == newSong.Url &&
                new[] { SongState.COMPLETED, SongState.IN_PROGRESS, SongState.NOT_STARTED }.Contains(x.JobState)))
        {
            // Already processed/processing/will be processed.
            return;
        }
        
        var jobId = BackgroundJob.Enqueue(Queues.SongQueue, () => InternalWorker.Process(newSong));
        newSong.JobId = jobId;
        SongQueue.SongList.Push(newSong);
    }

    public void UpdateSongState(Song existingSong)
    {
        var job = StorageApi.GetJobData(existingSong.JobId);

        if (job == null)
        {
            // GetJobData has a [CanBeNull] attribute on it...
            // Very odd.
            existingSong.JobState = SongState.UNKNOWN;
            return;
        }

        if (job.State == "Processing" && existingSong.JobState != SongState.IN_PROGRESS)
        {
            existingSong.BeganProcessingAt = DateTime.Now;
        }
        
        existingSong.JobState = job.State switch
        {
            "Succeeded" => SongState.COMPLETED,
            "Failed" => SongState.FAILED,
            "Processing" => SongState.IN_PROGRESS,
            "Enqueued" => SongState.NOT_STARTED,
            _ => SongState.UNKNOWN,
        };
    }

    public string GetLatestLog()
    {
        return InternalWorker.Stdout.ToString();
    }

    public IEnumerable<Song> GetProcessedSongList() => SongQueue.SongList;
    
    private static class InternalWorker
    {
        public static StringBuilder Stdout { get; set; } = new();

        public static StringBuilder Stderr { get; set; } = new();

        private static EnvironmentalValuesService EnvironmentalValuesService { get; set; } = new();
        
        private const string OutputLeadingText = "Parse ultrastar txt -> ";
        private static Regex AnsiColourCodeRegex { get; set; } = new(@"\[[0-9]{1,2}m");
        

        private static void ParseOutput(StringBuilder output, string? source, Song song)
        {
            if (source == null)
            {
                return;
            }
            
            source = AnsiColourCodeRegex.Replace(source, "");
            
            if (source.Contains("[UltraSinger]") == true)
            {
                output.Insert(0, source + '\n');
            }
        
            if (source.Contains(OutputLeadingText) != true)
            {
                return;
            }

            var rootPathIndex = source.IndexOf(EnvironmentalValuesService.UltraStarDeluxeWSLPath, StringComparison.Ordinal);

            if (rootPathIndex == -1)
            {
                return;
            }
            
            var relativePathIndex = EnvironmentalValuesService.UltraStarDeluxeWSLPath.Length + rootPathIndex;
            song.UltraStarTxtPath = source[relativePathIndex..].TrimStart('/');
        }
        
        public static async Task Process(Song newSong)
        {
            Stdout = new();
            Stderr = new();
            
            var process = new Process();
            var fileName = EnvironmentalValuesService.PythonExecutable;
            
            var ultraStarDeluxePath = SanitisePath($"{EnvironmentalValuesService.UltraSingerPath.TrimEnd('/', '\\')}/src/UltraSinger.py");
            var wslOutputPath = EnvironmentalValuesService.UltraStarDeluxeWSLPath;
            
            var arguments = $"""{EnvironmentalValuesService.PythonArguments} {ultraStarDeluxePath} -i {newSong.Url} -o {wslOutputPath} --language "{EnvironmentalValuesService.KaraokeLanguage}" {EnvironmentalValuesService.UltraSingerAdditionalArgs}""";
            
            Console.WriteLine( $"Using argument: {fileName} {arguments}\n");

            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var envVar in EnvironmentalValuesService.GetUltraSingerAdditionalEnvironmentVariables())
            {
                process.StartInfo.Environment.Add(envVar.Item1, envVar.Item2);
            }

            process.OutputDataReceived += (sender, args) =>
            {
                ParseOutput(Stdout, args.Data, newSong);
                Console.WriteLine(args.Data);
            };
            process.ErrorDataReceived += (sender, args) =>
            {
                ParseOutput(Stderr, args.Data, newSong);
                Console.WriteLine(args.Data);
            };
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            
            newSong.CompletedAt = DateTime.Now;
            
            if (process.ExitCode == 0)
            {
                try
                {
                    // Attempt OpenAI-based improvement of UltraStar file
                    await TryImproveUltraStarWithOpenAI(newSong);
                }
                catch (Exception ex)
                {
                    // Do not fail the job if post-processing fails; just log it.
                    var msg = $"[UltraSinger][PostProcess] Improvement step failed: {ex.Message}";
                    Stdout.AppendLine(msg);
                    Console.WriteLine(msg);
                }
                return;
            }

            throw new ApplicationException($"Failed to process URL: {Stderr}");
        }

        private static async Task TryImproveUltraStarWithOpenAI(Song song)
        {
            if (!EnvironmentalValuesService.EnableOpenAICorrections)
            {
                Stdout.AppendLine("[UltraSinger][PostProcess] OpenAI corrections disabled by configuration. Skipping.");
                return;
            }
            
            Console.WriteLine("[UltraSinger][PostProcess] Attempting AI-based lyric improvement.");
            
            if (string.IsNullOrWhiteSpace(song.UltraStarTxtPath))
            {
                Stdout.AppendLine("[UltraSinger][PostProcess] UltraStar txt path not found in output. Skipping improvement.");
                return;
            }

            if (string.IsNullOrWhiteSpace(EnvironmentalValuesService.OpenAIKey))
            {
                Stdout.AppendLine("[UltraSinger][PostProcess] OpenAI API key not configured. Skipping.");
                return;
            }

            var localRoot = EnvironmentalValuesService.UltraStarDeluxeLocalLibraryPath.TrimEnd('\\', '/');
            var relative = song.UltraStarTxtPath!.Replace('/', System.IO.Path.DirectorySeparatorChar).TrimStart(System.IO.Path.DirectorySeparatorChar);
            var fullPath = System.IO.Path.Combine(localRoot, relative);

            if (!System.IO.File.Exists(fullPath))
            {
                Stdout.AppendLine($"[UltraSinger][PostProcess] UltraStar file not found at expected location: {fullPath}");
                return;
            }

            var originalTxt = System.IO.File.ReadAllText(fullPath);

            // Fetch lyrics using title
            var title = song.Title ?? song.FriendlyName;
            var lyrics = await LyricsService.TryGetLyricsAsync(title);
            if (string.IsNullOrWhiteSpace(lyrics))
            {
                var noLyrics = $"[UltraSinger][PostProcess] Could not fetch lyrics for '{title}'. Skipping improvement.";
                Stdout.AppendLine(noLyrics);
                Stderr.AppendLine(noLyrics);
                Console.WriteLine(noLyrics);
                return;
            }

            var improved = await OpenAIImproverService.ImproveUltraStarAsync(originalTxt, lyrics!, title, EnvironmentalValuesService.OpenAIKey!, EnvironmentalValuesService.OpenAIModel);

            if (string.IsNullOrWhiteSpace(improved))
            {
                var noResponse = "[UltraSinger][PostProcess] OpenAI returned no content. Skipping write.";
                Stdout.AppendLine(noResponse);
                Stderr.AppendLine(noResponse);
                Console.WriteLine(noResponse);
                return;
            }

            string targetPath;
            if (EnvironmentalValuesService.OverwriteUltraStarFile)
            {
                targetPath = fullPath;
            }
            else
            {
                var dir = System.IO.Path.GetDirectoryName(fullPath)!;
                var name = System.IO.Path.GetFileNameWithoutExtension(fullPath);
                var ext = System.IO.Path.GetExtension(fullPath);
                targetPath = System.IO.Path.Combine(dir, name + "-improved" + ext);
            }

            await System.IO.File.WriteAllTextAsync(targetPath, improved, Encoding.UTF8);
            var msg = $"[UltraSinger][PostProcess] Wrote improved UltraStar file: {targetPath}";
            Stdout.AppendLine(msg);
            Stderr.AppendLine(msg);
            Console.WriteLine(msg);
        }
    }

    public static string SanitisePath(string source) => source.Replace(" ", "\\ ");
}