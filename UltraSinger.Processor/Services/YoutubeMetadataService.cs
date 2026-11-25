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
        var options = new OptionSet { Print = "title" };

        if (environmentalValues.YTDLPCookiesFromBrowser is { } cookiesFromBrowser)
        {
            options.CookiesFromBrowser = cookiesFromBrowser;
        }

        var result = await ytdlProcess.RunAsync(new[] { url }, options);
        ytdlProcess.OutputReceived -= OutputData;

        // yt-dlp's output ends in a newline (and the reader appends one per line), which
        // would otherwise end up in the stored title and every name derived from it.
        var title = OutputBuffer.ToString().Trim();

        return result == 0 && title.Length > 0
            ? title
            : null;
    }

    private void OutputData(object? raiser, DataReceivedEventArgs eventArgs)
    {
        OutputBuffer.AppendLine(eventArgs.Data);
    }
}
