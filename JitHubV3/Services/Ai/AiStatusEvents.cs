namespace JitHubV3.Services.Ai;

public abstract record AiStatusEvent;

public sealed record AiEnablementChanged(bool IsEnabled) : AiStatusEvent;

public sealed record AiSelectionChanged(AiModelSelection? Selection) : AiStatusEvent;

public sealed record AiDownloadProgressChanged(
    Guid DownloadId,
    AiModelDownloadRequest Request,
    AiModelDownloadProgress Progress) : AiStatusEvent;
