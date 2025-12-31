namespace JitHub.GitHub.Octokit.Mapping;

internal sealed record OctokitUserData(
    long Id,
    string Login,
    string? Name,
    string? Bio,
    string? Url);
