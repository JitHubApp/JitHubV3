using JitHub.Dashboard.Layouts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JitHubV3.Presentation;

public sealed partial class DashboardLayoutTestPage : Page
{
    public DashboardLayoutTestPage()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WireSwipeDelegate();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        WireSwipeDelegate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnwireSwipeDelegate();
    }

    private void WireSwipeDelegate()
    {
        if (DataContext is DashboardLayoutTestViewModel vm)
        {
            vm.SwipeRequestAsync = (cardId, direction, ct) => CardPresenter.SwipeAsync(cardId, direction, ct);
        }
    }

    private void UnwireSwipeDelegate()
    {
        if (DataContext is DashboardLayoutTestViewModel vm)
        {
            vm.SwipeRequestAsync = null;
        }
    }
}
