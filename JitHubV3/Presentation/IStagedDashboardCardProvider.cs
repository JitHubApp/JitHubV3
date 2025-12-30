namespace JitHubV3.Presentation;

public interface IStagedDashboardCardProvider : IDashboardCardProvider
{
    DashboardCardProviderTier Tier { get; }
}
