namespace JitHub.GitHub.Octokit;

public sealed record OctokitClientOptions(
    string ProductName,
    string? ProductVersion = null,
    Uri? ApiBaseAddress = null,
    Action<OctokitClientCreatedEvent>? OnClientCreated = null);

public sealed record OctokitClientCreatedEvent(
    Uri ApiBaseAddress,
    string ProductName,
    string? ProductVersion);
