namespace JitHubV3.Presentation.ComposeSearch;

using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

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

    public ComposeSearchOrchestrator(
        IGitHubIssueSearchService issues,
        IGitHubRepoSearchService repos,
        IGitHubUserSearchService users,
        IGitHubCodeSearchService code)
    {
        _issues = issues ?? throw new ArgumentNullException(nameof(issues));
        _repos = repos ?? throw new ArgumentNullException(nameof(repos));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _code = code ?? throw new ArgumentNullException(nameof(code));
    }

    public async Task<ComposeSearchResponse> SearchAsync(ComposeSearchRequest request, RefreshMode refresh, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var input = (request.Input ?? string.Empty).Trim();
        if (input.Length == 0)
        {
            return new ComposeSearchResponse(request.Input ?? string.Empty, Query: string.Empty, Groups: Array.Empty<ComposeSearchResultGroup>());
        }

        var query = IsStructuredQuery(input) ? input : input;
        var page = PageRequest.FirstPage(pageSize: request.PageSize);

        var issuesTask = SearchIssuesBestEffortAsync(query, page, refresh, ct);
        var reposTask = SearchReposBestEffortAsync(query, page, refresh, ct);
        var usersTask = SearchUsersBestEffortAsync(query, page, refresh, ct);
        var codeTask = SearchCodeBestEffortAsync(query, page, refresh, ct);

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
