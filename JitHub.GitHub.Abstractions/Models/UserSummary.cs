namespace JitHub.GitHub.Abstractions.Models;

public sealed record UserSummary(
    long Id,
    string Login,
    string? Name,
    string? Bio,
    string? Url);
