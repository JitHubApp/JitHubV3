using CommunityToolkit.Mvvm.ComponentModel;

namespace JitHubV3.Presentation;

public sealed partial class DashboardViewModel : ObservableObject, IActivatableViewModel
{
    private readonly StatusBarViewModel _statusBar;

    public DashboardViewModel(StatusBarViewModel statusBar)
    {
        _statusBar = statusBar;
    }

    public string Title { get; } = "Dashboard";

    public Task ActivateAsync()
    {
        _statusBar.Set(
            message: "Dashboard",
            isBusy: false,
            isRefreshing: false,
            freshness: DataFreshnessState.Unknown,
            lastUpdatedAt: null);

        return Task.CompletedTask;
    }

    public void Deactivate()
    {
        // No subscriptions to tear down yet.
    }
}
