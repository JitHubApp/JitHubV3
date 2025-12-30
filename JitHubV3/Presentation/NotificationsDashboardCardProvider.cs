using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public sealed class NotificationsDashboardCardProvider : IStagedDashboardCardProvider
{
    private const long CardId = 30_000_003;

    private readonly IGitHubNotificationService _notifications;

    public NotificationsDashboardCardProvider(IGitHubNotificationService notifications)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    public string ProviderId => "notifications";

    public int Priority => 32;

    public DashboardCardProviderTier Tier => DashboardCardProviderTier.SingleCallSingleCard;

    public async Task<IReadOnlyList<DashboardCardModel>> GetCardsAsync(DashboardContext context, RefreshMode refresh, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var page = PageRequest.FirstPage(pageSize: 5);
        var result = await _notifications.GetMyNotificationsAsync(unreadOnly: true, page, refresh, ct).ConfigureAwait(false);
        var items = result.Items;

        if (items.Count == 0)
        {
            return new[]
            {
                new DashboardCardModel(
                    CardId: CardId,
                    Kind: DashboardCardKind.Notifications,
                    Title: "Notifications",
                    Subtitle: "0 unread",
                    Summary: "You're all caught up.",
                    Importance: 69,
                    TintVariant: 2),
            };
        }

        var lines = items
            .Take(4)
            .Select(n => $"{Trim(FormatRepo(n.Repo), 28)} · {Trim(n.Title, 52)}")
            .ToArray();

        var summary = string.Join("\n", lines);
        var subtitle = items.Count == 1 ? "1 unread" : $"{items.Count} unread";

        return new[]
        {
            new DashboardCardModel(
                CardId: CardId,
                Kind: DashboardCardKind.Notifications,
                Title: "Notifications",
                Subtitle: subtitle,
                Summary: string.IsNullOrWhiteSpace(summary) ? null : summary,
                Importance: 69,
                TintVariant: 2),
        };
    }

    private static string FormatRepo(JitHub.GitHub.Abstractions.Models.RepoKey repo)
        => $"{repo.Owner}/{repo.Name}";

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max - 1) + "…";
}
