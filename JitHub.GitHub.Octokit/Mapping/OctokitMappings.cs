using JitHub.GitHub.Abstractions.Models;

namespace JitHub.GitHub.Octokit.Mapping;

public static class OctokitMappings
{
    public static RepositorySummary ToRepositorySummary(OctokitRepositoryData repo)
    {
        if (repo is null)
        {
            throw new ArgumentNullException(nameof(repo));
        }

        return new RepositorySummary(
            Id: repo.Id,
            Name: repo.Name ?? string.Empty,
            OwnerLogin: repo.OwnerLogin ?? string.Empty,
            IsPrivate: repo.IsPrivate,
            DefaultBranch: repo.DefaultBranch,
            Description: repo.Description,
            UpdatedAt: repo.UpdatedAt);
    }

    public static IssueSummary ToIssueSummary(OctokitIssueData issue)
    {
        if (issue is null)
        {
            throw new ArgumentNullException(nameof(issue));
        }

        return new IssueSummary(
            Id: issue.Id,
            Number: issue.Number,
            Title: issue.Title ?? string.Empty,
            State: ParseIssueState(issue.State),
            AuthorLogin: issue.AuthorLogin,
            CommentCount: issue.CommentCount,
            UpdatedAt: issue.UpdatedAt);
    }

    private static IssueState ParseIssueState(string? state)
    {
        if (string.Equals(state, "closed", StringComparison.OrdinalIgnoreCase))
        {
            return IssueState.Closed;
        }

        return IssueState.Open;
    }
}
