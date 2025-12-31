using FluentAssertions;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHubV3.Presentation.ComposeSearch;

namespace JitHubV3.Tests.ComposeSearch;

public sealed class ComposeSearchOrchestratorTests
{
    [Test]
    public async Task SearchAsync_UsesFirstPage_WithConfiguredPageSize()
    {
        var service = new CapturingIssueSearchService();
        var orchestrator = new ComposeSearchOrchestrator(service);

        var response = await orchestrator.SearchAsync(
            new ComposeSearchRequest("repo:octocat/Hello-World is:issue", PageSize: 37),
            RefreshMode.CacheOnly,
            CancellationToken.None);

        response.Query.Should().Be("repo:octocat/Hello-World is:issue");
        service.CapturedPage.Should().Be(PageRequest.FirstPage(37));
        service.CapturedQuery.Should().Be(new IssueSearchQuery("repo:octocat/Hello-World is:issue"));
        service.CapturedRefresh.Should().Be(RefreshMode.CacheOnly);
    }

    [Test]
    public void SearchAsync_Throws_WhenCancelled()
    {
        var service = new CapturingIssueSearchService();
        var orchestrator = new ComposeSearchOrchestrator(service);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => orchestrator.SearchAsync(new ComposeSearchRequest("test"), RefreshMode.CacheOnly, cts.Token);

        act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class CapturingIssueSearchService : IGitHubIssueSearchService
    {
        public IssueSearchQuery? CapturedQuery { get; private set; }
        public PageRequest? CapturedPage { get; private set; }
        public RefreshMode? CapturedRefresh { get; private set; }

        public Task<PagedResult<IReadOnlyList<WorkItemSummary>>> SearchAsync(
            IssueSearchQuery query,
            PageRequest page,
            RefreshMode refresh,
            CancellationToken ct)
        {
            CapturedQuery = query;
            CapturedPage = page;
            CapturedRefresh = refresh;

            IReadOnlyList<WorkItemSummary> items = Array.Empty<WorkItemSummary>();
            return Task.FromResult(new PagedResult<IReadOnlyList<WorkItemSummary>>(items, Next: null));
        }
    }
}
