using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public sealed class RepoRecentlyUpdatedIssuesDashboardCardProvider : IStagedDashboardCardProvider
{
    private const long CardId = 20_000_003;

    private readonly IGitHubIssueService _issues;

    public RepoRecentlyUpdatedIssuesDashboardCardProvider(IGitHubIssueService issues)
    {
        _issues = issues ?? throw new ArgumentNullException(nameof(issues));
    }

    public string ProviderId => "repo-issues-recent";

    public int Priority => 21;

    public DashboardCardProviderTier Tier => DashboardCardProviderTier.SingleCallSingleCard;

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

        var page = PageRequest.FirstPage(pageSize: 8);
        var result = await _issues.GetIssuesAsync(repo.Value, query, page, refresh, ct).ConfigureAwait(false);

        var items = result.Items
            .OrderByDescending(i => i.UpdatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(i => i.Number)
            .Take(5)
            .ToArray();

        if (items.Length == 0)
        {
            return new[]
            {
                new DashboardCardModel(
                    CardId: CardId,
                    Kind: DashboardCardKind.RepoRecentlyUpdatedIssues,
                    Title: "Recently updated issues",
                    Subtitle: "No open issues",
                    Summary: null,
                    Importance: 70,
                    TintVariant: 3),
            };
        }

        var summary = string.Join("\n", items.Select(i => $"#{i.Number} {Trim(i.Title, 72)}"));

        return new[]
        {
            new DashboardCardModel(
                CardId: CardId,
                Kind: DashboardCardKind.RepoRecentlyUpdatedIssues,
                Title: "Recently updated issues",
                Subtitle: $"Top {items.Length}",
                Summary: summary,
                Importance: 72,
                TintVariant: 3),
        };
    }

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max - 1) + "…";
}
