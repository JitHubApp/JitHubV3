using System.Collections.Generic;

namespace JitHubV3.Presentation;

public sealed record DashboardCardModel(
    long CardId,
    DashboardCardKind Kind,
    string Title,
    string? Subtitle,
    string? Summary,
    int Importance = 0,
    IReadOnlyList<DashboardCardActionModel>? Actions = null,
    int? TintVariant = null);
