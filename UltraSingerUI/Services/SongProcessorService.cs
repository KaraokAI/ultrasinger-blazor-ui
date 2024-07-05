using System.Diagnostics;
using System.Text;
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
        
        public static void Process(Song newSong)
        {
            Stdout = new();
            Stderr = new();
            
            var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = "asdf",
                // Arguments = $"exec python {EnvironmentalValuesService.UltraSingerPath}/src/UltraSinger.py -i {newSong.Url} -o \"/Users/sacredskull/UltraStar Deluxe Library\" --language {EnvironmentalValuesService.KaraokeLanguage}",
                Arguments = $"exec python {EnvironmentalValuesService.UltraSingerPath}/src/UltraSinger.py -i {newSong.Url} -o \"{EnvironmentalValuesService.UltraStarDeluxeLocalLibraryPath}\" --language {EnvironmentalValuesService.KaraokeLanguage}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Environment =
                // {
                //     { "PYENV_VERSION", "3.10.11" },
                //     { "ASDF_PYTHON_VERSION", "3.10.11" },
                //     { "PYTORCH_ENABLE_MPS_FALLBACK", "1" }
                // }
            };

            foreach (var envVar in EnvironmentalValuesService.GetUltraSingerAdditionalEnvironmentVariables())
            {
                process.StartInfo.Environment.Add(envVar.Item1, envVar.Item2);
            }

            process.OutputDataReceived += (sender, args) =>
            {
                Stdout.Insert(0, args.Data + '\n');
                Console.WriteLine(args.Data);
            };
            process.ErrorDataReceived += (sender, args) =>
            {
                Stdout.Insert(0, args.Data + '\n');
                Stderr.Insert(0, args.Data + '\n');
                Console.WriteLine(args.Data);
            };
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            
            newSong.CompletedAt = DateTime.Now;
            
            if (process.ExitCode == 0)
            {
                return;
            }

            throw new ApplicationException($"Failed to process URL: {Stderr}");
        }
    }
}