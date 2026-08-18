using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using UltraSinger.Blazor.Services;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Components.Pages.Components;

public partial class AddSong : ComponentBase
{
    [Inject]
    private ProcessorApiClient Processor { get; set; } = null!;

    [Inject]
    private UsdbService UsdbService { get; set; } = null!;

    private string? SongUrl { get; set; }

    private bool IsAdding { get; set; }

    private Regex YTVideoRegex { get; } =
        new (
            "^((?:https?:)?\\/\\/)?((?:www|m)\\.)?((?:youtube\\.com|youtu.be))(\\/(?:[\\w\\-]+\\?v=|embed\\/|v\\/)?)([\\w\\-]+)(\\S+)?$");

    private Regex UsdbRegex { get; } =
        new (
            @"(?:usdb\.(?:animux\.de|eu).*?[?&]id=(\d+)|^(\d+)$)", RegexOptions.IgnoreCase);

    private bool EnableAddButton
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SongUrl) || IsAdding) return false;
            var trimmed = SongUrl.Trim();
            return YTVideoRegex.Match(trimmed).Success || UsdbRegex.Match(trimmed).Success;
        }
    }

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
            var trimmed = SongUrl.Trim();
            var usdbMatch = UsdbRegex.Match(trimmed);
            if (usdbMatch.Success)
            {
                var idStr = !string.IsNullOrEmpty(usdbMatch.Groups[1].Value) ? usdbMatch.Groups[1].Value : usdbMatch.Groups[2].Value;
                if (int.TryParse(idStr, out var songId))
                {
                    var request = await UsdbService.TryBuildEnqueueRequestAsync(songId);
                    if (request != null)
                    {
                        await Processor.EnqueueAsync(request);
                    }
                    SongUrl = string.Empty;
                    return;
                }
            }

            // Standard YouTube URL fallback
            await Processor.EnqueueAsync(trimmed);
            SongUrl = string.Empty;
        }
        finally
        {
            IsAdding = false;
            StateHasChanged();
        }
    }
}
