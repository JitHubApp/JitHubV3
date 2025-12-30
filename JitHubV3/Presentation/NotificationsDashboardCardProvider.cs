using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public sealed class NotificationsDashboardCardProvider : IStagedDashboardCardProvider
{
    private const long EmptyCardId = 30_000_003;
    private const long CardIdBase = 30_000_003_000_000;
    private const int CardIdModulo = 1_000_000;

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
                    CardId: EmptyCardId,
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
                CardId: ComputeCardId(n.Id),
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

    private static long ComputeCardId(string id)
    {
        // Must be stable across processes; do NOT use string.GetHashCode().
        // We keep a dedicated ID range per provider to avoid collisions with other cards.
        var hash = StableHash32(id);
        return CardIdBase + (hash % CardIdModulo);
    }

    private static long StableHash32(string value)
    {
        // FNV-1a 32-bit
        unchecked
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;

            uint hash = offsetBasis;
            for (var i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= prime;
            }

            return (long)hash;
        }
    }
}
