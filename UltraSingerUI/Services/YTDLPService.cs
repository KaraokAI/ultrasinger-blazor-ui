using System.Diagnostics;
using System.Reflection;
using System.Text;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace UltraSingerUI.Services;

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