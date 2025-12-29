namespace JitHubV3.Presentation;

public sealed partial class IssuesPage : ActivatablePage
{
    private ScrollViewer? _scrollViewer;

    public IssuesPage()
    {
        InitializeComponent();
    }

    private void OnIssuesListLoaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is not null)
        {
            return;
        }

        if (IssuesList is null)
        {
            return;
        }

        _scrollViewer = FindScrollViewer(IssuesList);
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged += OnListViewScrollChanged;
        }
    }

    private void OnIssuesListUnloaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged -= OnListViewScrollChanged;
            _scrollViewer = null;
        }
    }

    private void OnListViewScrollChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (DataContext is not IssuesViewModel vm)
        {
            return;
        }

        var sv = _scrollViewer;
        if (sv is null)
        {
            return;
        }

        // Trigger incremental loading when near the bottom.
        // Threshold in DIPs: small enough to feel immediate, large enough to avoid repeated triggering.
        const double threshold = 240;
        var remaining = sv.ExtentHeight - (sv.VerticalOffset + sv.ViewportHeight);
        if (remaining <= threshold)
        {
            _ = vm.TryLoadNextPageAsync();
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
        {
            return sv;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindScrollViewer(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private async void OnIssueClicked(object sender, ItemClickEventArgs e)
    {
        if (DataContext is not IssuesViewModel vm)
        {
            return;
        }

        if (e.ClickedItem is JitHub.GitHub.Abstractions.Models.IssueSummary issue)
        {
            await vm.OpenIssueAsync(issue);
        }
    }
}
