using JitHub.GitHub.Abstractions.Refresh;
using System.Collections.Generic;

namespace JitHubV3.Presentation;

public interface IDashboardCardProvider
{
    string ProviderId { get; }

    int Priority { get; }

    Task<IReadOnlyList<DashboardCardModel>> GetCardsAsync(
        DashboardContext context,
        RefreshMode refresh,
        CancellationToken ct);
}
