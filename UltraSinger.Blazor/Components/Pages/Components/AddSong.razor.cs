using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using UltraSinger.Blazor.Services;

namespace UltraSinger.Blazor.Components.Pages.Components;

public partial class AddSong : ComponentBase
{
    [Inject]
    private ProcessorApiClient Processor { get; set; } = null!;

    private string? SongUrl { get; set; }

    private bool IsAdding { get; set; }

    private Regex YTVideoRegex { get; } =
        new (
            "^((?:https?:)?\\/\\/)?((?:www|m)\\.)?((?:youtube\\.com|youtu.be))(\\/(?:[\\w\\-]+\\?v=|embed\\/|v\\/)?)([\\w\\-]+)(\\S+)?$");

    private bool EnableAddButton => SongUrl?.Length > 0 && !IsAdding && YTVideoRegex.Match(SongUrl).Success;

    private async Task AddToQueue()
    {
        if (string.IsNullOrWhiteSpace(SongUrl))
        {
            return;
        }

        IsAdding = true;
        StateHasChanged();

        try
        {
            // No title supplied — the processor resolves it with yt-dlp, which the UI no
            // longer has a copy of.
            await Processor.EnqueueAsync(SongUrl);
            SongUrl = string.Empty;
        }
        finally
        {
            IsAdding = false;
            StateHasChanged();
        }
    }
}
