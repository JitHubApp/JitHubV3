using JitHub.GitHub.Abstractions.Models;
using JitHub.GitHub.Abstractions.Refresh;

namespace JitHub.GitHub.Abstractions.Services;

public interface IGitHubIssueConversationService
{
    Task<IssueDetail?> GetIssueAsync(
        RepoKey repo,
        int issueNumber,
        RefreshMode refresh,
        CancellationToken ct);

    Task<IReadOnlyList<IssueComment>> GetCommentsAsync(
        RepoKey repo,
        int issueNumber,
        RefreshMode refresh,
        CancellationToken ct);
}
