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

        if (!string.IsNullOrWhiteSpace(query.SearchText) || query.Sort is not null || query.Direction is not null)
        {
            var q = BuildSearchQuery(repo, query);
            var request = new SearchIssuesRequest(q)
            {
                PerPage = page.PageSize,
                Page = page.PageNumber.GetValueOrDefault(1),
            };

            ApplySearchSorting(request, query);

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

    public async Task<IReadOnlyList<OctokitWorkItemData>> SearchIssuesAsync(IssueSearchQuery query, PageRequest page, CancellationToken ct)
    {
        if (page.Cursor is not null)
        {
            throw new NotSupportedException("Cursor-based pagination is not supported by the Octokit provider.");
        }

        if (string.IsNullOrWhiteSpace(query.Query))
        {
            return Array.Empty<OctokitWorkItemData>();
        }

        var client = await _clientFactory.CreateAsync(ct).ConfigureAwait(false);

        var request = new SearchIssuesRequest(query.Query.Trim())
        {
            PerPage = page.PageSize,
            Page = page.PageNumber.GetValueOrDefault(1),
        };

        ApplySearchSorting(request, query.Sort, query.Direction);

        var result = await client.Search.SearchIssues(request).ConfigureAwait(false);

        return result.Items
            .Select(i =>
            {
                var repo = TryParseRepoKeyFromSearchItem(i) ?? new RepoKey(string.Empty, string.Empty);
                var isPullRequest = TryGetIsPullRequest(i);

                return new OctokitWorkItemData(
                    Id: i.Id,
                    Repo: repo,
                    Number: i.Number,
                    Title: i.Title,
                    IsPullRequest: isPullRequest,
                    State: i.State.ToString(),
                    AuthorLogin: i.User?.Login,
                    CommentCount: i.Comments,
                    UpdatedAt: i.UpdatedAt);
            })
            .ToArray();
    }

    public async Task<OctokitIssueDetailData?> GetIssueAsync(RepoKey repo, int issueNumber, CancellationToken ct)
    {
        var client = await _clientFactory.CreateAsync(ct).ConfigureAwait(false);

        var issue = await client.Issue.Get(repo.Owner, repo.Name, issueNumber).ConfigureAwait(false);

        return new OctokitIssueDetailData(
            Id: issue.Id,
            Number: issue.Number,
            Title: issue.Title,
            State: issue.State.ToString(),
            AuthorLogin: issue.User?.Login,
            Body: issue.Body,
            CommentCount: issue.Comments,
            CreatedAt: issue.CreatedAt,
            UpdatedAt: issue.UpdatedAt);
    }

    public async Task<IReadOnlyList<OctokitIssueCommentData>> GetIssueCommentsAsync(RepoKey repo, int issueNumber, PageRequest page, CancellationToken ct)
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

        var comments = await client.Issue.Comment.GetAllForIssue(repo.Owner, repo.Name, issueNumber, apiOptions).ConfigureAwait(false);

        return comments
            .Select(c => new OctokitIssueCommentData(
                Id: c.Id,
                AuthorLogin: c.User?.Login,
                Body: c.Body,
                CreatedAt: c.CreatedAt,
                UpdatedAt: c.UpdatedAt))
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

    private static void ApplySearchSorting(SearchIssuesRequest request, IssueQuery query)
    {
        ApplySearchSorting(request, query.Sort, query.Direction);
    }

    private static void ApplySearchSorting(SearchIssuesRequest request, IssueSortField? sort, IssueSortDirection? direction)
    {
        if (sort is null && direction is null)
        {
            return;
        }

        var requestType = request.GetType();

        if (sort is not null)
        {
            var sortProperty = requestType.GetProperty("SortField") ?? requestType.GetProperty("Sort");
            if (sortProperty is not null && sortProperty.CanWrite)
            {
                var sortValueName = sort.Value switch
                {
                    IssueSortField.Created => "Created",
                    IssueSortField.Updated => "Updated",
                    IssueSortField.Comments => "Comments",
                    _ => "Updated",
                };

                TrySetEnumValue(request, sortProperty, sortValueName);
            }
        }

        if (direction is not null)
        {
            var orderProperty = requestType.GetProperty("Order") ?? requestType.GetProperty("Direction");
            if (orderProperty is not null && orderProperty.CanWrite)
            {
                var orderValueName = direction.Value switch
                {
                    IssueSortDirection.Asc => "Ascending",
                    IssueSortDirection.Desc => "Descending",
                    _ => "Descending",
                };

                if (!TrySetEnumValue(request, orderProperty, orderValueName))
                {
                    // Some Octokit versions use Asc/Desc.
                    var alt = direction.Value == IssueSortDirection.Asc ? "Asc" : "Desc";
                    _ = TrySetEnumValue(request, orderProperty, alt);
                }
            }
        }
    }

    private static RepoKey? TryParseRepoKeyFromSearchItem(object item)
    {
        Uri? repoUri = TryGetUriProperty(item, "RepositoryUrl")
            ?? TryGetUriProperty(item, "Repository")
            ?? TryGetUriProperty(item, "Url")
            ?? TryGetUriProperty(item, "HtmlUrl");

        if (repoUri is null)
        {
            return null;
        }

        // Prefer parsing from API-style URLs: /repos/{owner}/{name}/...
        var segments = repoUri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i < segments.Length - 2; i++)
        {
            if (string.Equals(segments[i], "repos", StringComparison.OrdinalIgnoreCase))
            {
                return new RepoKey(segments[i + 1], segments[i + 2]);
            }
        }

        // Fallback for github.com/{owner}/{name}/...
        if (segments.Length >= 2 && string.Equals(repoUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return new RepoKey(segments[0], segments[1]);
        }

        return null;
    }

    private static Uri? TryGetUriProperty(object item, string propertyName)
    {
        try
        {
            var prop = item.GetType().GetProperty(propertyName);
            if (prop is null)
            {
                return null;
            }

            var value = prop.GetValue(item);
            if (value is Uri uri)
            {
                return uri;
            }

            if (value is string s && Uri.TryCreate(s, UriKind.Absolute, out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static bool TryGetIsPullRequest(object item)
    {
        try
        {
            var prop = item.GetType().GetProperty("PullRequest");
            if (prop is null)
            {
                return false;
            }

            return prop.GetValue(item) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetEnumValue(object target, System.Reflection.PropertyInfo property, string valueName)
    {
        try
        {
            var enumType = property.PropertyType;
            if (!enumType.IsEnum)
            {
                return false;
            }

            var parsed = Enum.Parse(enumType, valueName, ignoreCase: true);
            property.SetValue(target, parsed);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
