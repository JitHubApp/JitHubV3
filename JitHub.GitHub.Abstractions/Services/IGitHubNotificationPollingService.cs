using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Polling;

namespace JitHub.GitHub.Abstractions.Services;

public interface IGitHubNotificationPollingService
{
    Task StartNotificationsPollingAsync(
        bool unreadOnly,
        PageRequest page,
        PollingRequest polling,
        CancellationToken ct);
}
