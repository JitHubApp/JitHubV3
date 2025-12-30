using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public sealed class MyRecentActivityDashboardCardProvider : IStagedDashboardCardProvider
{
    private readonly IGitHubActivityService _activity;

    public MyRecentActivityDashboardCardProvider(IGitHubActivityService activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    public string ProviderId => "my-activity";

    public int Priority => 33;

    public DashboardCardProviderTier Tier => DashboardCardProviderTier.SingleCallMultiCard;

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
                    CardId: DashboardCardId.MyRecentActivityEmpty,
                    Kind: DashboardCardKind.MyRecentActivity,
                    Title: "Recent activity",
                    Subtitle: "No recent activity",
                    Summary: "No recent events found.",
                    Importance: 66,
                    TintVariant: 1),
            };
        }

        var cards = new List<DashboardCardModel>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var type = string.IsNullOrWhiteSpace(item.Type) ? "Event" : item.Type;
            var actor = string.IsNullOrWhiteSpace(item.ActorLogin) ? "someone" : item.ActorLogin;
            var when = item.CreatedAt == DateTimeOffset.MinValue ? "unknown" : item.CreatedAt.LocalDateTime.ToString("g");

            var repoText = item.Repo is null ? null : $"{item.Repo.Value.Owner}/{item.Repo.Value.Name}";
            var title = repoText is null ? Trim(type, 52) : Trim(repoText, 52);
            var subtitle = repoText is null ? null : Trim(type, 40);

            var details = string.IsNullOrWhiteSpace(item.Description) ? null : Trim(item.Description, 120);
            var summary = details is null
                ? $"{Trim(actor, 24)} · {when}"
                : $"{Trim(actor, 24)} · {when}\n{details}";

            cards.Add(new DashboardCardModel(
                CardId: DashboardCardId.MyRecentActivityItem(item.Id),
                Kind: DashboardCardKind.MyRecentActivity,
                Title: title,
                Subtitle: subtitle,
                Summary: summary,
                Importance: 66 - i,
                TintVariant: 1));
        }

        return cards;
    }

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max - 1) + "…";
}
