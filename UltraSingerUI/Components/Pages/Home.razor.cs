using Microsoft.AspNetCore.Components;
using UltraSingerUI.Services;

namespace UltraSingerUI.Components.Pages;

public partial class Home : ComponentBase
{
    [Inject]
    private SongProcessorService SongProcessorService { get; set; } = null!;
}