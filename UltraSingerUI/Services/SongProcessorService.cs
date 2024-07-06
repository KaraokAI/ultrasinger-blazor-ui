using System.Diagnostics;
using System.Text;
using Hangfire;
using Hangfire.Storage;
using UltraSingerUI.Constants;
using UltraSingerUI.Entities;

namespace UltraSingerUI.Services;

public class SongProcessorService
{
    public SongProcessorService(SongQueue songQueue)
    {
        StorageApi = JobStorage.Current.GetConnection();
        SongQueue = songQueue;
    }
    
    private SongQueue SongQueue { get; set; }
    
    private IStorageConnection StorageApi { get; }
    
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
        
        public static void Process(Song newSong)
        {
            Stdout = new();
            Stderr = new();
            
            var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = $"C:\\Users\\clott\\.pyenv\\pyenv-win\\bin\\pyenv.ps1 exec python UltraSinger.py -i {newSong.Url} -o 'F\\UltraStar Deluxe\\songs'",
                WorkingDirectory = "C:\\Users\\clott\\UltraSinger\\src\\",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Environment = { { "PYENV_VERSION", "3.10.11" } }
            };

            process.OutputDataReceived += (sender, args) => Stdout.Insert(0, args.Data + '\n');
            process.ErrorDataReceived += (sender, args) => Stderr.Insert(0, args.Data + '\n');
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode == 0) return; 

            throw new ApplicationException($"Failed to process URL: {Stderr}");
        }
    }
}