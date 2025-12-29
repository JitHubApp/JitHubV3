using FluentAssertions;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Security;
using JitHub.GitHub.Octokit;
using JitHub.GitHub.Octokit.Mapping;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace JitHub.GitHub.Tests;

public sealed class OctokitAdapterTests
{
    [Test]
    public async Task OctokitClientFactory_sets_user_agent_base_address_and_token()
    {
        var tokenProvider = new InMemoryGitHubTokenProvider();
        tokenProvider.SetToken("t");

        var created = new List<OctokitClientCreatedEvent>();
        var options = new OctokitClientOptions(
            ProductName: "JitHubV3",
            ProductVersion: "test",
            ApiBaseAddress: new Uri("https://api.github.com/"),
            OnClientCreated: e => created.Add(e));

        var factory = new OctokitClientFactory(tokenProvider, options, NullLogger<OctokitClientFactory>.Instance);
        var client = await factory.CreateAsync(CancellationToken.None);

        client.Credentials.Should().NotBeNull();
        client.Connection.BaseAddress.Should().Be(options.ApiBaseAddress);

        created.Should().ContainSingle();
        created[0].ApiBaseAddress.Should().Be(options.ApiBaseAddress);
    }

    [Test]
    public void Mapping_repository_handles_null_owner_login()
    {
        var data = new OctokitRepositoryData(
            Id: 1,
            Name: "repo",
            OwnerLogin: null,
            IsPrivate: true,
            DefaultBranch: "main",
            Description: null,
            UpdatedAt: null);

        var mapped = OctokitMappings.ToRepositorySummary(data);
        mapped.Should().BeEquivalentTo(new RepositorySummary(
            Id: 1,
            Name: "repo",
            OwnerLogin: string.Empty,
            IsPrivate: true,
            DefaultBranch: "main",
            Description: null,
            UpdatedAt: null));
    }

    [Test]
    public void Mapping_issue_maps_state_case_insensitively()
    {
        var open = OctokitMappings.ToIssueSummary(new OctokitIssueData(
            Id: 1,
            Number: 10,
            Title: "t",
            State: "OPEN",
            AuthorLogin: "me",
            CommentCount: 3,
            UpdatedAt: null));

        open.State.Should().Be(IssueState.Open);

        var closed = OctokitMappings.ToIssueSummary(new OctokitIssueData(
            Id: 2,
            Number: 11,
            Title: "t",
            State: "closed",
            AuthorLogin: "me",
            CommentCount: 0,
            UpdatedAt: null));

        closed.State.Should().Be(IssueState.Closed);
    }
}
