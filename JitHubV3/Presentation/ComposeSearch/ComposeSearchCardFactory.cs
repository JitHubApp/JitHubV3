namespace JitHubV3.Presentation.ComposeSearch;

using JitHubV3.Presentation;

public interface IComposeSearchCardFactory
{
    IReadOnlyList<DashboardCardModel> CreateCards(ComposeSearchResponse response, int maxItems);
}

public sealed class ComposeSearchCardFactory : IComposeSearchCardFactory
{
    public IReadOnlyList<DashboardCardModel> CreateCards(ComposeSearchResponse response, int maxItems)
    {
        if (response.Groups.Count == 0)
        {
            return Array.Empty<DashboardCardModel>();
        }

        // Phase 0.3: issues-only. Represent results as a single list card.
        var group = response.Groups
            .FirstOrDefault(g => g.Domain == ComposeSearchDomain.IssuesAndPullRequests);

        if (group is null || group.Items.Count == 0)
        {
            return Array.Empty<DashboardCardModel>();
        }

        var lines = new List<string>();
        foreach (var item in group.Items.OfType<WorkItemSearchItem>().Take(Math.Max(1, maxItems)))
        {
            var wi = item.WorkItem;
            var repo = $"{wi.Repo.Owner}/{wi.Repo.Name}";
            var number = wi.IsPullRequest ? $"PR #{wi.Number}" : $"#{wi.Number}";
            var title = string.IsNullOrWhiteSpace(wi.Title) ? "(no title)" : wi.Title.Trim();
            lines.Add($"{repo} {number} • {title}");
        }

        var summary = string.Join("\n", lines);

        var card = new DashboardCardModel(
            CardId: DashboardCardId.ComposeSearchIssues,
            Kind: DashboardCardKind.ComposeSearchIssues,
            Title: "Search results",
            Subtitle: response.Query,
            Summary: summary,
            Importance: 95);

        return new[] { card };
    }
}
