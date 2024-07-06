using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.AspNetCore.Components;
using UltraSingerUI.Constants;
using UltraSingerUI.Entities;
using UltraSingerUI.Services;
using Timer = System.Timers.Timer;

namespace UltraSingerUI.Components.Pages;

public partial class Home : ComponentBase
{
    [Inject]
    private SongProcessorService SongProcessorService { get; set; } = null!;
}