using Microsoft.AspNetCore.Components;
using UltraSinger.Blazor.Services;

namespace UltraSinger.Blazor.Components.Layout;

public partial class MainLayout
{
    [Inject]
    private BlazorSongQueueService SongQueueService { get; set; } = null!;
}