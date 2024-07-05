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
        var jobId = BackgroundJob.Enqueue(Queues.SongQueue, () => InternalWorker.Process(newSong));
        newSong.JobId = jobId;
        SongQueue.SongList.Push(newSong);
    }

    public bool HasProcessedSong(Song existingSong)
    {
        var job = StorageApi.GetJobData(existingSong.JobId);
        return job.State
    }

    public IEnumerable<Song> GetProcessedSongList() => SongQueue.SongList;
    
    private static class InternalWorker
    {
        public static void Process(Song newSong)
        {
            Console.WriteLine($"Received request for {newSong.FriendlyName}");
            Task.Delay(TimeSpan.FromSeconds(10)).Wait();
            Console.WriteLine($"Completed request for {newSong.FriendlyName}");
        }
    }
}