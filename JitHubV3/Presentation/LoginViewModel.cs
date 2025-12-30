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
        GoToDashboardLayoutTest = new AsyncRelayCommand(DoGoToDashboardLayoutTest);
        GoToDashboardTest = new AsyncRelayCommand(DoGoToDashboardTest);
    }

    private Task DoGoToMarkdownTest()
        => _navigator.NavigateViewModelAsync<MarkdownTestViewModel>(this);

    private Task DoGoToDashboardLayoutTest()
        => _navigator.NavigateViewModelAsync<DashboardLayoutTestViewModel>(this);

    private Task DoGoToDashboardTest()
        => _navigator.NavigateViewModelAsync<DashboardViewModel>(this);

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

    public ICommand GoToDashboardLayoutTest { get; }

    public ICommand GoToDashboardTest { get; }
}
