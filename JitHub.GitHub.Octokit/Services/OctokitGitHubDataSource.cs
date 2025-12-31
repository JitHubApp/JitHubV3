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

    public async Task<OctokitRepositoryDetailData?> GetRepositoryAsync(RepoKey repo, CancellationToken ct)
    {
        var client = await _clientFactory.CreateAsync(ct).ConfigureAwait(false);

        var r = await client.Repository.Get(repo.Owner, repo.Name).ConfigureAwait(false);

        return new OctokitRepositoryDetailData(
            Repo: repo,
            IsPrivate: r.Private,
            DefaultBranch: r.DefaultBranch,
            Description: r.Description,
            UpdatedAt: r.UpdatedAt,
            StargazersCount: r.StargazersCount,
            ForksCount: r.ForksCount,
            WatchersCount: r.SubscribersCount);
    }

    public async Task<IReadOnlyList<OctokitActivityEventData>> GetMyActivityAsync(PageRequest page, CancellationToken ct)
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

        // Events API needs a username.
        var me = await client.User.Current().ConfigureAwait(false);
        var login = me.Login;

        var eventsClient = client.Activity.Events;
        var raw = await InvokeToObjectListAsync(eventsClient, "GetAllUserPerformed", login, apiOptions).ConfigureAwait(false);

        return raw.Select(MapActivityEvent).ToArray();
    }

    public async Task<IReadOnlyList<OctokitActivityEventData>> GetRepoActivityAsync(RepoKey repo, PageRequest page, CancellationToken ct)
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

        var eventsClient = client.Activity.Events;
        var raw = await InvokeToObjectListAsync(eventsClient, "GetAllForRepository", repo.Owner, repo.Name, apiOptions).ConfigureAwait(false);

        return raw.Select(MapActivityEvent).ToArray();
    }

    public async Task<IReadOnlyList<OctokitNotificationData>> GetMyNotificationsAsync(bool unreadOnly, PageRequest page, CancellationToken ct)
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

        var notifications = await GetNotificationsCompatAsync(client, unreadOnly, apiOptions).ConfigureAwait(false);

        return notifications
            .Select(n => new OctokitNotificationData(
                Id: TryGetNotificationId(n),
                Repo: new RepoKey(n.Repository?.Owner?.Login ?? string.Empty, n.Repository?.Name ?? string.Empty),
                Title: n.Subject?.Title ?? string.Empty,
                Type: n.Subject?.Type,
                UpdatedAt: TryGetUpdatedAt(n),
                Unread: n.Unread))
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

    private static async Task<IReadOnlyList<Notification>> GetNotificationsCompatAsync(IGitHubClient client, bool unreadOnly, ApiOptions apiOptions)
    {
        // Octokit surface differs across versions. Prefer typed request if available; otherwise fall back to reflection.
        try
        {
            var request = new NotificationsRequest
            {
                All = !unreadOnly,
                Participating = false,
            };

            var result = await client.Activity.Notifications.GetAllForCurrent(request, apiOptions).ConfigureAwait(false);
            return result;
        }
        catch
        {
            // Fallback: try to invoke GetAllForCurrent(...) overloads via reflection.
            var notificationsClient = client.Activity.Notifications;
            var type = notificationsClient.GetType();

            var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, "GetAllForCurrent", StringComparison.Ordinal))
                .ToArray();

            // Try (ApiOptions)
            var mApiOnly = methods.FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return p.Length == 1 && p[0].ParameterType == typeof(ApiOptions);
            });

            if (mApiOnly is not null)
            {
                var task = (Task)mApiOnly.Invoke(notificationsClient, new object?[] { apiOptions })!;
                await task.ConfigureAwait(false);
                return ExtractTaskResult<IReadOnlyList<Notification>>(task) ?? Array.Empty<Notification>();
            }

            // Try (object request, ApiOptions)
            var mReqApi = methods.FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return p.Length == 2 && p[1].ParameterType == typeof(ApiOptions);
            });

            if (mReqApi is not null)
            {
                var requestType = mReqApi.GetParameters()[0].ParameterType;
                var req = Activator.CreateInstance(requestType);
                if (req is not null)
                {
                    TrySetBoolProperty(req, "All", !unreadOnly);
                    TrySetBoolProperty(req, "Participating", false);
                }

                var task = (Task)mReqApi.Invoke(notificationsClient, new[] { req, apiOptions })!;
                await task.ConfigureAwait(false);
                return ExtractTaskResult<IReadOnlyList<Notification>>(task) ?? Array.Empty<Notification>();
            }

            // Last resort: no apiOptions.
            var mNoArgs = methods.FirstOrDefault(m => m.GetParameters().Length == 0);
            if (mNoArgs is not null)
            {
                var task = (Task)mNoArgs.Invoke(notificationsClient, Array.Empty<object?>())!;
                await task.ConfigureAwait(false);
                var result = ExtractTaskResult<IReadOnlyList<Notification>>(task) ?? Array.Empty<Notification>();
                return unreadOnly ? result.Where(n => n.Unread).ToArray() : result;
            }

            return Array.Empty<Notification>();
        }
    }

    private static void TrySetBoolProperty(object target, string propertyName, bool value)
    {
        try
        {
            var prop = target.GetType().GetProperty(propertyName);
            if (prop is not null && prop.CanWrite && prop.PropertyType == typeof(bool))
            {
                prop.SetValue(target, value);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static T? ExtractTaskResult<T>(Task task) where T : class
    {
        try
        {
            var prop = task.GetType().GetProperty("Result");
            return prop?.GetValue(task) as T;
        }
        catch
        {
            return null;
        }
    }

    private static string TryGetNotificationId(Notification n)
    {
        try
        {
            var prop = n.GetType().GetProperty("Id");
            var value = prop?.GetValue(n);
            return value?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static DateTimeOffset? TryGetUpdatedAt(Notification n)
    {
        try
        {
            var prop = n.GetType().GetProperty("UpdatedAt") ?? n.GetType().GetProperty("LastReadAt");
            var value = prop?.GetValue(n);
            return value as DateTimeOffset?;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<object>> InvokeToObjectListAsync(object target, string methodName, params object[] args)
    {
        try
        {
            var methods = target.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                .ToArray();

            var match = methods.FirstOrDefault(m => ParametersMatch(m.GetParameters(), args));
            if (match is null)
            {
                return Array.Empty<object>();
            }

            var invoked = match.Invoke(target, args);
            if (invoked is not Task task)
            {
                return Array.Empty<object>();
            }

            await task.ConfigureAwait(false);

            var result = ExtractTaskResult<object>(task);
            if (result is System.Collections.IEnumerable enumerable)
            {
                var list = new List<object>();
                foreach (var item in enumerable)
                {
                    if (item is not null)
                    {
                        list.Add(item);
                    }
                }
                return list;
            }

            return Array.Empty<object>();
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    private static bool ParametersMatch(System.Reflection.ParameterInfo[] parameters, object[] args)
    {
        if (parameters.Length != args.Length)
        {
            return false;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var arg = args[i];
            if (arg is null)
            {
                return false;
            }

            var pType = parameters[i].ParameterType;
            if (!pType.IsInstanceOfType(arg))
            {
                return false;
            }
        }

        return true;
    }

    private static OctokitActivityEventData MapActivityEvent(object e)
    {
        var id = TryGetStringProperty(e, "Id");
        var type = TryGetStringProperty(e, "Type");
        var createdAt = TryGetDateTimeOffsetProperty(e, "CreatedAt") ?? DateTimeOffset.MinValue;

        var actorLogin = TryGetNestedStringProperty(e, "Actor", "Login")
            ?? TryGetNestedStringProperty(e, "Actor", "Name");

        var repoFullName = TryGetNestedStringProperty(e, "Repo", "Name")
            ?? TryGetNestedStringProperty(e, "Repository", "FullName")
            ?? TryGetNestedStringProperty(e, "Repository", "Name");

        var repo = TryParseRepoKey(repoFullName);

        // Avoid exposing raw payloads; keep this field for future UI-friendly summaries.
        var description = (string?)null;

        return new OctokitActivityEventData(
            Id: id,
            Repo: repo,
            Type: type,
            ActorLogin: actorLogin,
            Description: description,
            CreatedAt: createdAt);
    }

    private static string? TryGetStringProperty(object target, string propertyName)
    {
        try
        {
            var prop = target.GetType().GetProperty(propertyName);
            var value = prop?.GetValue(target);
            return value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? TryGetDateTimeOffsetProperty(object target, string propertyName)
    {
        try
        {
            var prop = target.GetType().GetProperty(propertyName);
            var value = prop?.GetValue(target);
            return value as DateTimeOffset?;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetNestedStringProperty(object target, string objectPropertyName, string nestedPropertyName)
    {
        try
        {
            var objProp = target.GetType().GetProperty(objectPropertyName);
            var obj = objProp?.GetValue(target);
            if (obj is null)
            {
                return null;
            }

            var nestedProp = obj.GetType().GetProperty(nestedPropertyName);
            var value = nestedProp?.GetValue(obj);
            return value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static RepoKey? TryParseRepoKey(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }

        var parts = fullName.Trim().Split('/');
        if (parts.Length != 2)
        {
            return null;
        }

        try
        {
            return new RepoKey(parts[0], parts[1]);
        }
        catch
        {
            return null;
        }
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

    public async Task<IReadOnlyList<OctokitRepositoryData>> SearchRepositoriesAsync(RepoSearchQuery query, PageRequest page, CancellationToken ct)
    {
        if (page.Cursor is not null)
        {
            throw new NotSupportedException("Cursor-based pagination is not supported by the Octokit provider.");
        }

        if (string.IsNullOrWhiteSpace(query.Query))
        {
            return Array.Empty<OctokitRepositoryData>();
        }

        var client = await _clientFactory.CreateAsync(ct).ConfigureAwait(false);

        var request = new SearchRepositoriesRequest(query.Query.Trim())
        {
            PerPage = page.PageSize,
            Page = page.PageNumber.GetValueOrDefault(1),
        };

        ApplySearchSorting(request, query.Sort, query.Direction);

        // Octokit surface differs across versions (SearchRepo vs SearchRepositories). Try both.
        var items = await InvokeSearchItemsAsync<Repository>(client.Search, request, new[] { "SearchRepositories", "SearchRepo" }).ConfigureAwait(false);

        return items
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

    public async Task<IReadOnlyList<OctokitUserData>> SearchUsersAsync(UserSearchQuery query, PageRequest page, CancellationToken ct)
    {
        if (page.Cursor is not null)
        {
            throw new NotSupportedException("Cursor-based pagination is not supported by the Octokit provider.");
        }

        if (string.IsNullOrWhiteSpace(query.Query))
        {
            return Array.Empty<OctokitUserData>();
        }

        var client = await _clientFactory.CreateAsync(ct).ConfigureAwait(false);

        var request = new SearchUsersRequest(query.Query.Trim())
        {
            PerPage = page.PageSize,
            Page = page.PageNumber.GetValueOrDefault(1),
        };

        ApplySearchSorting(request, query.Sort, query.Direction);

        var items = await InvokeSearchItemsAsync<User>(client.Search, request, new[] { "SearchUsers", "SearchUser" }).ConfigureAwait(false);

        return items
            .Select(u => new OctokitUserData(
                Id: u.Id,
                Login: u.Login,
                Name: null,
                Bio: null,
                Url: u.HtmlUrl))
            .ToArray();
    }

    public async Task<IReadOnlyList<OctokitCodeSearchItemData>> SearchCodeAsync(CodeSearchQuery query, PageRequest page, CancellationToken ct)
    {
        if (page.Cursor is not null)
        {
            throw new NotSupportedException("Cursor-based pagination is not supported by the Octokit provider.");
        }

        if (string.IsNullOrWhiteSpace(query.Query))
        {
            return Array.Empty<OctokitCodeSearchItemData>();
        }

        var client = await _clientFactory.CreateAsync(ct).ConfigureAwait(false);

        var request = new SearchCodeRequest(query.Query.Trim())
        {
            PerPage = page.PageSize,
            Page = page.PageNumber.GetValueOrDefault(1),
        };

        ApplySearchSorting(request, query.Sort, query.Direction);

        var items = await InvokeSearchItemsAsync<SearchCode>(client.Search, request, new[] { "SearchCode" }).ConfigureAwait(false);

        return items
            .Select(i =>
            {
                var repoKey = TryParseRepoKey(i.Repository?.FullName) ?? new RepoKey(string.Empty, string.Empty);
                return new OctokitCodeSearchItemData(
                    Path: i.Path ?? string.Empty,
                    Repo: repoKey,
                    Sha: i.Sha,
                    Url: i.HtmlUrl);
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

    private static void ApplySearchSorting(SearchRepositoriesRequest request, RepoSortField? sort, RepoSortDirection? direction)
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
                    RepoSortField.Stars => "Stars",
                    RepoSortField.Forks => "Forks",
                    RepoSortField.Updated => "Updated",
                    _ => "BestMatch",
                };

                TrySetEnumPropertyValue(sortProperty, request, sortValueName);
            }
        }

        if (direction is not null)
        {
            var orderProperty = requestType.GetProperty("Order") ?? requestType.GetProperty("SortDirection") ?? requestType.GetProperty("Direction");
            if (orderProperty is not null && orderProperty.CanWrite)
            {
                var orderValueName = direction.Value == RepoSortDirection.Asc ? "Ascending" : "Descending";
                TrySetEnumPropertyValue(orderProperty, request, orderValueName);
            }
        }
    }

    private static void ApplySearchSorting(SearchUsersRequest request, UserSortField? sort, UserSortDirection? direction)
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
                    UserSortField.Followers => "Followers",
                    UserSortField.Repositories => "Repositories",
                    UserSortField.Joined => "Joined",
                    _ => "BestMatch",
                };

                TrySetEnumPropertyValue(sortProperty, request, sortValueName);
            }
        }

        if (direction is not null)
        {
            var orderProperty = requestType.GetProperty("Order") ?? requestType.GetProperty("SortDirection") ?? requestType.GetProperty("Direction");
            if (orderProperty is not null && orderProperty.CanWrite)
            {
                var orderValueName = direction.Value == UserSortDirection.Asc ? "Ascending" : "Descending";
                TrySetEnumPropertyValue(orderProperty, request, orderValueName);
            }
        }
    }

    private static void ApplySearchSorting(SearchCodeRequest request, CodeSortField? sort, CodeSortDirection? direction)
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
                    CodeSortField.Indexed => "Indexed",
                    _ => "BestMatch",
                };

                TrySetEnumPropertyValue(sortProperty, request, sortValueName);
            }
        }

        if (direction is not null)
        {
            var orderProperty = requestType.GetProperty("Order") ?? requestType.GetProperty("SortDirection") ?? requestType.GetProperty("Direction");
            if (orderProperty is not null && orderProperty.CanWrite)
            {
                var orderValueName = direction.Value == CodeSortDirection.Asc ? "Ascending" : "Descending";
                TrySetEnumPropertyValue(orderProperty, request, orderValueName);
            }
        }
    }

    private static void TrySetEnumPropertyValue(System.Reflection.PropertyInfo property, object target, string enumValueName)
    {
        try
        {
            var enumValue = Enum.Parse(property.PropertyType, enumValueName, ignoreCase: true);
            property.SetValue(target, enumValue);
        }
        catch
        {
            // ignore
        }
    }

    private static async Task<IReadOnlyList<TItem>> InvokeSearchItemsAsync<TItem>(object searchClient, object request, IReadOnlyList<string> methodNames)
        where TItem : class
    {
        foreach (var methodName in methodNames)
        {
            try
            {
                var methods = searchClient.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                    .ToArray();

                var match = methods.FirstOrDefault(m => ParametersMatch(m.GetParameters(), new[] { request }));
                if (match is null)
                {
                    continue;
                }

                var invoked = match.Invoke(searchClient, new[] { request });
                if (invoked is not Task task)
                {
                    continue;
                }

                await task.ConfigureAwait(false);

                var result = ExtractTaskResult<object>(task);
                if (result is null)
                {
                    continue;
                }

                var itemsProp = result.GetType().GetProperty("Items");
                var itemsObj = itemsProp?.GetValue(result);
                if (itemsObj is not System.Collections.IEnumerable enumerable)
                {
                    continue;
                }

                var list = new List<TItem>();
                foreach (var item in enumerable)
                {
                    if (item is TItem typed)
                    {
                        list.Add(typed);
                    }
                }

                return list;
            }
            catch
            {
                // ignore and try next method name
            }
        }

        return Array.Empty<TItem>();
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
