using JitHubV3.Services.Ai;

namespace JitHubV3.Presentation;

public sealed class AiDownloadStatusBarExtension : IStatusBarExtension, IDisposable
{
    private readonly IDisposable _subscription;

    private AiModelDownloadProgress? _progress;
    private AiModelDownloadRequest? _request;

    public AiDownloadStatusBarExtension(IAiStatusEventBus events)
    {
        if (events is null)
        {
            throw new ArgumentNullException(nameof(events));
        }

        _subscription = events.Subscribe(OnEvent);
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<StatusBarSegment> Segments
    {
        get
        {
            var p = _progress;
            var r = _request;

            if (p is null || r is null)
            {
                return Array.Empty<StatusBarSegment>();
            }

            if (p.Status is not (AiModelDownloadStatus.Queued or AiModelDownloadStatus.Downloading))
            {
                return Array.Empty<StatusBarSegment>();
            }

            var model = string.IsNullOrWhiteSpace(r.ModelId) ? "model" : r.ModelId;
            var text = p.Progress is null
                ? $"Downloading {model}…"
                : $"Downloading {model} ({Math.Round(p.Progress.Value * 100, 1):0.#}%)";

            return new[]
            {
                new StatusBarSegment(
                    Id: "ai-download",
                    Text: text,
                    IsVisible: true,
                    Priority: 300),
            };
        }
    }

    private void OnEvent(AiStatusEvent evt)
    {
        if (evt is not AiDownloadProgressChanged download)
        {
            return;
        }

        _request = download.Request;
        _progress = download.Progress;

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
