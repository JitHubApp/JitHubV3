namespace JitHubV3.Presentation.ComposeSearch;

using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;
using JitHubV3.Services.Ai;

public interface IComposeSearchOrchestrator
{
    Task<ComposeSearchResponse> SearchAsync(ComposeSearchRequest request, RefreshMode refresh, CancellationToken ct);
}

public sealed record ComposeSearchRequest(
    string Input,
    int PageSize = 20);

public sealed record ComposeSearchResponse(
    string Input,
    string Query,
    IReadOnlyList<ComposeSearchResultGroup> Groups);

public sealed record ComposeSearchResultGroup(
    ComposeSearchDomain Domain,
    IReadOnlyList<ComposeSearchItem> Items);

public enum ComposeSearchDomain
{
    IssuesAndPullRequests = 0,
    Repositories = 1,
    Users = 2,
    Code = 3,
}

public abstract record ComposeSearchItem;

public sealed record WorkItemSearchItem(WorkItemSummary WorkItem) : ComposeSearchItem;

public sealed record RepositorySearchItem(RepositorySummary Repository) : ComposeSearchItem;

public sealed record UserSearchItem(UserSummary User) : ComposeSearchItem;

public sealed record CodeSearchItem(CodeSearchItemSummary Code) : ComposeSearchItem;

public sealed class ComposeSearchOrchestrator : IComposeSearchOrchestrator
{
    private static readonly string[] StructuredQueryMarkers =
    [
        "repo:",
        "org:",
        "user:",
        "is:",
        "label:",
        "author:",
        "assignee:",
        "mentions:",
        "in:",
        "sort:",
        "archived:",
    ];

    private readonly IGitHubIssueSearchService _issues;
    private readonly IGitHubRepoSearchService _repos;
    private readonly IGitHubUserSearchService _users;
    private readonly IGitHubCodeSearchService _code;

    private readonly AiSettings? _aiSettings;
    private readonly IAiEnablementStore? _aiEnablementStore;
    private readonly IAiRuntimeResolver? _aiRuntimeResolver;

    public ComposeSearchOrchestrator(
        IGitHubIssueSearchService issues,
        IGitHubRepoSearchService repos,
        IGitHubUserSearchService users,
        IGitHubCodeSearchService code,
        AiSettings? aiSettings = null,
        IAiEnablementStore? aiEnablementStore = null,
        IAiRuntimeResolver? aiRuntimeResolver = null)
    {
        _issues = issues ?? throw new ArgumentNullException(nameof(issues));
        _repos = repos ?? throw new ArgumentNullException(nameof(repos));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _code = code ?? throw new ArgumentNullException(nameof(code));

        _aiSettings = aiSettings;
        _aiEnablementStore = aiEnablementStore;
        _aiRuntimeResolver = aiRuntimeResolver;
    }

    public async Task<ComposeSearchResponse> SearchAsync(ComposeSearchRequest request, RefreshMode refresh, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var input = (request.Input ?? string.Empty).Trim();
        if (input.Length == 0)
        {
            return new ComposeSearchResponse(request.Input ?? string.Empty, Query: string.Empty, Groups: Array.Empty<ComposeSearchResultGroup>());
        }

        var isStructured = IsStructuredQuery(input);
        var query = input;
        IReadOnlyList<ComposeSearchDomain>? requestedDomains = null;

        if (!isStructured)
        {
            var plan = await TryBuildAiPlanAsync(input, ct).ConfigureAwait(false);
            if (plan is not null)
            {
                query = plan.Query;
                requestedDomains = plan.Domains;
            }
        }

        var page = PageRequest.FirstPage(pageSize: request.PageSize);

        var includeIssues = requestedDomains is null || requestedDomains.Contains(ComposeSearchDomain.IssuesAndPullRequests);
        var includeRepos = requestedDomains is null || requestedDomains.Contains(ComposeSearchDomain.Repositories);
        var includeUsers = requestedDomains is null || requestedDomains.Contains(ComposeSearchDomain.Users);
        var includeCode = requestedDomains is null || requestedDomains.Contains(ComposeSearchDomain.Code);

        var issuesTask = includeIssues ? SearchIssuesBestEffortAsync(query, page, refresh, ct) : Task.FromResult<IReadOnlyList<ComposeSearchItem>>(Array.Empty<ComposeSearchItem>());
        var reposTask = includeRepos ? SearchReposBestEffortAsync(query, page, refresh, ct) : Task.FromResult<IReadOnlyList<ComposeSearchItem>>(Array.Empty<ComposeSearchItem>());
        var usersTask = includeUsers ? SearchUsersBestEffortAsync(query, page, refresh, ct) : Task.FromResult<IReadOnlyList<ComposeSearchItem>>(Array.Empty<ComposeSearchItem>());
        var codeTask = includeCode ? SearchCodeBestEffortAsync(query, page, refresh, ct) : Task.FromResult<IReadOnlyList<ComposeSearchItem>>(Array.Empty<ComposeSearchItem>());

        await Task.WhenAll(issuesTask, reposTask, usersTask, codeTask).ConfigureAwait(false);

        var groups = new[]
        {
            new ComposeSearchResultGroup(ComposeSearchDomain.IssuesAndPullRequests, issuesTask.Result),
            new ComposeSearchResultGroup(ComposeSearchDomain.Repositories, reposTask.Result),
            new ComposeSearchResultGroup(ComposeSearchDomain.Users, usersTask.Result),
            new ComposeSearchResultGroup(ComposeSearchDomain.Code, codeTask.Result),
        };

        return new ComposeSearchResponse(input, query, groups);
    }

    private async Task<AiGitHubQueryPlan?> TryBuildAiPlanAsync(string input, CancellationToken ct)
    {
        if (_aiSettings?.Enabled != true)
        {
            return null;
        }

        if (_aiEnablementStore is not null)
        {
            var enabled = await _aiEnablementStore.GetIsEnabledAsync(ct).ConfigureAwait(false);
            if (!enabled)
            {
                return null;
            }
        }

        if (_aiRuntimeResolver is null)
        {
            return null;
        }

        try
        {
            var runtime = await _aiRuntimeResolver.ResolveSelectedRuntimeAsync(ct).ConfigureAwait(false);
            if (runtime is null)
            {
                return null;
            }

            return await runtime.BuildGitHubQueryPlanAsync(new AiGitHubQueryBuildRequest(input), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<ComposeSearchItem>> SearchIssuesBestEffortAsync(
        string query,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct)
    {
        try
        {
            var result = await _issues.SearchAsync(new IssueSearchQuery(query), page, refresh, ct).ConfigureAwait(false);
            return result.Items.Select(wi => (ComposeSearchItem)new WorkItemSearchItem(wi)).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Array.Empty<ComposeSearchItem>();
        }
    }

    private async Task<IReadOnlyList<ComposeSearchItem>> SearchReposBestEffortAsync(
        string query,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct)
    {
        try
        {
            var result = await _repos.SearchAsync(new RepoSearchQuery(query), page, refresh, ct).ConfigureAwait(false);
            return result.Items.Select(r => (ComposeSearchItem)new RepositorySearchItem(r)).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Array.Empty<ComposeSearchItem>();
        }
    }

    private async Task<IReadOnlyList<ComposeSearchItem>> SearchUsersBestEffortAsync(
        string query,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct)
    {
        try
        {
            var result = await _users.SearchAsync(new UserSearchQuery(query), page, refresh, ct).ConfigureAwait(false);
            return result.Items.Select(u => (ComposeSearchItem)new UserSearchItem(u)).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Array.Empty<ComposeSearchItem>();
        }
    }

    private async Task<IReadOnlyList<ComposeSearchItem>> SearchCodeBestEffortAsync(
        string query,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct)
    {
        try
        {
            var result = await _code.SearchAsync(new CodeSearchQuery(query), page, refresh, ct).ConfigureAwait(false);
            return result.Items.Select(c => (ComposeSearchItem)new CodeSearchItem(c)).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Array.Empty<ComposeSearchItem>();
        }
    }

    private static bool IsStructuredQuery(string input)
        => StructuredQueryMarkers.Any(m => input.Contains(m, StringComparison.OrdinalIgnoreCase));
}
