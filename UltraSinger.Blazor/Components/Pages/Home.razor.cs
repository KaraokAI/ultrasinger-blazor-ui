using Microsoft.AspNetCore.Components;
using UltraSinger.Blazor.Services;

namespace UltraSinger.Blazor.Components.Pages;

public partial class Home : ComponentBase, IDisposable
{
    private enum TabType
    {
        Search,
        Library,
        Queue,
        Downloads
    }

    [Inject]
    private BlazorSongQueueService SongQueueService { get; set; } = null!;

    private TabType ActiveTab { get; set; } = TabType.Search;
    private bool ShowAddModal { get; set; } = false;

    protected override void OnInitialized()
    {
        SongQueueService.OnQueueChanged += HandleQueueChanged;
    }

    private void HandleQueueChanged()
    {
        InvokeAsync(StateHasChanged);
    }

    private void SetActiveTab(TabType tab)
    {
        ActiveTab = tab;
    }

    private void ToggleAddModal()
    {
        ShowAddModal = !ShowAddModal;
    }

    public void Dispose()
    {
        SongQueueService.OnQueueChanged -= HandleQueueChanged;
    }
}