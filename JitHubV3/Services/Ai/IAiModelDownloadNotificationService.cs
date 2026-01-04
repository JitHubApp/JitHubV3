namespace JitHubV3.Services.Ai;

public interface IAiModelDownloadNotificationService
{
    void NotifyDownloadCompleted(AiModelDownloadProgress completedProgress);

    void NotifyDownloadFailed(AiModelDownloadProgress failedProgress);
}

public sealed class NullAiModelDownloadNotificationService : IAiModelDownloadNotificationService
{
    public void NotifyDownloadCompleted(AiModelDownloadProgress completedProgress)
    {
        // no-op
    }

    public void NotifyDownloadFailed(AiModelDownloadProgress failedProgress)
    {
        // no-op
    }
}
