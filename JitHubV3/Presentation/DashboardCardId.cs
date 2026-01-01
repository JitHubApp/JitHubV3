namespace JitHubV3.Presentation;

internal static class DashboardCardId
{
    // NOTE: DashboardViewModel syncs cards by CardId; these must be globally unique.

    public const long ComposeSearchIssues = 40_000_001;
    public const long ComposeSearchRepositories = 40_000_002;
    public const long ComposeSearchUsers = 40_000_003;
    public const long ComposeSearchCode = 40_000_004;

    public const long FoundrySetup = 40_000_010;

    public const long RepoRecentlyUpdatedIssues = 20_000_003;
    public const long RepoSnapshot = 20_000_005;

    public const long RepoRecentActivityEmpty = 20_000_004;
    public const long MyRecentActivityEmpty = 30_000_004;
    public const long NotificationsEmpty = 30_000_003;

    private const long RepoRecentActivityItemBase = 20_000_004_000_000;
    private const long MyRecentActivityItemBase = 30_000_004_000_000;
    private const long NotificationsItemBase = 30_000_003_000_000;

    private const uint FeedCardIdModulo = 1_000_000;

    public static long NotificationsItem(string notificationId)
        => FeedItem(NotificationsItemBase, notificationId);

    public static long MyRecentActivityItem(string eventId)
        => FeedItem(MyRecentActivityItemBase, eventId);

    public static long RepoRecentActivityItem(string eventId)
        => FeedItem(RepoRecentActivityItemBase, eventId);

    private static long FeedItem(long cardIdBase, string id)
    {
        if (id is null)
        {
            throw new ArgumentNullException(nameof(id));
        }

        // Must be stable across processes; do NOT use string.GetHashCode().
        var hash = StableHash32(id);
        return cardIdBase + (hash % FeedCardIdModulo);
    }

    private static uint StableHash32(string value)
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

            return hash;
        }
    }
}
