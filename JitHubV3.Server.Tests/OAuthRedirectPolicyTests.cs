using Microsoft.Extensions.Options;
using JitHubV3.Server.Options;
using JitHubV3.Server.Services.Auth;

namespace JitHubV3.Server.Tests;

[TestFixture]
public sealed class OAuthRedirectPolicyTests
{
    [Test]
    public void TryGetAllowedRedirectUri_http_disallows_fragment()
    {
        var policy = CreatePolicy(
            allowedOrigins: ["http://localhost:5000"],
            allowedPaths: ["/"]);

        policy.TryGetAllowedRedirectUri("http://localhost:5000/#x").Should().BeNull();
    }

    [Test]
    public void TryGetAllowedRedirectUri_http_requires_allowlisted_origin()
    {
        var policy = CreatePolicy(
            allowedOrigins: ["http://localhost:5000"],
            allowedPaths: ["/"]);

        policy.TryGetAllowedRedirectUri("http://localhost:1234/").Should().BeNull();
        policy.TryGetAllowedRedirectUri("http://localhost:5000/").Should().NotBeNull();
    }

    [Test]
    public void TryGetAllowedRedirectUri_http_requires_allowlisted_path_when_configured()
    {
        var policy = CreatePolicy(
            allowedOrigins: ["http://localhost:5000"],
            allowedPaths: ["/", "/authentication-callback.html"]);

        policy.TryGetAllowedRedirectUri("http://localhost:5000/").Should().NotBeNull();
        policy.TryGetAllowedRedirectUri("http://localhost:5000/authentication-callback.html").Should().NotBeNull();
        policy.TryGetAllowedRedirectUri("http://localhost:5000/other").Should().BeNull();
    }

    [Test]
    public void TryGetAllowedRedirectUri_custom_scheme_allows_jithubv3()
    {
        var policy = CreatePolicy(
            allowedOrigins: Array.Empty<string>(),
            allowedPaths: Array.Empty<string>());

        policy.TryGetAllowedRedirectUri("jithubv3://auth?handoffCode=abc").Should().NotBeNull();
    }

    private static OAuthRedirectPolicy CreatePolicy(string[] allowedOrigins, string[] allowedPaths)
    {
        return new OAuthRedirectPolicy(Microsoft.Extensions.Options.Options.Create(new OAuthRedirectOptions
        {
            AllowedRedirectOrigins = allowedOrigins,
            AllowedRedirectPaths = allowedPaths,
            DefaultWasmRedirectUri = "http://localhost:5000/",
        }));
    }
}
