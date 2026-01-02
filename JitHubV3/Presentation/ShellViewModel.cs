namespace JitHubV3.Presentation;

public class ShellViewModel
{
    private readonly IAuthenticationService _authentication;

    private readonly StatusBarComposer _statusBarComposer;


    private readonly INavigator _navigator;
    public StatusBarViewModel StatusBar { get; }

    public ShellViewModel(
        IAuthenticationService authentication,
        INavigator navigator,
        StatusBarViewModel statusBar,
        StatusBarComposer statusBarComposer)
    {
        _navigator = navigator;
        _authentication = authentication;
        StatusBar = statusBar;
        _statusBarComposer = statusBarComposer;
        _authentication.LoggedOut += LoggedOut;
    }

    private async void LoggedOut(object? sender, EventArgs e)
    {
        StatusBar.Clear();
        await _navigator.NavigateViewModelAsync<LoginViewModel>(this, qualifier: Qualifiers.ClearBackStack);
    }
}
