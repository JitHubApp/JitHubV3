using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Octokit.Mapping;
using Octokit;

namespace JitHub.GitHub.Octokit.Services;

internal sealed class OctokitGitHubDataSource : IGitHubDataSource
{
    private readonly IOctokitClientFactory _clientFactory;

    public OctokitGitHubDataSource(IOctokitClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public async Task<IReadOnlyList<OctokitRepositoryData>> GetMyRepositoriesAsync(CancellationToken ct)
    {
        var client = await _clientFactory.CreateAsync(ct).ConfigureAwait(false);

        var request = new RepositoryRequest
        {
            // Align with the POC: show repositories the user can access.
            Affiliation = RepositoryAffiliation.Owner | RepositoryAffiliation.Collaborator | RepositoryAffiliation.OrganizationMember,
        };

        var repos = await client.Repository.GetAllForCurrent(request).ConfigureAwait(false);

        return repos
            .Select(r => new OctokitRepositoryData(
                Id: r.Id,
                Name: r.Name,
                OwnerLogin: r.Owner?.Login,
                IsPrivate: r.Private,
                DefaultBranch: r.DefaultBranch,
                Description: r.Description,
                UpdatedAt: r.UpdatedAt))
            .ToArray();
    }

    public async Task<IReadOnlyList<OctokitIssueData>> GetIssuesAsync(RepoKey repo, IssueQuery query, PageRequest page, CancellationToken ct)
    {
        if (page.Cursor is not null)
        {
            throw new NotSupportedException("Cursor-based pagination is not supported by the Octokit provider.");
        }

        var client = await _clientFactory.CreateAsync(ct).ConfigureAwait(false);

        var apiOptions = new ApiOptions
        {
            PageCount = 1,
            PageSize = page.PageSize,
            StartPage = page.PageNumber.GetValueOrDefault(1),
        };

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var q = BuildSearchQuery(repo, query);
            var request = new SearchIssuesRequest(q)
            {
                PerPage = page.PageSize,
                Page = page.PageNumber.GetValueOrDefault(1),
            };

            var result = await client.Search.SearchIssues(request).ConfigureAwait(false);

            return result.Items
                .Select(i => new OctokitIssueData(
                    Id: i.Id,
                    Number: i.Number,
                    Title: i.Title,
                    State: i.State.ToString(),
                    AuthorLogin: i.User?.Login,
                    CommentCount: i.Comments,
                    UpdatedAt: i.UpdatedAt))
                .ToArray();
        }

        var issueRequest = new RepositoryIssueRequest
        {
            State = query.State switch
            {
                IssueStateFilter.Open => ItemStateFilter.Open,
                IssueStateFilter.Closed => ItemStateFilter.Closed,
                IssueStateFilter.All => ItemStateFilter.All,
                _ => ItemStateFilter.Open,
            },
        };

        var issues = await client.Issue.GetAllForRepository(repo.Owner, repo.Name, issueRequest, apiOptions).ConfigureAwait(false);

        return issues
            .Select(i => new OctokitIssueData(
                Id: i.Id,
                Number: i.Number,
                Title: i.Title,
                State: i.State.ToString(),
                AuthorLogin: i.User?.Login,
                CommentCount: i.Comments,
                UpdatedAt: i.UpdatedAt))
            .ToArray();
    }

    private static string BuildSearchQuery(RepoKey repo, IssueQuery query)
    {
        var terms = new List<string>
        {
            $"repo:{repo.Owner}/{repo.Name}",
            "is:issue",
        };

        terms.Add(query.State switch
        {
            IssueStateFilter.Open => "state:open",
            IssueStateFilter.Closed => "state:closed",
            IssueStateFilter.All => "state:open state:closed",
            _ => "state:open",
        });

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            terms.Add(query.SearchText.Trim());
        }

        return string.Join(" ", terms);
    }
}
