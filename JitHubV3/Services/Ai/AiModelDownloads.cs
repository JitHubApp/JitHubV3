namespace JitHubV3.Services.Ai;

public enum AiModelDownloadStatus
{
    Queued = 0,
    Downloading = 1,
    Completed = 2,
    Failed = 3,
    Canceled = 4,
    Verifying = 5,
    VerificationFailed = 6,
}

public sealed record AiModelDownloadRequest(
    string ModelId,
    string RuntimeId,
    Uri SourceUri,
    string InstallPath,
    string? ArtifactFileName = null,
    long? ExpectedBytes = null,
    string? ExpectedSha256 = null);

public sealed record AiModelDownloadProgress(
    Guid DownloadId,
    string ModelId,
    string RuntimeId,
    AiModelDownloadStatus Status,
    long BytesReceived,
    long? TotalBytes,
    double? Progress,
    string? InstallPath,
    string? ArtifactPath,
    string? ErrorMessage);
