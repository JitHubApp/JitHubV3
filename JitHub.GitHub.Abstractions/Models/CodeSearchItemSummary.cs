namespace JitHub.GitHub.Abstractions.Models;

public sealed record CodeSearchItemSummary(
    string Path,
    RepoKey Repo,
    string? Sha,
    string? Url);
