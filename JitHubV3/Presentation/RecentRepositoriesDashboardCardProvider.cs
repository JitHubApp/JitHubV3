using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public sealed class RecentRepositoriesDashboardCardProvider : IStagedDashboardCardProvider
{
    private const long OverviewCardId = 10_000_001;
    private const long RecentCardId = 10_000_002;
    private const long MostActiveCardId = 10_000_003;

    private readonly IGitHubRepositoryService _repositories;

    public RecentRepositoriesDashboardCardProvider(IGitHubRepositoryService repositories)
    {
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
    }

    public string ProviderId => "recent-repos";

    public int Priority => 10;

    public DashboardCardProviderTier Tier => DashboardCardProviderTier.SingleCallMultiCard;

    public async Task<IReadOnlyList<DashboardCardModel>> GetCardsAsync(
        DashboardContext context,
        RefreshMode refresh,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var repos = await _repositories.GetMyRepositoriesAsync(refresh, ct).ConfigureAwait(false);
        if (repos.Count == 0)
        {
            return new[]
            {
                new DashboardCardModel(
                    CardId: OverviewCardId,
                    Kind: DashboardCardKind.Unknown,
                    Title: "Repositories",
                    Subtitle: "0 repositories",
                    Summary: "No repositories were returned for the current account.",
                    Importance: 60,
                    TintVariant: 0),
            };
        }

        var total = repos.Count;
        var privateCount = repos.Count(r => r.IsPrivate);

        var mostActive = repos
            .OrderByDescending(r => r.UpdatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(r => r.OwnerLogin, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var recent = repos
            .OrderByDescending(r => r.UpdatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(r => r.OwnerLogin, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        var overviewSubtitle = privateCount == 0
            ? $"{total} repositories"
            : $"{total} repositories · {privateCount} private";

        var overview = new DashboardCardModel(
            CardId: OverviewCardId,
            Kind: DashboardCardKind.Unknown,
            Title: "Repositories",
            Subtitle: overviewSubtitle,
            Summary: "Pick a repository on the left to personalize the dashboard.",
            Importance: 80,
            TintVariant: 1);

        var mostActiveCard = mostActive is null
            ? null
            : new DashboardCardModel(
                CardId: MostActiveCardId,
                Kind: DashboardCardKind.Unknown,
                Title: "Most recently updated",
                Subtitle: FormatRepo(mostActive),
                Summary: FormatUpdatedAt(mostActive.UpdatedAt),
                Importance: 75,
                TintVariant: 2);

        var recentSummary = string.Join("\n", recent.Select(r => $"{FormatRepo(r)} · {FormatShortUpdatedAt(r.UpdatedAt)}"));
        var recentCard = new DashboardCardModel(
            CardId: RecentCardId,
            Kind: DashboardCardKind.Unknown,
            Title: "Recently updated repositories",
            Subtitle: $"Top {recent.Length}",
            Summary: string.IsNullOrWhiteSpace(recentSummary) ? null : recentSummary,
            Importance: 70,
            TintVariant: 3);

        var cards = new List<DashboardCardModel>(capacity: 3)
        {
            overview,
            recentCard,
        };

        if (mostActiveCard is not null)
        {
            cards.Insert(1, mostActiveCard);
        }

        return cards;
    }

    private static string FormatRepo(RepositorySummary r) => $"{r.OwnerLogin}/{r.Name}";

    private static string FormatUpdatedAt(DateTimeOffset? updatedAt)
        => updatedAt is null ? "" : $"Last updated: {updatedAt.Value.LocalDateTime:g}";

    private static string FormatShortUpdatedAt(DateTimeOffset? updatedAt)
        => updatedAt is null ? "unknown" : updatedAt.Value.LocalDateTime.ToString("g");
}
