using JitHubV3.Server.Services.Auth;

namespace JitHubV3.Server.Tests;

[TestFixture]
public sealed class OAuthScopesTests
{
    [Test]
    public void NormalizeScope_defaults_to_repo()
    {
        OAuthScopes.NormalizeScope(null).Should().Be("repo");
        OAuthScopes.NormalizeScope("").Should().Be("repo");
        OAuthScopes.NormalizeScope("   ").Should().Be("repo");
    }

    [Test]
    public void NormalizeScope_accepts_commas_semicolons_spaces_and_dedupes()
    {
        OAuthScopes.NormalizeScope("repo, user:email; repo").Should().Be("repo user:email");
    }

    [Test]
    public void SplitScopes_splits_by_commas_and_spaces_and_dedupes()
    {
        OAuthScopes.SplitScopes("repo, user:email repo").Should().BeEquivalentTo(["repo", "user:email"]);
    }
}
