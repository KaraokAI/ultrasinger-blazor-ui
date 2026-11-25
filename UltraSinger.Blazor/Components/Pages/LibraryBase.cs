using Microsoft.AspNetCore.Components;
using UltraSinger.Blazor.Components.Layout;
using UltraSinger.Blazor.Services;

namespace UltraSinger.Blazor.Components.Pages;

[Layout(typeof(MainLayout))]
public abstract class LibraryBase : ComponentBase
{
    [Inject]
    protected BlazorSongQueueService SongQueueService { get; set; } = null!;
}