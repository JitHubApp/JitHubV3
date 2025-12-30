using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public sealed class MyRecentActivityDashboardCardProvider : IStagedDashboardCardProvider
{
    private const long CardId = 30_000_004;

    private readonly IGitHubActivityService _activity;

    public MyRecentActivityDashboardCardProvider(IGitHubActivityService activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    public string ProviderId => "my-activity";

    public int Priority => 33;

    public DashboardCardProviderTier Tier => DashboardCardProviderTier.SingleCallSingleCard;

    public async Task<IReadOnlyList<DashboardCardModel>> GetCardsAsync(DashboardContext context, RefreshMode refresh, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var page = PageRequest.FirstPage(pageSize: 8);
        var result = await _activity.GetMyActivityAsync(page, refresh, ct).ConfigureAwait(false);
        var items = result.Items;

        if (items.Count == 0)
        {
            return new[]
            {
                new DashboardCardModel(
                    CardId: CardId,
                    Kind: DashboardCardKind.MyRecentActivity,
                    Title: "Recent activity",
                    Subtitle: "No recent activity",
                    Summary: "No recent events found.",
                    Importance: 66,
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
                Kind: DashboardCardKind.MyRecentActivity,
                Title: "Recent activity",
                Subtitle: $"Top {Math.Min(items.Count, 5)}",
                Summary: string.IsNullOrWhiteSpace(summary) ? null : summary,
                Importance: 66,
                TintVariant: 1),
        };
    }

    private static string FormatLine(ActivitySummary item)
    {
        var repo = FormatRepo(item.Repo);
        var type = string.IsNullOrWhiteSpace(item.Type) ? "Event" : item.Type;
        var actor = string.IsNullOrWhiteSpace(item.ActorLogin) ? "someone" : item.ActorLogin;

        return $"{Trim(repo, 28)} · {Trim(type, 22)} · {Trim(actor, 16)}";
    }

    private static string FormatRepo(RepoKey? repo)
        => repo is null ? "—" : $"{repo.Value.Owner}/{repo.Value.Name}";

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max - 1) + "…";
}
