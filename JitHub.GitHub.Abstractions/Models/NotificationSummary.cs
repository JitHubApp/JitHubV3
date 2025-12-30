namespace JitHub.GitHub.Abstractions.Models;

public sealed record NotificationSummary(
    string Id,
    RepoKey Repo,
    string Title,
    string? Type,
    DateTimeOffset? UpdatedAt,
    bool Unread);
