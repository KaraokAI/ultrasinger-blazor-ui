using System.Diagnostics;
using System.Reflection;
using System.Text;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace UltraSingerUI.Services;

public class YoutubeMetadataService
{
    private EnvironmentalValuesService EnvironmentalValuesService { get; set; } = new();
    
    private static bool FirstRun { get; set; }

    private StringBuilder OutputBuffer { get; set; } = new();
    
    public async Task<string?> GetTitleOfVideo(string url)
    {
        var ytdl = new YoutubeDL();
        // ytdl.YoutubeDLPath = "/opt/homebrew/bin/yt-dlp";
        ytdl.YoutubeDLPath = EnvironmentalValuesService.YTDLPPath;

        if (FirstRun)
        {
            await ytdl.RunUpdate();
            FirstRun = false;
        }

        var ytdlProcess = new YoutubeDLProcess(ytdl.YoutubeDLPath);
        OutputBuffer = new StringBuilder();
        
        ytdlProcess.OutputReceived += OutputData;
        var result = await ytdlProcess.RunAsync(new []{ url }, new OptionSet() { Print = "title"} );
        ytdlProcess.OutputReceived -= OutputData;
        
        return result == 0
            ? OutputBuffer.ToString()
            : null;
    }

    private void OutputData(object? raiser, DataReceivedEventArgs eventArgs)
    {
        OutputBuffer.AppendLine(eventArgs.Data);
    }
}