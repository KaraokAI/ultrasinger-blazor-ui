using Microsoft.AspNetCore.Components;
using UltraSinger.Blazor.Services;
using UltraSinger.Contracts;

namespace UltraSinger.Blazor.Components.Pages.Components;

public partial class SongQueueView : ComponentBase, IDisposable
{
    [Inject]
    private BlazorSongQueueService SongQueueService { get; set; } = null!;

    private IReadOnlyList<SongQueueItem> Items => SongQueueService.Items;

    protected override void OnInitialized()
    {
        SongQueueService.OnQueueChanged += HandleQueueChanged;
    }

    private void HandleQueueChanged()
    {
        InvokeAsync(StateHasChanged);
    }

    private void MoveUp(Guid id)
    {
        SongQueueService.MoveUp(id);
    }

    private void MoveDown(Guid id)
    {
        SongQueueService.MoveDown(id);
    }

    private void Remove(Guid id)
    {
        SongQueueService.Remove(id);
    }

    private void ToggleSung(SongQueueItem item)
    {
        if (item.IsSung)
        {
            SongQueueService.UnmarkSung(item.Id);
        }
        else
        {
            SongQueueService.MarkSung(item.Id);
        }
    }

    private void ClearQueue()
    {
        SongQueueService.Clear();
    }

    public void Dispose()
    {
        SongQueueService.OnQueueChanged -= HandleQueueChanged;
    }
}
