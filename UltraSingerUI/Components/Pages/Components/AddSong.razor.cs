using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using UltraSingerUI.Entities;
using UltraSingerUI.Services;

namespace UltraSingerUI.Components.Pages.Components;

public partial class AddSong : ComponentBase
{
    [Inject]
    private SongProcessorService SongProcessorService { get; set; } = null!;
    
    [Inject]
    private YoutubeMetadataService YoutubeMetadataService { get; set; } = null!;
    
    private string? SongUrl { get; set; }
    
    private bool IsAdding { get; set; }

    private Regex YTVideoRegex { get; } =
        new (
            "^((?:https?:)?\\/\\/)?((?:www|m)\\.)?((?:youtube\\.com|youtu.be))(\\/(?:[\\w\\-]+\\?v=|embed\\/|v\\/)?)([\\w\\-]+)(\\S+)?$");
    
    private bool EnableAddButton => SongUrl?.Length > 0 && !IsAdding && YTVideoRegex.Match(SongUrl).Success;
    
    private async void AddToQueue()
    {
        IsAdding = true;
        StateHasChanged();
        
        Console.WriteLine($"Triggered queue add for {SongUrl}");
        if (SongUrl == null)
        {
            return;
        }
        
        SongProcessorService.ProcessSong(new Song
        {
            Url = SongUrl,
            Title = await YoutubeMetadataService.GetTitleOfVideo(SongUrl),
        });

        SongUrl = string.Empty;
        IsAdding = false;
        StateHasChanged();
    }
}