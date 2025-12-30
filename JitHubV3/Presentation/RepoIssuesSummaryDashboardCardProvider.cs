using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public sealed class RepoIssuesSummaryDashboardCardProvider : IStagedDashboardCardProvider
{
    private const long SummaryCardId = 20_000_001;
    private const long MostDiscussedCardId = 20_000_002;

    private readonly IGitHubIssueService _issues;

    public RepoIssuesSummaryDashboardCardProvider(IGitHubIssueService issues)
    {
        _issues = issues ?? throw new ArgumentNullException(nameof(issues));
    }

    public string ProviderId => "repo-issues-summary";

    public int Priority => 20;

    public DashboardCardProviderTier Tier => DashboardCardProviderTier.SingleCallMultiCard;

    public async Task<IReadOnlyList<DashboardCardModel>> GetCardsAsync(
        DashboardContext context,
        RefreshMode refresh,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var repo = context.SelectedRepo;
        if (repo is null)
        {
            return Array.Empty<DashboardCardModel>();
        }

        var query = new IssueQuery(
            State: IssueStateFilter.Open,
            SearchText: null,
            Sort: IssueSortField.Updated,
            Direction: IssueSortDirection.Desc);

        var page = PageRequest.FirstPage(pageSize: 20);
        var result = await _issues.GetIssuesAsync(repo.Value, query, page, refresh, ct).ConfigureAwait(false);

        var items = result.Items
            .OrderByDescending(i => i.UpdatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(i => i.Number)
            .ToArray();

        var summary = items.Length == 0
            ? "No open issues found."
            : string.Join("\n", items.Take(3).Select(FormatIssueLine));

        var summaryCard = new DashboardCardModel(
            CardId: SummaryCardId,
            Kind: DashboardCardKind.RepoIssuesSummary,
            Title: "Open issues",
            Subtitle: items.Length == 0 ? "No open issues" : $"Top {Math.Min(items.Length, 20)} (recently updated)",
            Summary: summary,
            Importance: 85,
            TintVariant: 2);

        if (items.Length == 0)
        {
            return new[] { summaryCard };
        }

        var mostDiscussed = items
            .OrderByDescending(i => i.CommentCount)
            .ThenByDescending(i => i.UpdatedAt ?? DateTimeOffset.MinValue)
            .First();

        var mostDiscussedCard = new DashboardCardModel(
            CardId: MostDiscussedCardId,
            Kind: DashboardCardKind.RepoIssuesSummary,
            Title: "Most discussed (open)",
            Subtitle: FormatIssueTitle(mostDiscussed),
            Summary: FormatIssueMeta(mostDiscussed),
            Importance: 80,
            TintVariant: 1);

        return new[] { summaryCard, mostDiscussedCard };
    }

    private static string FormatIssueTitle(IssueSummary issue) => $"#{issue.Number} {issue.Title}";

    private static string FormatIssueLine(IssueSummary issue)
        => $"#{issue.Number} {Trim(issue.Title, 80)} · {issue.CommentCount} comments · {FormatShortUpdatedAt(issue.UpdatedAt)}";

    private static string FormatIssueMeta(IssueSummary issue)
        => $"{issue.CommentCount} comments · Updated {FormatShortUpdatedAt(issue.UpdatedAt)}";

    private static string FormatShortUpdatedAt(DateTimeOffset? updatedAt)
        => updatedAt is null ? "unknown" : updatedAt.Value.LocalDateTime.ToString("g");

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max - 1) + "…";
}
