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
        var issues = new CapturingIssueSearchService();
        var repos = new CapturingRepoSearchService();
        var users = new CapturingUserSearchService();
        var code = new CapturingCodeSearchService();

        var orchestrator = new ComposeSearchOrchestrator(issues, repos, users, code);

        var response = await orchestrator.SearchAsync(
            new ComposeSearchRequest("repo:octocat/Hello-World is:issue", PageSize: 37),
            RefreshMode.CacheOnly,
            CancellationToken.None);

        response.Query.Should().Be("repo:octocat/Hello-World is:issue");
        issues.CapturedPage.Should().Be(PageRequest.FirstPage(37));
        issues.CapturedQuery.Should().Be(new IssueSearchQuery("repo:octocat/Hello-World is:issue"));
        issues.CapturedRefresh.Should().Be(RefreshMode.CacheOnly);

        repos.CapturedPage.Should().Be(PageRequest.FirstPage(37));
        repos.CapturedQuery.Should().Be(new RepoSearchQuery("repo:octocat/Hello-World is:issue"));
        repos.CapturedRefresh.Should().Be(RefreshMode.CacheOnly);

        users.CapturedPage.Should().Be(PageRequest.FirstPage(37));
        users.CapturedQuery.Should().Be(new UserSearchQuery("repo:octocat/Hello-World is:issue"));
        users.CapturedRefresh.Should().Be(RefreshMode.CacheOnly);

        code.CapturedPage.Should().Be(PageRequest.FirstPage(37));
        code.CapturedQuery.Should().Be(new CodeSearchQuery("repo:octocat/Hello-World is:issue"));
        code.CapturedRefresh.Should().Be(RefreshMode.CacheOnly);
    }

    [Test]
    public void SearchAsync_Throws_WhenCancelled()
    {
        var issues = new CapturingIssueSearchService();
        var repos = new CapturingRepoSearchService();
        var users = new CapturingUserSearchService();
        var code = new CapturingCodeSearchService();

        var orchestrator = new ComposeSearchOrchestrator(issues, repos, users, code);

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

    private sealed class CapturingRepoSearchService : IGitHubRepoSearchService
    {
        public RepoSearchQuery? CapturedQuery { get; private set; }
        public PageRequest? CapturedPage { get; private set; }
        public RefreshMode? CapturedRefresh { get; private set; }

        public Task<PagedResult<IReadOnlyList<RepositorySummary>>> SearchAsync(
            RepoSearchQuery query,
            PageRequest page,
            RefreshMode refresh,
            CancellationToken ct)
        {
            CapturedQuery = query;
            CapturedPage = page;
            CapturedRefresh = refresh;

            IReadOnlyList<RepositorySummary> items = Array.Empty<RepositorySummary>();
            return Task.FromResult(new PagedResult<IReadOnlyList<RepositorySummary>>(items, Next: null));
        }
    }

    private sealed class CapturingUserSearchService : IGitHubUserSearchService
    {
        public UserSearchQuery? CapturedQuery { get; private set; }
        public PageRequest? CapturedPage { get; private set; }
        public RefreshMode? CapturedRefresh { get; private set; }

        public Task<PagedResult<IReadOnlyList<UserSummary>>> SearchAsync(
            UserSearchQuery query,
            PageRequest page,
            RefreshMode refresh,
            CancellationToken ct)
        {
            CapturedQuery = query;
            CapturedPage = page;
            CapturedRefresh = refresh;

            IReadOnlyList<UserSummary> items = Array.Empty<UserSummary>();
            return Task.FromResult(new PagedResult<IReadOnlyList<UserSummary>>(items, Next: null));
        }
    }

    private sealed class CapturingCodeSearchService : IGitHubCodeSearchService
    {
        public CodeSearchQuery? CapturedQuery { get; private set; }
        public PageRequest? CapturedPage { get; private set; }
        public RefreshMode? CapturedRefresh { get; private set; }

        public Task<PagedResult<IReadOnlyList<CodeSearchItemSummary>>> SearchAsync(
            CodeSearchQuery query,
            PageRequest page,
            RefreshMode refresh,
            CancellationToken ct)
        {
            CapturedQuery = query;
            CapturedPage = page;
            CapturedRefresh = refresh;

            IReadOnlyList<CodeSearchItemSummary> items = Array.Empty<CodeSearchItemSummary>();
            return Task.FromResult(new PagedResult<IReadOnlyList<CodeSearchItemSummary>>(items, Next: null));
        }
    }
}
