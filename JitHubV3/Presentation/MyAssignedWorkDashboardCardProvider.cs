using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public sealed class MyAssignedWorkDashboardCardProvider : IStagedDashboardCardProvider
{
    private const long CardId = 30_000_001;

    private readonly IGitHubIssueSearchService _search;

    public MyAssignedWorkDashboardCardProvider(IGitHubIssueSearchService search)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
    }

    public string ProviderId => "my-work-assigned";

    public int Priority => 30;

    public DashboardCardProviderTier Tier => DashboardCardProviderTier.SingleCallSingleCard;

    public async Task<IReadOnlyList<DashboardCardModel>> GetCardsAsync(DashboardContext context, RefreshMode refresh, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var page = PageRequest.FirstPage(pageSize: 5);
        var query = new IssueSearchQuery(
            Query: "is:open is:issue assignee:@me archived:false",
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
                    Kind: DashboardCardKind.MyAssignedWork,
                    Title: "My assigned issues",
                    Subtitle: "0 open",
                    Summary: "No open issues are currently assigned to you.",
                    Importance: 72,
                    TintVariant: 0),
            };
        }

        var summary = string.Join("\n", items.Select(i => $"{i.Repo.Owner}/{i.Repo.Name}#{i.Number} · {i.Title}"));

        return new[]
        {
            new DashboardCardModel(
                CardId: CardId,
                Kind: DashboardCardKind.MyAssignedWork,
                Title: "My assigned issues",
                Subtitle: $"Top {items.Count}",
                Summary: summary,
                Importance: 82,
                TintVariant: 0),
        };
    }
}
