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

        var cards = new List<DashboardCardModel>();
        var take = Math.Max(1, maxItems);

        // Issues / PRs
        var issuesGroup = response.Groups.FirstOrDefault(g => g.Domain == ComposeSearchDomain.IssuesAndPullRequests);
        if (issuesGroup is not null && issuesGroup.Items.Count > 0)
        {
            var lines = new List<string>();
            foreach (var item in issuesGroup.Items.OfType<WorkItemSearchItem>().Take(take))
            {
                var wi = item.WorkItem;
                var repo = $"{wi.Repo.Owner}/{wi.Repo.Name}";
                var number = wi.IsPullRequest ? $"PR #{wi.Number}" : $"#{wi.Number}";
                var title = string.IsNullOrWhiteSpace(wi.Title) ? "(no title)" : wi.Title.Trim();
                lines.Add($"{repo} {number} • {title}");
            }

            if (lines.Count > 0)
            {
                cards.Add(new DashboardCardModel(
                    CardId: DashboardCardId.ComposeSearchIssues,
                    Kind: DashboardCardKind.ComposeSearchIssues,
                    Title: "Issues & PRs",
                    Subtitle: response.Query,
                    Summary: string.Join("\n", lines),
                    Importance: 95));
            }
        }

        // Repositories
        var reposGroup = response.Groups.FirstOrDefault(g => g.Domain == ComposeSearchDomain.Repositories);
        if (reposGroup is not null && reposGroup.Items.Count > 0)
        {
            var lines = new List<string>();
            foreach (var item in reposGroup.Items.OfType<RepositorySearchItem>().Take(take))
            {
                var r = item.Repository;
                var name = $"{r.OwnerLogin}/{r.Name}";
                var visibility = r.IsPrivate ? "Private" : "Public";
                var description = string.IsNullOrWhiteSpace(r.Description) ? string.Empty : $" • {r.Description.Trim()}";
                lines.Add($"{name} • {visibility}{description}");
            }

            if (lines.Count > 0)
            {
                cards.Add(new DashboardCardModel(
                    CardId: DashboardCardId.ComposeSearchRepositories,
                    Kind: DashboardCardKind.ComposeSearchRepositories,
                    Title: "Repositories",
                    Subtitle: response.Query,
                    Summary: string.Join("\n", lines),
                    Importance: 90));
            }
        }

        // Users
        var usersGroup = response.Groups.FirstOrDefault(g => g.Domain == ComposeSearchDomain.Users);
        if (usersGroup is not null && usersGroup.Items.Count > 0)
        {
            var lines = new List<string>();
            foreach (var item in usersGroup.Items.OfType<UserSearchItem>().Take(take))
            {
                var u = item.User;
                var displayName = string.IsNullOrWhiteSpace(u.Name) ? string.Empty : $" • {u.Name.Trim()}";
                lines.Add($"{u.Login}{displayName}");
            }

            if (lines.Count > 0)
            {
                cards.Add(new DashboardCardModel(
                    CardId: DashboardCardId.ComposeSearchUsers,
                    Kind: DashboardCardKind.ComposeSearchUsers,
                    Title: "Users",
                    Subtitle: response.Query,
                    Summary: string.Join("\n", lines),
                    Importance: 85));
            }
        }

        // Code
        var codeGroup = response.Groups.FirstOrDefault(g => g.Domain == ComposeSearchDomain.Code);
        if (codeGroup is not null && codeGroup.Items.Count > 0)
        {
            var lines = new List<string>();
            foreach (var item in codeGroup.Items.OfType<CodeSearchItem>().Take(take))
            {
                var c = item.Code;
                var repo = $"{c.Repo.Owner}/{c.Repo.Name}";
                var path = string.IsNullOrWhiteSpace(c.Path) ? "(no path)" : c.Path.Trim();
                lines.Add($"{repo} • {path}");
            }

            if (lines.Count > 0)
            {
                cards.Add(new DashboardCardModel(
                    CardId: DashboardCardId.ComposeSearchCode,
                    Kind: DashboardCardKind.ComposeSearchCode,
                    Title: "Code",
                    Subtitle: response.Query,
                    Summary: string.Join("\n", lines),
                    Importance: 80));
            }
        }

        return cards;
    }
}
