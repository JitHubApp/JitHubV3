#if WINDOWS
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace JitHubV3.Services.Ai;

public sealed class WindowsAiModelDownloadNotificationService : IAiModelDownloadNotificationService
{
    public void NotifyDownloadCompleted(AiModelDownloadProgress completedProgress)
    {
        TryShow(
            title: "Model download completed",
            body: string.IsNullOrWhiteSpace(completedProgress.ModelId)
                ? "A model finished downloading."
                : $"{completedProgress.ModelId} finished downloading.");
    }

    public void NotifyDownloadFailed(AiModelDownloadProgress failedProgress)
    {
        var reason = string.IsNullOrWhiteSpace(failedProgress.ErrorMessage)
            ? "The download failed."
            : failedProgress.ErrorMessage;

        TryShow(
            title: "Model download failed",
            body: string.IsNullOrWhiteSpace(failedProgress.ModelId)
                ? reason
                : $"{failedProgress.ModelId}: {reason}");
    }

    private static void TryShow(string title, string body)
    {
        try
        {
            // Best-effort. If AppNotificationManager isn't configured, just skip.
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(body)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // ignore
        }
    }
}
#endif
