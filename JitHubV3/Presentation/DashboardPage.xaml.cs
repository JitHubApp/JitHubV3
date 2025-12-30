namespace JitHubV3.Presentation;

using System.Numerics;

public sealed partial class DashboardPage : ActivatablePage
{
    public DashboardPage()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        SizeChanged += (_, __) => UpdateWidthState(useTransitions: true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateWidthState(useTransitions: false);
        ApplyComposeElevation();
    }

    private void ApplyComposeElevation()
    {
        if (ComposeBox is null)
        {
            return;
        }

        var elevation = ResolveDoubleResource("DashboardElevationCompose", fallback: 16);
        ComposeBox.Translation = new Vector3(0, 0, (float)elevation);
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
        return ResolveDoubleResource("DashboardGridToDeckMinWidth", fallback: 900);
    }

    private static double ResolveDoubleResource(string key, double fallback)
    {
        var resources = Application.Current?.Resources;
        if (resources is not null && resources.TryGetValue(key, out var value) && value is double width)
        {
            return width;
        }

        return fallback;
    }
}
