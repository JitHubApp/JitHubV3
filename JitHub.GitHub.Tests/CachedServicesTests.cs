using FluentAssertions;
using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Polling;
using JitHub.GitHub.Abstractions.Queries;
using JitHub.GitHub.Abstractions.Refresh;
using JitHub.GitHub.Octokit.Mapping;
using JitHub.GitHub.Octokit.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace JitHub.GitHub.Tests;

public sealed class CachedServicesTests
{
    [Test]
    public async Task RepoService_cache_only_returns_cached_value()
    {
        var cache = new CacheRuntime(new InMemoryCacheStore(), new CacheEventBus());

        var dataSource = new FakeGitHubDataSource
        {
            Repositories =
            [
                new OctokitRepositoryData(1, "repo1", "me", true, "main", null, DateTimeOffset.UtcNow),
            ]
        };

        var sut = new CachedGitHubRepositoryService(cache, dataSource, NullLogger<CachedGitHubRepositoryService>.Instance);

        var first = await sut.GetMyRepositoriesAsync(RefreshMode.ForceRefresh, CancellationToken.None);
        first.Should().HaveCount(1);
        dataSource.GetMyRepositoriesCallCount.Should().Be(1);

        var cached = await sut.GetMyRepositoriesAsync(RefreshMode.CacheOnly, CancellationToken.None);
        cached.Should().HaveCount(1);
        dataSource.GetMyRepositoriesCallCount.Should().Be(1);
    }

    [Test]
    public async Task IssueService_cache_key_varies_by_state()
    {
        var cache = new CacheRuntime(new InMemoryCacheStore(), new CacheEventBus());

        var dataSource = new FakeGitHubDataSource
        {
            IssuesFactory = (_, query, _, _) =>
            {
                var state = query.State == IssueStateFilter.Closed ? "closed" : "open";
                return Task.FromResult<IReadOnlyList<OctokitIssueData>>(
                [
                    new OctokitIssueData(10, 1, $"{state} issue", state, "me", 0, DateTimeOffset.UtcNow),
                ]);
            }
        };

        var sut = new CachedGitHubIssueService(cache, dataSource);
        var repo = new RepoKey("octo", "hello");
        var page = PageRequest.FirstPage(30);

        var open = await sut.GetIssuesAsync(repo, new IssueQuery(IssueStateFilter.Open), page, RefreshMode.ForceRefresh, CancellationToken.None);
        open.Items.Should().ContainSingle();

        var closed = await sut.GetIssuesAsync(repo, new IssueQuery(IssueStateFilter.Closed), page, RefreshMode.ForceRefresh, CancellationToken.None);
        closed.Items.Should().ContainSingle();

        dataSource.GetIssuesCallCount.Should().Be(2);

        // CacheOnly should not call datasource again
        _ = await sut.GetIssuesAsync(repo, new IssueQuery(IssueStateFilter.Open), page, RefreshMode.CacheOnly, CancellationToken.None);
        dataSource.GetIssuesCallCount.Should().Be(2);
    }

    [Test]
    public async Task IssueService_cache_key_varies_by_page_number()
    {
        var cache = new CacheRuntime(new InMemoryCacheStore(), new CacheEventBus());

        var now = DateTimeOffset.UtcNow;
        var dataSource = new FakeGitHubDataSource
        {
            IssuesFactory = (_, _, page, _) =>
            {
                var n = page.PageNumber ?? 0;
                return Task.FromResult<IReadOnlyList<OctokitIssueData>>(
                [
                    new OctokitIssueData(Id: n, Number: n, Title: $"page {n}", State: "open", AuthorLogin: "me", CommentCount: 0, UpdatedAt: now),
                ]);
            }
        };

        var sut = new CachedGitHubIssueService(cache, dataSource);
        var repo = new RepoKey("octo", "hello");
        var query = new IssueQuery(IssueStateFilter.Open);

        var p1 = PageRequest.FromPageNumber(1, pageSize: 30);
        var p2 = PageRequest.FromPageNumber(2, pageSize: 30);

        var r1 = await sut.GetIssuesAsync(repo, query, p1, RefreshMode.ForceRefresh, CancellationToken.None);
        var r2 = await sut.GetIssuesAsync(repo, query, p2, RefreshMode.ForceRefresh, CancellationToken.None);

        dataSource.GetIssuesCallCount.Should().Be(2);

        var cached1 = await sut.GetIssuesAsync(repo, query, p1, RefreshMode.CacheOnly, CancellationToken.None);
        cached1.Items.Should().ContainSingle();

        cached1.Items[0].Id.Should().Be(r1.Items[0].Id);
        cached1.Items[0].Id.Should().NotBe(r2.Items[0].Id);
    }

    [Test]
    public void IssueService_cursor_paging_is_not_supported()
    {
        var cache = new CacheRuntime(new InMemoryCacheStore(), new CacheEventBus());
        var dataSource = new FakeGitHubDataSource();
        var sut = new CachedGitHubIssueService(cache, dataSource);

        var repo = new RepoKey("octo", "hello");
        var query = new IssueQuery(IssueStateFilter.Open);
        var page = PageRequest.FromCursor("abc", pageSize: 30);

        Func<Task> act = () => sut.GetIssuesAsync(repo, query, page, RefreshMode.ForceRefresh, CancellationToken.None);
        act.Should().ThrowAsync<NotSupportedException>();
    }

    [Test]
    public async Task Polling_populates_same_cache_entry_as_issue_service()
    {
        var events = new CacheEventBus();
        var cache = new CacheRuntime(new InMemoryCacheStore(), events);

        var dataSource = new FakeGitHubDataSource
        {
            IssuesFactory = (_, _, _, _) =>
                Task.FromResult<IReadOnlyList<OctokitIssueData>>(
                [
                    new OctokitIssueData(1, 42, "Hello", "open", "me", 1, DateTimeOffset.UtcNow),
                ])
        };

        var issueService = new CachedGitHubIssueService(cache, dataSource);
        var pollingService = new CachedGitHubIssuePollingService(cache, dataSource, NullLogger<CachedGitHubIssuePollingService>.Instance);

        var repo = new RepoKey("octo", "hello");
        var query = new IssueQuery(IssueStateFilter.Open);
        var page = PageRequest.FirstPage(30);

        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sub = events.Subscribe(e =>
        {
            if (e.Kind == CacheEventKind.Updated && e.Key.Operation == "github.issues.list")
            {
                updated.TrySetResult();
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var pollingTask = pollingService.StartIssuesPollingAsync(
            repo,
            query,
            page,
            new PollingRequest(TimeSpan.FromMilliseconds(1)),
            cts.Token);

        await updated.Task;
        cts.Cancel();

        // Polling may complete as canceled.
        try { await pollingTask; } catch (OperationCanceledException) { }

        var cached = await issueService.GetIssuesAsync(repo, query, page, RefreshMode.CacheOnly, CancellationToken.None);
        cached.Items.Should().ContainSingle(i => i.Number == 42);
    }

    [Test]
    public async Task Polling_populates_same_cache_entry_as_notification_service()
    {
        var events = new CacheEventBus();
        var cache = new CacheRuntime(new InMemoryCacheStore(), events);

        var dataSource = new FakeGitHubDataSource
        {
            NotificationsFactory = (_, _, _) =>
                Task.FromResult<IReadOnlyList<OctokitNotificationData>>(
                [
                    new OctokitNotificationData(
                        Id: "n1",
                        Repo: new RepoKey("octo", "hello"),
                        Title: "Build is failing",
                        Type: "Issue",
                        UpdatedAt: DateTimeOffset.UtcNow,
                        Unread: true),
                ])
        };

        var notificationService = new CachedGitHubNotificationService(cache, dataSource);
        var pollingService = new CachedGitHubNotificationPollingService(cache, dataSource, NullLogger<CachedGitHubNotificationPollingService>.Instance);

        var page = PageRequest.FirstPage(pageSize: 5);

        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sub = events.Subscribe(e =>
        {
            if (e.Kind == CacheEventKind.Updated && e.Key.Operation == "github.notifications.mine")
            {
                updated.TrySetResult();
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var pollingTask = pollingService.StartNotificationsPollingAsync(
            unreadOnly: true,
            page,
            new PollingRequest(TimeSpan.FromMilliseconds(1)),
            cts.Token);

        await updated.Task;
        cts.Cancel();

        // Polling may complete as canceled.
        try { await pollingTask; } catch (OperationCanceledException) { }

        var cached = await notificationService.GetMyNotificationsAsync(unreadOnly: true, page, RefreshMode.CacheOnly, CancellationToken.None);
        cached.Items.Should().ContainSingle(n => n.Id == "n1");
    }

    [Test]
    public async Task ActivityService_cache_only_returns_cached_value()
    {
        var cache = new CacheRuntime(new InMemoryCacheStore(), new CacheEventBus());

        var now = DateTimeOffset.UtcNow;
        var dataSource = new FakeGitHubDataSource
        {
            MyActivityFactory = (page, _) =>
                Task.FromResult<IReadOnlyList<OctokitActivityEventData>>(
                [
                    new OctokitActivityEventData("e1", new RepoKey("octo", "hello"), "PushEvent", "me", null, now),
                ])
        };

        var sut = new CachedGitHubActivityService(cache, dataSource);

        var page = PageRequest.FirstPage(pageSize: 5);

        var first = await sut.GetMyActivityAsync(page, RefreshMode.ForceRefresh, CancellationToken.None);
        first.Items.Should().HaveCount(1);
        dataSource.GetMyActivityCallCount.Should().Be(1);

        var cached = await sut.GetMyActivityAsync(page, RefreshMode.CacheOnly, CancellationToken.None);
        cached.Items.Should().HaveCount(1);
        dataSource.GetMyActivityCallCount.Should().Be(1);
    }

    [Test]
    public async Task ActivityService_cache_key_varies_by_repo()
    {
        var cache = new CacheRuntime(new InMemoryCacheStore(), new CacheEventBus());

        var dataSource = new FakeGitHubDataSource
        {
            RepoActivityFactory = (repo, page, _) =>
            {
                var id = $"{repo.Owner}/{repo.Name}:{page.PageNumber}";
                return Task.FromResult<IReadOnlyList<OctokitActivityEventData>>(
                [
                    new OctokitActivityEventData(id, repo, "WatchEvent", "me", null, DateTimeOffset.UtcNow),
                ]);
            }
        };

        var sut = new CachedGitHubActivityService(cache, dataSource);
        var page = PageRequest.FromPageNumber(1, pageSize: 5);

        var repo1 = new RepoKey("octo", "hello");
        var repo2 = new RepoKey("octo", "world");

        var r1 = await sut.GetRepoActivityAsync(repo1, page, RefreshMode.ForceRefresh, CancellationToken.None);
        var r2 = await sut.GetRepoActivityAsync(repo2, page, RefreshMode.ForceRefresh, CancellationToken.None);

        dataSource.GetRepoActivityCallCount.Should().Be(2);
        r1.Items.Single().Id.Should().NotBe(r2.Items.Single().Id);

        _ = await sut.GetRepoActivityAsync(repo1, page, RefreshMode.CacheOnly, CancellationToken.None);
        dataSource.GetRepoActivityCallCount.Should().Be(2);
    }

    [Test]
    public async Task RepoDetailsService_cache_only_returns_cached_value()
    {
        var cache = new CacheRuntime(new InMemoryCacheStore(), new CacheEventBus());

        var now = DateTimeOffset.UtcNow;
        var dataSource = new FakeGitHubDataSource
        {
            RepoDetailsFactory = (repo, _) =>
                Task.FromResult<OctokitRepositoryDetailData?>(
                    new OctokitRepositoryDetailData(
                        Repo: repo,
                        IsPrivate: false,
                        DefaultBranch: "main",
                        Description: "Demo",
                        UpdatedAt: now,
                        StargazersCount: 12,
                        ForksCount: 3,
                        WatchersCount: 4))
        };

        var sut = new CachedGitHubRepositoryDetailsService(cache, dataSource);
        var repo = new RepoKey("octo", "hello");

        var first = await sut.GetRepositoryAsync(repo, RefreshMode.ForceRefresh, CancellationToken.None);
        first.Should().NotBeNull();
        dataSource.GetRepoDetailsCallCount.Should().Be(1);

        var cached = await sut.GetRepositoryAsync(repo, RefreshMode.CacheOnly, CancellationToken.None);
        cached.Should().NotBeNull();
        dataSource.GetRepoDetailsCallCount.Should().Be(1);
    }

    [Test]
    public async Task RepoSearchService_cache_key_varies_by_query()
    {
        var cache = new CacheRuntime(new InMemoryCacheStore(), new CacheEventBus());

        var dataSource = new FakeGitHubDataSource
        {
            SearchReposFactory = (q, _, _) =>
                Task.FromResult<IReadOnlyList<OctokitRepositoryData>>(
                [
                    new OctokitRepositoryData(
                        Id: q.Query.GetHashCode(),
                        Name: q.Query,
                        OwnerLogin: "me",
                        IsPrivate: false,
                        DefaultBranch: "main",
                        Description: null,
                        UpdatedAt: DateTimeOffset.UtcNow),
                ])
        };

        var sut = new CachedGitHubRepoSearchService(cache, dataSource);
        var page = PageRequest.FirstPage(pageSize: 5);

        var q1 = new RepoSearchQuery("uno platform");
        var q2 = new RepoSearchQuery("octokit");

        _ = await sut.SearchAsync(q1, page, RefreshMode.ForceRefresh, CancellationToken.None);
        _ = await sut.SearchAsync(q2, page, RefreshMode.ForceRefresh, CancellationToken.None);

        dataSource.SearchReposCallCount.Should().Be(2);

        _ = await sut.SearchAsync(q1, page, RefreshMode.CacheOnly, CancellationToken.None);
        dataSource.SearchReposCallCount.Should().Be(2);
    }

    [Test]
    public async Task UserSearchService_cache_key_varies_by_query()
    {
        var cache = new CacheRuntime(new InMemoryCacheStore(), new CacheEventBus());

        var dataSource = new FakeGitHubDataSource
        {
            SearchUsersFactory = (q, _, _) =>
                Task.FromResult<IReadOnlyList<OctokitUserData>>(
                [
                    new OctokitUserData(
                        Id: q.Query.GetHashCode(),
                        Login: q.Query,
                        Name: null,
                        Bio: null,
                        Url: "https://example.invalid"),
                ])
        };

        var sut = new CachedGitHubUserSearchService(cache, dataSource);
        var page = PageRequest.FirstPage(pageSize: 5);

        var q1 = new UserSearchQuery("octocat");
        var q2 = new UserSearchQuery("hubot");

        _ = await sut.SearchAsync(q1, page, RefreshMode.ForceRefresh, CancellationToken.None);
        _ = await sut.SearchAsync(q2, page, RefreshMode.ForceRefresh, CancellationToken.None);

        dataSource.SearchUsersCallCount.Should().Be(2);

        _ = await sut.SearchAsync(q1, page, RefreshMode.CacheOnly, CancellationToken.None);
        dataSource.SearchUsersCallCount.Should().Be(2);
    }

    [Test]
    public async Task CodeSearchService_cache_key_varies_by_query()
    {
        var cache = new CacheRuntime(new InMemoryCacheStore(), new CacheEventBus());

        var dataSource = new FakeGitHubDataSource
        {
            SearchCodeFactory = (q, _, _) =>
                Task.FromResult<IReadOnlyList<OctokitCodeSearchItemData>>(
                [
                    new OctokitCodeSearchItemData(
                        Path: q.Query,
                        Repo: new RepoKey("octo", "hello"),
                        Sha: "deadbeef",
                        Url: "https://example.invalid"),
                ])
        };

        var sut = new CachedGitHubCodeSearchService(cache, dataSource);
        var page = PageRequest.FirstPage(pageSize: 5);

        var q1 = new CodeSearchQuery("language:csharp HttpClient");
        var q2 = new CodeSearchQuery("language:csharp CancellationToken");

        _ = await sut.SearchAsync(q1, page, RefreshMode.ForceRefresh, CancellationToken.None);
        _ = await sut.SearchAsync(q2, page, RefreshMode.ForceRefresh, CancellationToken.None);

        dataSource.SearchCodeCallCount.Should().Be(2);

        _ = await sut.SearchAsync(q1, page, RefreshMode.CacheOnly, CancellationToken.None);
        dataSource.SearchCodeCallCount.Should().Be(2);
    }

    internal sealed class FakeGitHubDataSource : IGitHubDataSource
    {
        public IReadOnlyList<OctokitRepositoryData> Repositories { get; init; } = Array.Empty<OctokitRepositoryData>();

        public Func<PageRequest, CancellationToken, Task<IReadOnlyList<OctokitActivityEventData>>>? MyActivityFactory { get; init; }

        public Func<RepoKey, PageRequest, CancellationToken, Task<IReadOnlyList<OctokitActivityEventData>>>? RepoActivityFactory { get; init; }

        public Func<RepoKey, IssueQuery, PageRequest, CancellationToken, Task<IReadOnlyList<OctokitIssueData>>>? IssuesFactory { get; init; }

        public Func<RepoKey, int, CancellationToken, Task<OctokitIssueDetailData?>>? IssueFactory { get; init; }

        public Func<RepoKey, int, PageRequest, CancellationToken, Task<IReadOnlyList<OctokitIssueCommentData>>>? IssueCommentsFactory { get; init; }

        public Func<IssueSearchQuery, PageRequest, CancellationToken, Task<IReadOnlyList<OctokitWorkItemData>>>? SearchIssuesFactory { get; init; }

        public Func<RepoSearchQuery, PageRequest, CancellationToken, Task<IReadOnlyList<OctokitRepositoryData>>>? SearchReposFactory { get; init; }

        public Func<UserSearchQuery, PageRequest, CancellationToken, Task<IReadOnlyList<OctokitUserData>>>? SearchUsersFactory { get; init; }

        public Func<CodeSearchQuery, PageRequest, CancellationToken, Task<IReadOnlyList<OctokitCodeSearchItemData>>>? SearchCodeFactory { get; init; }

        public Func<bool, PageRequest, CancellationToken, Task<IReadOnlyList<OctokitNotificationData>>>? NotificationsFactory { get; init; }

        public Func<RepoKey, CancellationToken, Task<OctokitRepositoryDetailData?>>? RepoDetailsFactory { get; init; }

        public int GetMyRepositoriesCallCount { get; private set; }
        public int GetMyActivityCallCount { get; private set; }
        public int GetRepoActivityCallCount { get; private set; }
        public int GetIssuesCallCount { get; private set; }
        public int GetRepoDetailsCallCount { get; private set; }

        public int SearchReposCallCount { get; private set; }
        public int SearchUsersCallCount { get; private set; }
        public int SearchCodeCallCount { get; private set; }

        public Task<IReadOnlyList<OctokitRepositoryData>> GetMyRepositoriesAsync(CancellationToken ct)
        {
            GetMyRepositoriesCallCount++;
            return Task.FromResult(Repositories);
        }

        public Task<IReadOnlyList<OctokitActivityEventData>> GetMyActivityAsync(PageRequest page, CancellationToken ct)
        {
            GetMyActivityCallCount++;

            if (MyActivityFactory is not null)
            {
                return MyActivityFactory(page, ct);
            }

            return Task.FromResult<IReadOnlyList<OctokitActivityEventData>>(Array.Empty<OctokitActivityEventData>());
        }

        public Task<IReadOnlyList<OctokitActivityEventData>> GetRepoActivityAsync(RepoKey repo, PageRequest page, CancellationToken ct)
        {
            GetRepoActivityCallCount++;

            if (RepoActivityFactory is not null)
            {
                return RepoActivityFactory(repo, page, ct);
            }

            return Task.FromResult<IReadOnlyList<OctokitActivityEventData>>(Array.Empty<OctokitActivityEventData>());
        }

        public Task<IReadOnlyList<OctokitIssueData>> GetIssuesAsync(RepoKey repo, IssueQuery query, PageRequest page, CancellationToken ct)
        {
            GetIssuesCallCount++;

            if (IssuesFactory is not null)
            {
                return IssuesFactory(repo, query, page, ct);
            }

            return Task.FromResult<IReadOnlyList<OctokitIssueData>>(Array.Empty<OctokitIssueData>());
        }

        public Task<IReadOnlyList<OctokitWorkItemData>> SearchIssuesAsync(IssueSearchQuery query, PageRequest page, CancellationToken ct)
        {
            if (SearchIssuesFactory is not null)
            {
                return SearchIssuesFactory(query, page, ct);
            }

            return Task.FromResult<IReadOnlyList<OctokitWorkItemData>>(Array.Empty<OctokitWorkItemData>());
        }

        public Task<IReadOnlyList<OctokitRepositoryData>> SearchRepositoriesAsync(RepoSearchQuery query, PageRequest page, CancellationToken ct)
        {
            SearchReposCallCount++;

            if (SearchReposFactory is not null)
            {
                return SearchReposFactory(query, page, ct);
            }

            return Task.FromResult<IReadOnlyList<OctokitRepositoryData>>(Array.Empty<OctokitRepositoryData>());
        }

        public Task<IReadOnlyList<OctokitUserData>> SearchUsersAsync(UserSearchQuery query, PageRequest page, CancellationToken ct)
        {
            SearchUsersCallCount++;

            if (SearchUsersFactory is not null)
            {
                return SearchUsersFactory(query, page, ct);
            }

            return Task.FromResult<IReadOnlyList<OctokitUserData>>(Array.Empty<OctokitUserData>());
        }

        public Task<IReadOnlyList<OctokitCodeSearchItemData>> SearchCodeAsync(CodeSearchQuery query, PageRequest page, CancellationToken ct)
        {
            SearchCodeCallCount++;

            if (SearchCodeFactory is not null)
            {
                return SearchCodeFactory(query, page, ct);
            }

            return Task.FromResult<IReadOnlyList<OctokitCodeSearchItemData>>(Array.Empty<OctokitCodeSearchItemData>());
        }

        public Task<IReadOnlyList<OctokitNotificationData>> GetMyNotificationsAsync(bool unreadOnly, PageRequest page, CancellationToken ct)
        {
            if (NotificationsFactory is not null)
            {
                return NotificationsFactory(unreadOnly, page, ct);
            }

            return Task.FromResult<IReadOnlyList<OctokitNotificationData>>(Array.Empty<OctokitNotificationData>());
        }

        public Task<OctokitRepositoryDetailData?> GetRepositoryAsync(RepoKey repo, CancellationToken ct)
        {
            GetRepoDetailsCallCount++;

            if (RepoDetailsFactory is not null)
            {
                return RepoDetailsFactory(repo, ct);
            }

            return Task.FromResult<OctokitRepositoryDetailData?>(null);
        }

        public Task<OctokitIssueDetailData?> GetIssueAsync(RepoKey repo, int issueNumber, CancellationToken ct)
        {
            if (IssueFactory is not null)
            {
                return IssueFactory(repo, issueNumber, ct);
            }

            return Task.FromResult<OctokitIssueDetailData?>(null);
        }

        public Task<IReadOnlyList<OctokitIssueCommentData>> GetIssueCommentsAsync(RepoKey repo, int issueNumber, PageRequest page, CancellationToken ct)
        {
            if (IssueCommentsFactory is not null)
            {
                return IssueCommentsFactory(repo, issueNumber, page, ct);
            }

            return Task.FromResult<IReadOnlyList<OctokitIssueCommentData>>(Array.Empty<OctokitIssueCommentData>());
        }
    }
}
