namespace JitHubV3.Presentation;

using System.Collections.ObjectModel;
using System.Linq;

public partial class MainViewModel : ObservableObject
{
    private IAuthenticationService _authentication;

    private readonly Services.GitHub.IGitHubApi _gitHubApi;

    private INavigator _navigator;

    [ObservableProperty]
    private string? name;

    [ObservableProperty]
    private string? repoStatus;

    public ObservableCollection<string> PrivateRepos { get; } = new();

    public MainViewModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo,
        IAuthenticationService authentication,
        Services.GitHub.IGitHubApi gitHubApi,
        INavigator navigator)
    {
        _navigator = navigator;
        _authentication = authentication;
        _gitHubApi = gitHubApi;
        Title = "Main";
        Title += $" - {localizer["ApplicationName"]}";
        Title += $" - {appInfo?.Value?.Environment}";
        GoToSecond = new AsyncRelayCommand(GoToSecondView);
        GoToMarkdownTest = new AsyncRelayCommand(GoToMarkdownTestView);
        LoadPrivateRepos = new AsyncRelayCommand(DoLoadPrivateRepos);
        Logout = new AsyncRelayCommand(DoLogout);
    }
    public string? Title { get; }

    public ICommand GoToSecond { get; }

    public ICommand GoToMarkdownTest { get; }

    public ICommand LoadPrivateRepos { get; }

    public ICommand Logout { get; }

    private async Task GoToSecondView()
    {
        await _navigator.NavigateViewModelAsync<SecondViewModel>(this, data: new Entity(Name!));
    }

    private async Task GoToMarkdownTestView()
    {
        await _navigator.NavigateViewModelAsync<MarkdownTestViewModel>(this);
    }

    private async Task DoLoadPrivateRepos()
    {
        RepoStatus = "Loading...";
        PrivateRepos.Clear();

        try
        {
            var repos = await _gitHubApi.GetMyPrivateReposAsync(CancellationToken.None);
            foreach (var repo in repos.Take(10))
            {
                PrivateRepos.Add(repo.FullName);
            }

            RepoStatus = repos.Count switch
            {
                0 => "No private repos found.",
                _ => $"Loaded {repos.Count} private repos (showing first {Math.Min(10, repos.Count)})."
            };
        }
        catch (Exception ex)
        {
            RepoStatus = $"Failed to load repos: {ex.Message}";
        }
    }

    public async Task DoLogout(CancellationToken token)
    {
        try
        {
            await _authentication.LogoutAsync(token);
        }
        finally
        {
            RepoStatus = null;
            PrivateRepos.Clear();
            await _navigator.NavigateViewModelAsync<LoginViewModel>(this, qualifier: Qualifiers.ClearBackStack);
        }
    }
}
