using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public sealed class MyReviewRequestsDashboardCardProvider : IStagedDashboardCardProvider
{
    private const long CardId = 30_000_002;

    private readonly IGitHubIssueSearchService _search;

    public MyReviewRequestsDashboardCardProvider(IGitHubIssueSearchService search)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
    }

    public string ProviderId => "my-work-review-requests";

    public int Priority => 31;

    public DashboardCardProviderTier Tier => DashboardCardProviderTier.SingleCallSingleCard;

    public async Task<IReadOnlyList<DashboardCardModel>> GetCardsAsync(DashboardContext context, RefreshMode refresh, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var page = PageRequest.FirstPage(pageSize: 5);
        var query = new IssueSearchQuery(
            Query: "is:open is:pr review-requested:@me archived:false",
            Sort: IssueSortField.Updated,
            Direction: IssueSortDirection.Desc);

        var results = await _search.SearchAsync(query, page, refresh, ct).ConfigureAwait(false);
        var items = results.Items;

        if (items.Count == 0)
        {
            return new[]
            {
                new DashboardCardModel(
                    CardId: CardId,
                    Kind: DashboardCardKind.MyReviewRequests,
                    Title: "Review requests",
                    Subtitle: "0 pending",
                    Summary: "No pull requests are currently requesting your review.",
                    Importance: 70,
                    TintVariant: 1),
            };
        }

        var summary = string.Join("\n", items.Select(i => $"{i.Repo.Owner}/{i.Repo.Name}#{i.Number} · {i.Title}"));

        return new[]
        {
            new DashboardCardModel(
                CardId: CardId,
                Kind: DashboardCardKind.MyReviewRequests,
                Title: "Review requests",
                Subtitle: $"Top {items.Count}",
                Summary: summary,
                Importance: 84,
                TintVariant: 1),
        };
    }
}
