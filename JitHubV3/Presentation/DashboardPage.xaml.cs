namespace JitHubV3.Presentation;

public sealed partial class DashboardPage : ActivatablePage
{
    public DashboardPage()
    {
        InitializeComponent();

        Loaded += (_, __) => UpdateWidthState(useTransitions: false);
        SizeChanged += (_, __) => UpdateWidthState(useTransitions: true);
    }

    private void UpdateWidthState(bool useTransitions)
    {
        var threshold = ResolveGridToDeckMinWidth();
        var state = ActualWidth >= threshold ? "Wide" : "Narrow";

        if (StateHost is not null)
        {
            VisualStateManager.GoToState(StateHost, state, useTransitions);
        }
    }

    private static double ResolveGridToDeckMinWidth()
    {
        var resources = Application.Current?.Resources;
        if (resources is not null && resources.TryGetValue("DashboardGridToDeckMinWidth", out var value) && value is double width)
        {
            return width;
        }

        // Fallback: keep a sensible value if resources are unavailable.
        return 900;
    }
}
