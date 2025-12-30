using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Refresh;

namespace JitHub.GitHub.Abstractions.Services;

public interface IGitHubNotificationService
{
    Task<PagedResult<IReadOnlyList<NotificationSummary>>> GetMyNotificationsAsync(
        bool unreadOnly,
        PageRequest page,
        RefreshMode refresh,
        CancellationToken ct);
}
