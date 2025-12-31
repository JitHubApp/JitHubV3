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
}

public abstract record ComposeSearchItem;

public sealed record WorkItemSearchItem(WorkItemSummary WorkItem) : ComposeSearchItem;

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

    public ComposeSearchOrchestrator(IGitHubIssueSearchService issues)
    {
        _issues = issues;
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

        var result = await _issues.SearchAsync(
            new IssueSearchQuery(query),
            PageRequest.FirstPage(pageSize: request.PageSize),
            refresh,
            ct).ConfigureAwait(false);

        var items = result.Items
            .Select(wi => (ComposeSearchItem)new WorkItemSearchItem(wi))
            .ToArray();

        var groups = new[]
        {
            new ComposeSearchResultGroup(ComposeSearchDomain.IssuesAndPullRequests, items)
        };

        return new ComposeSearchResponse(input, query, groups);
    }

    private static bool IsStructuredQuery(string input)
        => StructuredQueryMarkers.Any(m => input.Contains(m, StringComparison.OrdinalIgnoreCase));
}
