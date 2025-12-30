using JitHub.Data.Caching;
using JitHub.GitHub.Abstractions.Services;
using JitHub.GitHub.Octokit.Services;
using Microsoft.Extensions.DependencyInjection;

namespace JitHub.GitHub.Octokit;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJitHubGitHubServices(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddSingleton<ICacheStore>(_ => new InMemoryCacheStore());
        services.AddSingleton<ICacheEventBus, CacheEventBus>();
        services.AddSingleton<CacheRuntime>();

        services.AddSingleton<IGitHubDataSource, OctokitGitHubDataSource>();

        services.AddSingleton<IGitHubRepositoryService, CachedGitHubRepositoryService>();
        services.AddSingleton<IGitHubIssueService, CachedGitHubIssueService>();
        services.AddSingleton<IGitHubIssueSearchService, CachedGitHubIssueSearchService>();
        services.AddSingleton<IGitHubNotificationService, CachedGitHubNotificationService>();
        services.AddSingleton<IGitHubNotificationPollingService, CachedGitHubNotificationPollingService>();
        services.AddSingleton<IGitHubIssueConversationService, CachedGitHubIssueConversationService>();
        services.AddSingleton<IGitHubIssuePollingService, CachedGitHubIssuePollingService>();

        return services;
    }
}
