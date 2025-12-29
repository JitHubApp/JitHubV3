using FluentAssertions;
using JitHub.GitHub.Abstractions.Security;
using NUnit.Framework;

namespace JitHub.GitHub.Tests;

public sealed class SecurityAbstractionsTests
{
    [Test]
    public async Task InMemoryGitHubTokenProvider_returns_null_when_not_set()
    {
        var provider = new InMemoryGitHubTokenProvider();
        var token = await provider.GetAccessTokenAsync(CancellationToken.None);
        token.Should().BeNull();
    }

    [Test]
    public async Task InMemoryGitHubTokenProvider_raises_TokenChanged_on_set()
    {
        var provider = new InMemoryGitHubTokenProvider();
        var raised = 0;
        provider.TokenChanged += (_, _) => Interlocked.Increment(ref raised);

        provider.SetToken("abc");
        (await provider.GetAccessTokenAsync(CancellationToken.None)).Should().Be("abc");
        raised.Should().Be(1);

        provider.SetToken(null);
        (await provider.GetAccessTokenAsync(CancellationToken.None)).Should().BeNull();
        raised.Should().Be(2);
    }

    [Test]
    public async Task InMemorySecretStore_set_get_remove_roundtrip()
    {
        var store = new InMemorySecretStore();
        var ct = CancellationToken.None;

        (await store.GetAsync("k", ct)).Should().BeNull();

        await store.SetAsync("k", "v", ct);
        (await store.GetAsync("k", ct)).Should().Be("v");

        await store.RemoveAsync("k", ct);
        (await store.GetAsync("k", ct)).Should().BeNull();
    }
}
