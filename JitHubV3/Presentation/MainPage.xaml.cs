namespace JitHubV3.Presentation;

public sealed partial class MainPage : ActivatablePage
{
    public MainPage()
    {
        this.InitializeComponent();
    }

    private async void OnRepoClicked(object sender, ItemClickEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.ClickedItem is JitHub.GitHub.Abstractions.Models.RepositorySummary repo)
        {
            await vm.OpenRepoAsync(repo);
        }
    }
}
