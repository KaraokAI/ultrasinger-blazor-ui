using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace UltraSinger.Processor.Services;

public class YTDLPService
{
    private static bool FirstRun { get; set; }

    public async Task<string?> GetTitleOfVideo(string url)
    {
        var ytdl = new YoutubeDL();

        if (FirstRun)
        {
            await ytdl.RunUpdate();
            FirstRun = false;
        }

        var result = await ytdl.RunWithOptions(url, new OptionSet()
        {
            Print = "all",
            IgnoreErrors = true,
            Verbose = true,
        });

        return result.Success && !string.IsNullOrWhiteSpace(result.Data)
            ? result.Data
            : null;
    }
}
