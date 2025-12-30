using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public sealed class RepoRecentActivityDashboardCardProvider : IStagedDashboardCardProvider
{
    private const long CardId = 20_000_004;

    private readonly IGitHubActivityService _activity;

    public RepoRecentActivityDashboardCardProvider(IGitHubActivityService activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    public string ProviderId => "repo-activity";

    public int Priority => 22;

    public DashboardCardProviderTier Tier => DashboardCardProviderTier.SingleCallSingleCard;

    public async Task<IReadOnlyList<DashboardCardModel>> GetCardsAsync(DashboardContext context, RefreshMode refresh, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var repo = context.SelectedRepo;
        if (repo is null)
        {
            return Array.Empty<DashboardCardModel>();
        }

        var page = PageRequest.FirstPage(pageSize: 10);
        var result = await _activity.GetRepoActivityAsync(repo.Value, page, refresh, ct).ConfigureAwait(false);
        var items = result.Items;

        if (items.Count == 0)
        {
            return new[]
            {
                new DashboardCardModel(
                    CardId: CardId,
                    Kind: DashboardCardKind.RepoRecentActivity,
                    Title: "Repo activity",
                    Subtitle: "No recent activity",
                    Summary: null,
                    Importance: 64,
                    TintVariant: 1),
            };
        }

        var lines = items
            .Take(5)
            .Select(FormatLine)
            .ToArray();

        var summary = string.Join("\n", lines);

        return new[]
        {
            new DashboardCardModel(
                CardId: CardId,
                Kind: DashboardCardKind.RepoRecentActivity,
                Title: "Repo activity",
                Subtitle: $"Top {Math.Min(items.Count, 5)}",
                Summary: string.IsNullOrWhiteSpace(summary) ? null : summary,
                Importance: 64,
                TintVariant: 1),
        };
    }

    private static string FormatLine(ActivitySummary item)
    {
        var type = string.IsNullOrWhiteSpace(item.Type) ? "Event" : item.Type;
        var actor = string.IsNullOrWhiteSpace(item.ActorLogin) ? "someone" : item.ActorLogin;
        var when = item.CreatedAt == DateTimeOffset.MinValue ? "unknown" : item.CreatedAt.LocalDateTime.ToString("g");

        return $"{Trim(type, 30)} · {Trim(actor, 18)} · {when}";
    }

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max - 1) + "…";
}
