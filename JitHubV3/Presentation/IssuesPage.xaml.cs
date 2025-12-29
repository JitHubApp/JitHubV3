namespace JitHubV3.Presentation;

public sealed partial class IssuesPage : ActivatablePage
{
    public IssuesPage()
    {
        InitializeComponent();
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
