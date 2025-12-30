using Uno.Resizetizer;
using JitHubV3.Authentication;
using JitHubV3.Services.GitHub;
using JitHub.GitHub.Abstractions.Security;
using JitHub.GitHub.Abstractions.Services;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Polling;
using JitHub.GitHub.Octokit;

namespace JitHubV3;

public partial class App : Application
{
    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();

        // Ensure the broker callback URI matches the app's registered custom scheme.
        // See: https://platform.uno/docs/articles/features/web-authentication-broker.html
        // Windows uses ms-app:// (handled by default); non-Windows native platforms use custom schemes.
#if __IOS__ || __ANDROID__ || __MACOS__ || __MACCATALYST__
        Uno.WinRTFeatureConfiguration.WebAuthenticationBroker.DefaultReturnUri = new Uri(GitHubAuthFlow.DefaultCallbackUri);
#endif
    }

    protected Window? MainWindow { get; private set; }
    protected IHost? Host { get; private set; }

    // Exposed for library components (via reflection) that need access to DI services.
    // Avoids creating compile-time dependencies from shared libraries back to the app.
    public IServiceProvider? Services => Host?.Services;

#if WINDOWS
    private bool _pendingWindowActivation;
#endif

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
#if WINDOWS
        if (!await EnsureSingleWindowsInstanceAsync())
        {
            return;
        }

    // Auth flow uses loopback redirect on desktop; protocol activation not required.
#endif
        var builder = this.CreateBuilder(args)
            // Add navigation support for toolkit controls such as TabBar and NavigationView
            .UseToolkitNavigation()
            .Configure(host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseLogging(configure: (context, logBuilder) =>
                {
                    // Allow category-level log filtering via appsettings.json (Logging:LogLevel:...).
                    logBuilder.AddConfiguration(context.Configuration.GetSection("Logging"));

                    // Configure log levels for different categories of logging
                    logBuilder
                        .SetMinimumLevel(
                            context.HostingEnvironment.IsDevelopment() ?
                                LogLevel.Information :
                                LogLevel.Warning)

                        // Default filters for core Uno Platform namespaces
                        .CoreLogLevel(LogLevel.Warning);

                    // Uno Platform namespace filter groups
                    // Uncomment individual methods to see more detailed logging
                    //// Generic Xaml events
                    //logBuilder.XamlLogLevel(LogLevel.Debug);
                    //// Layout specific messages
                    //logBuilder.XamlLayoutLogLevel(LogLevel.Debug);
                    //// Storage messages
                    //logBuilder.StorageLogLevel(LogLevel.Debug);
                    //// Binding related messages
                    if (context.HostingEnvironment.IsDevelopment())
                    {
                        logBuilder.XamlBindingLogLevel(LogLevel.Debug);
                    }
                    //// Binder memory references tracking
                    //logBuilder.BinderMemoryReferenceLogLevel(LogLevel.Debug);
                    //// DevServer and HotReload related
                    //logBuilder.HotReloadCoreLogLevel(LogLevel.Information);
                    //// Debug JS interop

                }, enableUnoLogging: true)
                .UseSerilog(consoleLoggingEnabled: true, fileLoggingEnabled: true)
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<AppConfig>()
                )
                // Enable localization (see appsettings.json for supported languages)
                .UseLocalization()
                .UseAuthentication(auth =>
                    auth.AddCustom(custom =>
                    {
                        custom
                            .Login(async (services, dispatcher, credentials, ct) =>
                            {
                                var resultTokens = await GitHubAuthFlow.LoginAsync(services, credentials);

                                if (services.GetService<IGitHubTokenProvider>() is UnoTokenCacheGitHubTokenProvider provider)
                                {
                                    provider.UpdateFromTokens(resultTokens);
                                }

                                return resultTokens;
                            })
                            .Refresh((services, tokens, ct) =>
                            {
                                // GitHub OAuth tokens are typically long-lived; no refresh token.
                                // If we have an access token, consider the session still valid.
                                if (tokens is not null && tokens.TryGetValue("access_token", out var accessToken) && !string.IsNullOrWhiteSpace(accessToken))
                                {
                                    return ValueTask.FromResult<IDictionary<string, string>?>(tokens);
                                }

                                return ValueTask.FromResult<IDictionary<string, string>?>(new Dictionary<string, string>());
                            })
                            .Logout(async (services, dispatcher, tokenCache, tokens, ct) =>
                            {
                                await tokenCache.ClearAsync(ct);

                                if (services.GetService<IGitHubTokenProvider>() is UnoTokenCacheGitHubTokenProvider provider)
                                {
                                    provider.UpdateFromTokens(null);
                                }

                                return true;
                            });
                    }, name: "GitHub")
                )
                .ConfigureServices((context, services) =>
                {
                    // TODO: Register your services
                    //services.AddSingleton<IMyService, MyService>();

                    services.AddSingleton<IGitHubTokenProvider, UnoTokenCacheGitHubTokenProvider>();
                    services.AddSingleton<ISecretStore, PlatformSecretStore>();

                    services.AddSingleton<StatusBarViewModel>();

                    services.AddSingleton<IDashboardCardProvider, SelectedRepoDashboardCardProvider>();
                    services.AddSingleton<IDashboardCardProvider, RecentRepositoriesDashboardCardProvider>();

                    services.AddSingleton(sp =>
                    {
                        var baseUrl = context.Configuration["GitHub:ApiBaseUrl"];
                        Uri? apiBase = null;
                        if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed))
                        {
                            apiBase = parsed;
                        }

                        return new OctokitClientOptions(
                            ProductName: "JitHubV3",
                            ProductVersion: null,
                            ApiBaseAddress: apiBase);
                    });

                    services.AddSingleton<IOctokitClientFactory>(sp =>
                        new OctokitClientFactory(
                            sp.GetRequiredService<IGitHubTokenProvider>(),
                            sp.GetRequiredService<OctokitClientOptions>(),
                            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OctokitClientFactory>>()));

                    services.AddJitHubGitHubServices();
                })
                .UseNavigation(RegisterRoutes)
            );
        MainWindow = builder.Window;

#if DEBUG
        MainWindow.UseStudio();
#endif
        MainWindow.SetWindowIcon();

    #if WINDOWS
        // If we received a protocol activation before the window existed, activate it now.
        if (_pendingWindowActivation)
        {
            _pendingWindowActivation = false;
            TryActivateMainWindow();
        }
    #endif

        Host = await builder.NavigateAsync<Shell>
            (initialNavigate: async (services, navigator) =>
            {
                var auth = services.GetRequiredService<IAuthenticationService>();

                var authenticated = await auth.RefreshAsync();
                if (authenticated)
                {
                    await navigator.NavigateViewModelAsync<DashboardViewModel>(this, qualifier: Qualifiers.Nested);
                }
                else
                {
                    await navigator.NavigateViewModelAsync<LoginViewModel>(this, qualifier: Qualifiers.Nested);
                }
            });
    }

#if WINDOWS
    private static async Task<bool> EnsureSingleWindowsInstanceAsync()
    {
        var currentInstance = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent();
        var activatedArgs = currentInstance.GetActivatedEventArgs();

        var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("main");
        if (!mainInstance.IsCurrent)
        {
            await mainInstance.RedirectActivationToAsync(activatedArgs);
            return false;
        }

        return true;
    }

    // Protocol activation was previously used for OAuth handoff; desktop now uses a loopback redirect.

    private void TryActivateMainWindow()
    {
        var window = MainWindow;
        if (window is null)
        {
            return;
        }

        try
        {
            var dispatcher = window.DispatcherQueue;
            if (dispatcher.HasThreadAccess)
            {
                window.Activate();
            }
            else
            {
                dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        window.Activate();
                    }
                    catch (System.Runtime.InteropServices.COMException)
                    {
                        // If activation is not possible in the current state, ignore.
                    }
                });
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Ignore activation failures.
        }
    }
#endif

    private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
    {
        views.Register(
            new ViewMap(ViewModel: typeof(ShellViewModel)),
            new ViewMap<LoginPage, LoginViewModel>(),
            new ViewMap<DashboardPage, DashboardViewModel>(),
            new ViewMap<MainPage, MainViewModel>(),
            new DataViewMap<IssuesPage, IssuesViewModel, RepoRouteData>(),
            new DataViewMap<IssueConversationPage, IssueConversationViewModel, IssueConversationRouteData>(),
            new ViewMap<MarkdownTestPage, MarkdownTestViewModel>(),
            new ViewMap<DashboardLayoutTestPage, DashboardLayoutTestViewModel>(),
            new DataViewMap<SecondPage, SecondViewModel, Entity>()
        );

        routes.Register(
            new RouteMap("", View: views.FindByViewModel<ShellViewModel>(),
                Nested:
                [
                    new ("Login", View: views.FindByViewModel<LoginViewModel>()),
                    new ("Dashboard", View: views.FindByViewModel<DashboardViewModel>(), IsDefault:true),
                    new ("Main", View: views.FindByViewModel<MainViewModel>()),
                    new ("Issues", View: views.FindByViewModel<IssuesViewModel>()),
                    new ("IssueConversation", View: views.FindByViewModel<IssueConversationViewModel>()),
                    new ("MarkdownTest", View: views.FindByViewModel<MarkdownTestViewModel>()),
                    new ("DashboardLayoutTest", View: views.FindByViewModel<DashboardLayoutTestViewModel>()),
                    new ("Second", View: views.FindByViewModel<SecondViewModel>()),
                ]
            )
        );
    }
}
