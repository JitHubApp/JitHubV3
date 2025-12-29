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

    internal sealed class FakeGitHubDataSource : IGitHubDataSource
    {
        public IReadOnlyList<OctokitRepositoryData> Repositories { get; init; } = Array.Empty<OctokitRepositoryData>();

        public Func<RepoKey, IssueQuery, PageRequest, CancellationToken, Task<IReadOnlyList<OctokitIssueData>>>? IssuesFactory { get; init; }

        public Func<RepoKey, int, CancellationToken, Task<OctokitIssueDetailData?>>? IssueFactory { get; init; }

        public Func<RepoKey, int, PageRequest, CancellationToken, Task<IReadOnlyList<OctokitIssueCommentData>>>? IssueCommentsFactory { get; init; }

        public int GetMyRepositoriesCallCount { get; private set; }
        public int GetIssuesCallCount { get; private set; }

        public Task<IReadOnlyList<OctokitRepositoryData>> GetMyRepositoriesAsync(CancellationToken ct)
        {
            GetMyRepositoriesCallCount++;
            return Task.FromResult(Repositories);
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
