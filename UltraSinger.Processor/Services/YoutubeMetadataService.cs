using System.Diagnostics;
using System.Text;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace UltraSinger.Processor.Services;

public class YoutubeMetadataService(EnvironmentalValuesService environmentalValues)
{
    private static bool FirstRun { get; set; }

    private StringBuilder OutputBuffer { get; set; } = new();

    public async Task<string?> GetTitleOfVideo(string url)
    {
        var ytdl = new YoutubeDL
        {
            YoutubeDLPath = environmentalValues.YTDLPPath
        };

        if (FirstRun)
        {
            await ytdl.RunUpdate();
            FirstRun = false;
        }

        var ytdlProcess = new YoutubeDLProcess(ytdl.YoutubeDLPath);
        OutputBuffer = new StringBuilder();

        ytdlProcess.OutputReceived += OutputData;
        var result = await ytdlProcess.RunAsync(new[] { url }, new OptionSet { Print = "title", CookiesFromBrowser = "firefox" });
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
