namespace JitHubV3.Presentation;

#if __WASM__
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Uno.Foundation;
#endif

public partial class LoginViewModel : ObservableObject
{
    private IAuthenticationService _authentication;

    private INavigator _navigator;

    private IDispatcher _dispatcher;

#if __WASM__
    private readonly IConfiguration _configuration;
#endif


    public LoginViewModel(
#if __WASM__
        IConfiguration configuration,
#endif
        IDispatcher dispatcher,
        INavigator navigator,
        IAuthenticationService authentication)
    {
#if __WASM__
        _configuration = configuration;
#endif
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
#if __WASM__
    var startUriRaw = _configuration["WebAuthentication:LoginStartUri"];
        if (string.IsNullOrWhiteSpace(startUriRaw))
        {
            throw new InvalidOperationException("Missing configuration value WebAuthentication:LoginStartUri");
        }

        var uiOrigin = WebAssemblyRuntime.InvokeJS("globalThis.location && globalThis.location.origin ? globalThis.location.origin : ''");
        if (string.IsNullOrWhiteSpace(uiOrigin))
        {
            throw new InvalidOperationException("Unable to determine browser origin.");
        }

        var scope = "repo";
        var url = startUriRaw;
        url += (url.Contains('?', StringComparison.Ordinal) ? "&" : "?");
        url += "client=wasm-fullpage";
        url += "&scope=" + Uri.EscapeDataString(scope);
        url += "&redirect_uri=" + Uri.EscapeDataString(uiOrigin + "/");

        // Full-page navigation: no popup/broker.
        WebAssemblyRuntime.InvokeJS($"globalThis.location.assign('{url.Replace("'", "%27")}')");
        return;
#else
        var success = await _authentication.LoginAsync(_dispatcher);
        if (success)
        {
            await _navigator.NavigateViewModelAsync<MainViewModel>(this, qualifier: Qualifiers.ClearBackStack);
        }
#endif
    }

    public string Title { get; } = "Login";

    public ICommand Login { get; }

    public ICommand GoToMarkdownTest { get; }
}
