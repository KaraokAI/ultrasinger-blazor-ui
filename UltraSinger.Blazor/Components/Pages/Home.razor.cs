namespace UltraSinger.Blazor.Components.Pages;

public partial class Home
{
    private enum TabType
    {
        Search,
        Library,
        Downloads
    }

    private TabType ActiveTab { get; set; } = TabType.Search;
    private bool ShowAddModal { get; set; } = false;

    private void SetActiveTab(TabType tab)
    {
        ActiveTab = tab;
    }

    private void ToggleAddModal()
    {
        ShowAddModal = !ShowAddModal;
    }
}