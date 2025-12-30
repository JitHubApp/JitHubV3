using FluentAssertions;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Octokit.Services;

namespace JitHub.GitHub.Tests;

public sealed class GitHubIssueSearchCacheKeyTests
{
    [Test]
    public void SearchIssuesKey_TrimsQuery_AndIncludesSortAndDirection()
    {
        var query = new IssueSearchQuery(
            Query: "  is:open assignee:@me  ",
            Sort: IssueSortField.Updated,
            Direction: IssueSortDirection.Desc);

        var page = PageRequest.FromPageNumber(2, pageSize: 25);

        var key = GitHubCacheKeys.SearchIssues(query, page);

        key.Operation.Should().Be("github.issues.search");

        key.Parameters.Should().Contain(p => p.Key == "q" && p.Value == "is:open assignee:@me");
        key.Parameters.Should().Contain(p => p.Key == "sort" && p.Value == "Updated");
        key.Parameters.Should().Contain(p => p.Key == "direction" && p.Value == "Desc");
        key.Parameters.Should().Contain(p => p.Key == "pageSize" && p.Value == "25");
        key.Parameters.Should().Contain(p => p.Key == "pageNumber" && p.Value == "2");
    }
}
