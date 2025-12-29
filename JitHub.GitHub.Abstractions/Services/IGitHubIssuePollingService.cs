using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Paging;
using JitHub.GitHub.Abstractions.Polling;
using JitHub.GitHub.Abstractions.Queries;

namespace JitHub.GitHub.Abstractions.Services;

public interface IGitHubIssuePollingService
{
    Task StartIssuesPollingAsync(
        RepoKey repo,
        IssueQuery query,
        PageRequest page,
        PollingRequest polling,
        CancellationToken ct);
}
