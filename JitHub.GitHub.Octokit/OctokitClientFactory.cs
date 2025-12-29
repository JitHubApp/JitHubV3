using JitHub.GitHub.Abstractions.Security;
using Microsoft.Extensions.Logging;
using Octokit;

namespace JitHub.GitHub.Octokit;

public sealed class OctokitClientFactory : IOctokitClientFactory
{
    private static readonly Uri DefaultApiBaseAddress = new("https://api.github.com/");

    private readonly IGitHubTokenProvider _tokenProvider;
    private readonly OctokitClientOptions _options;
    private readonly ILogger<OctokitClientFactory> _logger;

    public OctokitClientFactory(IGitHubTokenProvider tokenProvider, OctokitClientOptions options, ILogger<OctokitClientFactory> logger)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ProductName))
        {
            throw new ArgumentException("ProductName must not be empty.", nameof(options));
        }
    }

    public async ValueTask<GitHubClient> CreateAsync(CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Octokit client creation failed: missing access token");
            throw new InvalidOperationException("Missing access token. Please login first.");
        }

        var userAgent = _options.ProductVersion is null
            ? new ProductHeaderValue(_options.ProductName)
            : new ProductHeaderValue(_options.ProductName, _options.ProductVersion);

        var baseAddress = _options.ApiBaseAddress ?? DefaultApiBaseAddress;

        _logger.LogInformation("Creating Octokit client (BaseAddress={BaseAddress})", baseAddress);
        var client = new GitHubClient(userAgent, baseAddress)
        {
            Credentials = new Credentials(token)
        };

        _options.OnClientCreated?.Invoke(new OctokitClientCreatedEvent(baseAddress, _options.ProductName, _options.ProductVersion));
        return client;
    }
}
