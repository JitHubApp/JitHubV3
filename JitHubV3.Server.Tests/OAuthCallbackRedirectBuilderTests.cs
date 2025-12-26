using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.WebUtilities;
using JitHubV3.Server.Services;
using JitHubV3.Server.Services.Auth;

namespace JitHubV3.Server.Tests;

[TestFixture]
public sealed class OAuthCallbackRedirectBuilderTests
{
    [Test]
    public void BuildRedirect_wasm_fullpage_returns_fragment_with_token()
    {
        var store = new AuthHandoffStore();
        var builder = new OAuthCallbackRedirectBuilder(store);

        var stateEntry = new OAuthStateStore.OAuthStateEntry(
            Client: AuthClientKinds.WasmFullPage,
            RedirectUri: "http://localhost:5000/",
            Scope: "repo",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10));

        var redirect = builder.BuildRedirect(stateEntry, new OAuthTokenResult("tkn", "bearer", "repo"));

        redirect.Should().StartWith("http://localhost:5000/#");
        redirect.Should().Contain("access_token=tkn");
        redirect.Should().Contain("token_type=bearer");
        redirect.Should().Contain("scope=repo");

        store.TryConsume("anything", out _).Should().BeFalse();
    }

    [Test]
    public void BuildRedirect_default_mode_stores_handoff_and_returns_query_param()
    {
        var store = new AuthHandoffStore();
        var builder = new OAuthCallbackRedirectBuilder(store);

        var stateEntry = new OAuthStateStore.OAuthStateEntry(
            Client: AuthClientKinds.Wasm,
            RedirectUri: "http://localhost:5000/",
            Scope: "repo",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10));

        var redirect = builder.BuildRedirect(stateEntry, new OAuthTokenResult("tkn", "bearer", "repo"));

        redirect.Should().StartWith("http://localhost:5000/");
        redirect.Should().Contain("handoffCode=");

        var uri = new Uri(redirect);
        var query = QueryHelpers.ParseQuery(uri.Query);
        query.ContainsKey("handoffCode").Should().BeTrue();

        var handoffCode = query["handoffCode"].ToString();
        handoffCode.Should().NotBeNullOrWhiteSpace();

        store.TryConsume(handoffCode, out var entry).Should().BeTrue();
        entry.Should().NotBeNull();
        entry!.AccessToken.Should().Be("tkn");
        entry.TokenType.Should().Be("bearer");
        entry.Scope.Should().Be("repo");
    }

    [Test]
    public void BuildRedirect_windows_relative_redirect_is_supported()
    {
        var store = new AuthHandoffStore();
        var builder = new OAuthCallbackRedirectBuilder(store);

        var stateEntry = new OAuthStateStore.OAuthStateEntry(
            Client: AuthClientKinds.Windows,
            RedirectUri: "/auth/complete?client=windows",
            Scope: "repo",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10));

        var redirect = builder.BuildRedirect(stateEntry, new OAuthTokenResult("tkn", "bearer", "repo"));

        redirect.Should().StartWith("/auth/complete?client=windows");
        redirect.Should().Contain("handoffCode=");
    }
}
