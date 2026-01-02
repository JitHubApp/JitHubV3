using System.ComponentModel;

namespace JitHubV3.Presentation;

public sealed class CoreStatusBarExtension : IStatusBarExtension
{
    private readonly StatusBarViewModel _statusBar;

    public CoreStatusBarExtension(StatusBarViewModel statusBar)
    {
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _statusBar.PropertyChanged += OnStatusBarPropertyChanged;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<StatusBarSegment> Segments
    {
        get
        {
            var segments = new List<StatusBarSegment>(capacity: 2);

            if (!string.IsNullOrWhiteSpace(_statusBar.FreshnessLabel))
            {
                segments.Add(new StatusBarSegment(
                    Id: "data-freshness",
                    Text: _statusBar.FreshnessLabel,
                    IsVisible: true,
                    Priority: 100));
            }

            if (!string.IsNullOrWhiteSpace(_statusBar.LastUpdatedLabel))
            {
                segments.Add(new StatusBarSegment(
                    Id: "last-updated",
                    Text: _statusBar.LastUpdatedLabel,
                    IsVisible: true,
                    Priority: 90));
            }

            return segments;
        }
    }

    private void OnStatusBarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StatusBarViewModel.Freshness)
            or nameof(StatusBarViewModel.LastUpdatedAt)
            or nameof(StatusBarViewModel.FreshnessLabel)
            or nameof(StatusBarViewModel.LastUpdatedLabel))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
