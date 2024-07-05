using System.Diagnostics;
using System.Text;

namespace UltraSingerUI.Services;

public class YTDLPService
{
    public async Task<string?> GetTitleOfVideo(string url)
    {
        Process ytdlp = new Process();
        StringBuilder outputStringBuilder = new StringBuilder();
        ytdlp.OutputDataReceived += (sender, eventArgs) =>
        {
            Console.WriteLine(eventArgs.Data);
            outputStringBuilder.AppendLine(eventArgs.Data);
        };
        
        ytdlp.StartInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            Arguments = $"--print title {url}",
            RedirectStandardOutput = true,
        };

        ytdlp.Start();
        ytdlp.BeginOutputReadLine();
        
        await ytdlp.WaitForExitAsync();
        return outputStringBuilder.ToString();
    }
}