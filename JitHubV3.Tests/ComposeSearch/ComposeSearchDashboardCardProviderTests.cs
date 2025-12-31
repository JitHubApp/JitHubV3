using FluentAssertions;
using JitHub.GitHub.Abstractions.Models;
using JitHubV3.Presentation;
using JitHubV3.Presentation.ComposeSearch;

namespace JitHubV3.Tests.ComposeSearch;

public sealed class ComposeSearchDashboardCardProviderTests
{
    [Test]
    public async Task ReturnsEmpty_WhenNoState()
    {
        var state = new ComposeSearchStateStore();
        var factory = new ComposeSearchCardFactory();
        var provider = new ComposeSearchDashboardCardProvider(state, factory);

        var cards = await provider.GetCardsAsync(new DashboardContext(), refresh: default, CancellationToken.None);

        cards.Should().BeEmpty();
    }

    [Test]
    public async Task ProducesStableCardId_ForIssuesGroupCard()
    {
        var state = new ComposeSearchStateStore();
        var factory = new ComposeSearchCardFactory();
        var provider = new ComposeSearchDashboardCardProvider(state, factory);

        var wi = new WorkItemSummary(
            Id: 123,
            Repo: new RepoKey("octocat", "Hello-World"),
            Number: 42,
            Title: "Fix bug",
            IsPullRequest: false,
            State: "open",
            AuthorLogin: "me",
            CommentCount: 1,
            UpdatedAt: null);

        var response = new ComposeSearchResponse(
            Input: "bug",
            Query: "bug",
            Groups: new[]
            {
                new ComposeSearchResultGroup(
                    ComposeSearchDomain.IssuesAndPullRequests,
                    new ComposeSearchItem[] { new WorkItemSearchItem(wi) })
            });

        state.SetLatest(response);

        var cards1 = await provider.GetCardsAsync(new DashboardContext(), refresh: default, CancellationToken.None);
        var cards2 = await provider.GetCardsAsync(new DashboardContext(), refresh: default, CancellationToken.None);

        cards1.Should().HaveCount(1);
        cards1[0].CardId.Should().Be(DashboardCardId.ComposeSearchIssues);
        cards2[0].CardId.Should().Be(DashboardCardId.ComposeSearchIssues);
        cards1[0].Should().Be(cards2[0]);
    }
}
