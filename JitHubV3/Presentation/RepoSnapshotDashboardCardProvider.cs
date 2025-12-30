using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Abstractions.Services;

namespace JitHubV3.Presentation;

public sealed class RepoSnapshotDashboardCardProvider : IStagedDashboardCardProvider
{
    private readonly IGitHubRepositoryDetailsService _details;

    public RepoSnapshotDashboardCardProvider(IGitHubRepositoryDetailsService details)
    {
        _details = details ?? throw new ArgumentNullException(nameof(details));
    }

    public string ProviderId => "repo-snapshot";

    public int Priority => 21;

    // Include in Wave A so CacheOnly can render quickly.
    public DashboardCardProviderTier Tier => DashboardCardProviderTier.SingleCallMultiCard;

    public async Task<IReadOnlyList<DashboardCardModel>> GetCardsAsync(
        DashboardContext context,
        RefreshMode refresh,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var repo = context.SelectedRepo;
        if (repo is null)
        {
            return Array.Empty<DashboardCardModel>();
        }

        var snapshot = await _details.GetRepositoryAsync(repo.Value, refresh, ct).ConfigureAwait(false);

        if (snapshot is null)
        {
            return new[]
            {
                new DashboardCardModel(
                    CardId: DashboardCardId.RepoSnapshot,
                    Kind: DashboardCardKind.RepoSnapshot,
                    Title: "Repository snapshot",
                    Subtitle: FormatRepo(repo.Value),
                    Summary: "No cached details yet.",
                    Importance: 82,
                    TintVariant: 0),
            };
        }

        var summary = BuildSummary(snapshot);

        return new[]
        {
            new DashboardCardModel(
                CardId: DashboardCardId.RepoSnapshot,
                Kind: DashboardCardKind.RepoSnapshot,
                Title: "Repository snapshot",
                Subtitle: FormatSubtitle(snapshot),
                Summary: string.IsNullOrWhiteSpace(summary) ? null : summary,
                Importance: 82,
                TintVariant: 0),
        };
    }

    private static string FormatRepo(RepoKey key) => $"{key.Owner}/{key.Name}";

    private static string FormatSubtitle(RepositorySnapshot snapshot)
    {
        var privacy = snapshot.IsPrivate ? "private" : "public";
        return $"{FormatRepo(snapshot.Repo)} · {privacy}";
    }

    private static string BuildSummary(RepositorySnapshot snapshot)
    {
        var lines = new List<string>(capacity: 3);

        if (!string.IsNullOrWhiteSpace(snapshot.Description))
        {
            lines.Add(Trim(snapshot.Description!, 120));
        }

        lines.Add($"★ {snapshot.StargazersCount} · Forks {snapshot.ForksCount} · Watchers {snapshot.WatchersCount}");

        var updated = snapshot.UpdatedAt is null ? "unknown" : snapshot.UpdatedAt.Value.LocalDateTime.ToString("g");
        var branch = string.IsNullOrWhiteSpace(snapshot.DefaultBranch) ? "unknown" : snapshot.DefaultBranch;
        lines.Add($"Default {branch} · Updated {updated}");

        return string.Join("\n", lines);
    }

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max - 1) + "…";
}
