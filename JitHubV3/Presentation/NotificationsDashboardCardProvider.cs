using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public sealed class NotificationsDashboardCardProvider : IStagedDashboardCardProvider
{
    private readonly IGitHubNotificationService _notifications;

    public NotificationsDashboardCardProvider(IGitHubNotificationService notifications)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    public string ProviderId => "notifications";

    public int Priority => 32;

    public DashboardCardProviderTier Tier => DashboardCardProviderTier.SingleCallMultiCard;

    public async Task<IReadOnlyList<DashboardCardModel>> GetCardsAsync(DashboardContext context, RefreshMode refresh, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var page = PageRequest.FirstPage(pageSize: 8);
        var result = await _notifications.GetMyNotificationsAsync(unreadOnly: true, page, refresh, ct).ConfigureAwait(false);
        var items = result.Items;

        if (items.Count == 0)
        {
            return new[]
            {
                new DashboardCardModel(
                    CardId: DashboardCardId.NotificationsEmpty,
                    Kind: DashboardCardKind.Notifications,
                    Title: "Notifications",
                    Subtitle: "0 unread",
                    Summary: "You're all caught up.",
                    Importance: 69,
                    TintVariant: 2),
            };
        }

        var cards = new List<DashboardCardModel>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var n = items[i];

            var when = n.UpdatedAt is null
                ? "Updated recently"
                : n.UpdatedAt.Value.LocalDateTime.ToString("g");

            var type = string.IsNullOrWhiteSpace(n.Type) ? "Notification" : n.Type;

            cards.Add(new DashboardCardModel(
                CardId: DashboardCardId.NotificationsItem(n.Id),
                Kind: DashboardCardKind.Notifications,
                Title: Trim(n.Title, 64),
                Subtitle: Trim(FormatRepo(n.Repo), 48),
                Summary: $"{Trim(type, 20)} · {when}",
                Importance: 69 - i,
                TintVariant: 2));
        }

        return cards;
    }

    private static string FormatRepo(JitHub.GitHub.Abstractions.Models.RepoKey repo)
        => $"{repo.Owner}/{repo.Name}";

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max - 1) + "…";
}
