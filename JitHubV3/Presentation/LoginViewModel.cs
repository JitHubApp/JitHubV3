namespace JitHubV3.Presentation;

public partial class LoginViewModel : ObservableObject
{
    private IAuthenticationService _authentication;

    private INavigator _navigator;

    private IDispatcher _dispatcher;

    public LoginViewModel(
        IDispatcher dispatcher,
        INavigator navigator,
        IAuthenticationService authentication)
    {
        _dispatcher = dispatcher;
        _navigator = navigator;
        _authentication = authentication;
        Login = new AsyncRelayCommand(DoLogin);
        GoToMarkdownTest = new AsyncRelayCommand(DoGoToMarkdownTest);
    }

    private Task DoGoToMarkdownTest()
        => _navigator.NavigateViewModelAsync<MarkdownTestViewModel>(this);

    private async Task DoLogin()
    {
        var success = await _authentication.LoginAsync(_dispatcher);
        if (success)
        {
            await _navigator.NavigateViewModelAsync<DashboardViewModel>(this, qualifier: Qualifiers.ClearBackStack);
        }
    }

    public string Title { get; } = "Login";

    public ICommand Login { get; }

    public ICommand GoToMarkdownTest { get; }
}
