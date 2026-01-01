namespace JitHubV3.Services.Ai;

public sealed record AiLocalModelDefinition(
    string ModelId,
    string? DisplayName,
    string RuntimeId,
    string? DefaultInstallFolderName = null,
    string? DownloadUri = null,
    string? ArtifactFileName = null,
    long? ExpectedBytes = null,
    string? ExpectedSha256 = null);

public sealed record AiLocalModelInventoryEntry(
    string ModelId,
    string RuntimeId,
    string InstallPath);

public sealed record AiLocalModelCatalogItem(
    string ModelId,
    string? DisplayName,
    string RuntimeId,
    bool IsDownloaded,
    string InstallPath);
