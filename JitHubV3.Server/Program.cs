using Serilog;
using Microsoft.AspNetCore.Http.Extensions;
using JitHubV3.Server.Apis;
using JitHubV3.Server.Options;
using JitHubV3.Server.Services;
using JitHubV3.Server.Services.Auth;

try
{
    Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine("App_Data", "Logs", "log.txt"))
            .CreateLogger();
    var builder = WebApplication.CreateBuilder(args);
    SerilogHostBuilderExtensions.UseSerilog(builder.Host);

    // Visual Studio/IIS Express can run with different working directories.
    // Explicitly add user-secrets in Development so local GitHub OAuth config is picked up reliably.
    if (builder.Environment.IsDevelopment())
    {
        builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
    }

    // Configure the RouteOptions to use lowercase URLs
    builder.Services.Configure<RouteOptions>(options =>
        options.LowercaseUrls = true);

    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        // Include XML comments for all included assemblies
        Directory.EnumerateFiles(AppContext.BaseDirectory, "*.xml")
            .Where(x => x.Contains("JitHubV3")
                && File.Exists(Path.Combine(
                    AppContext.BaseDirectory,
                    $"{Path.GetFileNameWithoutExtension(x)}.dll")))
            .ToList()
            .ForEach(path => c.IncludeXmlComments(path));
    });

    builder.Services.AddOptions<GitHubOAuthOptions>()
        .Bind(builder.Configuration.GetSection("GitHub"));

    builder.Services.AddOptions<OAuthRedirectOptions>()
        .Bind(builder.Configuration.GetSection("OAuthRedirect"));

    builder.Services.AddSingleton<OAuthStateStore>();
    builder.Services.AddSingleton<AuthHandoffStore>();

    builder.Services.AddSingleton<IOAuthRedirectPolicy, OAuthRedirectPolicy>();
    builder.Services.AddSingleton<IGitHubOAuthClient, GitHubOAuthClient>();
    builder.Services.AddSingleton<OAuthCallbackRedirectBuilder>();

    builder.Services.AddHttpClient("GitHubOAuth", client =>
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("JitHubV3.Server");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    });

    builder.Services.AddHttpClient("GitHubApi", client =>
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("JitHubV3.Server");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    });

    builder.Services
        .AddRazorComponents()
        .AddInteractiveServerComponents();

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // For local WASM hosting + UI tests we intentionally allow HTTP.
    // Redirecting HTTP->HTTPS breaks when the dev cert isn't trusted.
    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }

    app.UseStaticFiles();
    app.UseAntiforgery();

    app.MapWeatherApi();
    app.MapAuthApi();

    app.MapRazorComponents<global::JitHubV3.Server.Components.App>()
        .AddInteractiveServerRenderMode();

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
#if DEBUG
    if (System.Diagnostics.Debugger.IsAttached)
    {
        System.Diagnostics.Debugger.Break();
    }
#endif
}
finally
{
    Log.CloseAndFlush();
}
