using FluentAssertions;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHubV3.Presentation.ComposeSearch;
using JitHubV3.Services.Ai;
using JitHubV3.Tests.Ai;

namespace JitHubV3.Tests.ComposeSearch;

public sealed class ComposeSearchOrchestratorAiTests
{
    [Test]
    public async Task SearchAsync_UsesAiPlanQuery_AndLimitsDomains_WhenEnabledAndSelected()
    {
        var issues = new CountingIssueSearchService();
        var repos = new CountingRepoSearchService();
        var users = new CountingUserSearchService();
        var code = new CountingCodeSearchService();

        var modelStore = new TestAiModelStore
        {
            Selection = new AiModelSelection(RuntimeId: "test", ModelId: "m")
        };

        var aiRuntime = new FakeAiRuntime(
            runtimeId: "test",
            plan: new AiGitHubQueryPlan(
                Query: "repo:uno-platform/uno is:issue bug",
                Domains: new[] { ComposeSearchDomain.IssuesAndPullRequests }));

        var resolver = new AiRuntimeResolver(modelStore, new[] { aiRuntime });

        var orchestrator = new ComposeSearchOrchestrator(
            issues,
            repos,
            users,
            code,
            aiSettings: new AiSettings { Enabled = true },
            aiRuntimeResolver: resolver);

        var response = await orchestrator.SearchAsync(
            new ComposeSearchRequest("find uno bugs", PageSize: 10),
            RefreshMode.CacheOnly,
            CancellationToken.None);

        response.Query.Should().Be("repo:uno-platform/uno is:issue bug");
        issues.CallCount.Should().Be(1);
        repos.CallCount.Should().Be(0);
        users.CallCount.Should().Be(0);
        code.CallCount.Should().Be(0);
    }

    [Test]
    public async Task SearchAsync_BypassesAi_WhenInputIsStructured()
    {
        var issues = new CountingIssueSearchService();
        var repos = new CountingRepoSearchService();
        var users = new CountingUserSearchService();
        var code = new CountingCodeSearchService();

        var modelStore = new TestAiModelStore
        {
            Selection = new AiModelSelection(RuntimeId: "test", ModelId: "m")
        };

        var aiRuntime = new FakeAiRuntime(
            runtimeId: "test",
            plan: new AiGitHubQueryPlan(
                Query: "THIS SHOULD NOT BE USED",
                Domains: new[] { ComposeSearchDomain.Code }));

        var resolver = new AiRuntimeResolver(modelStore, new[] { aiRuntime });

        var orchestrator = new ComposeSearchOrchestrator(
            issues,
            repos,
            users,
            code,
            aiSettings: new AiSettings { Enabled = true },
            aiRuntimeResolver: resolver);

        var structured = "repo:octocat/Hello-World is:issue";

        var response = await orchestrator.SearchAsync(
            new ComposeSearchRequest(structured, PageSize: 10),
            RefreshMode.CacheOnly,
            CancellationToken.None);

        response.Query.Should().Be(structured);
        aiRuntime.CallCount.Should().Be(0);

        issues.CallCount.Should().Be(1);
        repos.CallCount.Should().Be(1);
        users.CallCount.Should().Be(1);
        code.CallCount.Should().Be(1);
    }

    private sealed class FakeAiRuntime : IAiRuntime
    {
        private readonly AiGitHubQueryPlan _plan;

        public FakeAiRuntime(string runtimeId, AiGitHubQueryPlan plan)
        {
            RuntimeId = runtimeId;
            _plan = plan;
        }

        public string RuntimeId { get; }

        public int CallCount { get; private set; }

        public Task<AiGitHubQueryPlan?> BuildGitHubQueryPlanAsync(AiGitHubQueryBuildRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult<AiGitHubQueryPlan?>(_plan);
        }
    }

    private sealed class CountingIssueSearchService : IGitHubIssueSearchService
    {
        public int CallCount { get; private set; }

        public Task<PagedResult<IReadOnlyList<WorkItemSummary>>> SearchAsync(IssueSearchQuery query, PageRequest page, RefreshMode refresh, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new PagedResult<IReadOnlyList<WorkItemSummary>>(Array.Empty<WorkItemSummary>(), Next: null));
        }
    }

    private sealed class CountingRepoSearchService : IGitHubRepoSearchService
    {
        public int CallCount { get; private set; }

        public Task<PagedResult<IReadOnlyList<RepositorySummary>>> SearchAsync(RepoSearchQuery query, PageRequest page, RefreshMode refresh, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new PagedResult<IReadOnlyList<RepositorySummary>>(Array.Empty<RepositorySummary>(), Next: null));
        }
    }

    private sealed class CountingUserSearchService : IGitHubUserSearchService
    {
        public int CallCount { get; private set; }

        public Task<PagedResult<IReadOnlyList<UserSummary>>> SearchAsync(UserSearchQuery query, PageRequest page, RefreshMode refresh, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new PagedResult<IReadOnlyList<UserSummary>>(Array.Empty<UserSummary>(), Next: null));
        }
    }

    private sealed class CountingCodeSearchService : IGitHubCodeSearchService
    {
        public int CallCount { get; private set; }

        public Task<PagedResult<IReadOnlyList<CodeSearchItemSummary>>> SearchAsync(CodeSearchQuery query, PageRequest page, RefreshMode refresh, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new PagedResult<IReadOnlyList<CodeSearchItemSummary>>(Array.Empty<CodeSearchItemSummary>(), Next: null));
        }
    }
}
