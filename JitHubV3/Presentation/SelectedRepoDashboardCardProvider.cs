using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;

namespace JitHubV3.Presentation;

public sealed class SelectedRepoDashboardCardProvider : IStagedDashboardCardProvider
{
    public string ProviderId => "selected-repo";

    public int Priority => 0;

    public DashboardCardProviderTier Tier => DashboardCardProviderTier.Local;

    public Task<IReadOnlyList<DashboardCardModel>> GetCardsAsync(
        DashboardContext context,
        RefreshMode refresh,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var selected = context.SelectedRepo;
        DashboardCardModel card;

        if (selected is null)
        {
            card = new DashboardCardModel(
                CardId: 1,
                Kind: DashboardCardKind.SelectedRepo,
                Title: "No repository selected",
                Subtitle: null,
                Summary: "Select a repository in the left sidebar to personalize the dashboard.",
                Importance: 100);
        }
        else
        {
            card = new DashboardCardModel(
                CardId: 1,
                Kind: DashboardCardKind.SelectedRepo,
                Title: "Selected repository",
                Subtitle: FormatRepo(selected.Value),
                Summary: null,
                Importance: 100);
        }

        IReadOnlyList<DashboardCardModel> cards = new[] { card };
        return Task.FromResult(cards);
    }

    private static string FormatRepo(RepoKey key) => $"{key.Owner}/{key.Name}";
}
