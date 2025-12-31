namespace JitHubV3.Presentation.ComposeSearch;

using JitHub.GitHub.Abstractions.Refresh;

public sealed class ComposeSearchDashboardCardProvider : IStagedDashboardCardProvider
{
    private readonly IComposeSearchStateStore _state;
    private readonly IComposeSearchCardFactory _cards;

    public ComposeSearchDashboardCardProvider(IComposeSearchStateStore state, IComposeSearchCardFactory cards)
    {
        _state = state;
        _cards = cards;
    }

    public string ProviderId => "compose-search";

    // Near the top, but below the selected repo card.
    public int Priority => 5;

    public DashboardCardProviderTier Tier => DashboardCardProviderTier.Local;

    public Task<IReadOnlyList<DashboardCardModel>> GetCardsAsync(
        DashboardContext context,
        RefreshMode refresh,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var latest = _state.Latest;
        if (latest is null || string.IsNullOrWhiteSpace(latest.Query))
        {
            return Task.FromResult<IReadOnlyList<DashboardCardModel>>(Array.Empty<DashboardCardModel>());
        }

        var cards = _cards.CreateCards(latest, maxItems: 5);
        return Task.FromResult(cards);
    }
}
