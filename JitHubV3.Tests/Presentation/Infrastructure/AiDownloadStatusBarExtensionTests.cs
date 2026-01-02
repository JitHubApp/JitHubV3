using FluentAssertions;
using JitHubV3.Presentation;
using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Presentation.Infrastructure;

public sealed class AiDownloadStatusBarExtensionTests
{
    [Test]
    public void Segment_is_visible_only_while_download_is_active()
    {
        var bus = new AiStatusEventBus();
        var ext = new AiDownloadStatusBarExtension(bus);

        ext.Segments.Should().BeEmpty();

        var request = new AiModelDownloadRequest(
            ModelId: "m1",
            RuntimeId: "local-foundry",
            SourceUri: new Uri("https://example.invalid/m1.bin"),
            InstallPath: "C:\\models\\m1",
            ArtifactFileName: "m1.bin");

        var downloading = new AiModelDownloadProgress(
            DownloadId: Guid.NewGuid(),
            ModelId: request.ModelId,
            RuntimeId: request.RuntimeId,
            Status: AiModelDownloadStatus.Downloading,
            BytesReceived: 50,
            TotalBytes: 100,
            Progress: 0.5,
            InstallPath: request.InstallPath,
            ArtifactPath: null,
            ErrorMessage: null);

        bus.Publish(new AiDownloadProgressChanged(downloading.DownloadId, request, downloading));

        ext.Segments.Should().ContainSingle();
        ext.Segments[0].Text.Should().Contain("Downloading m1");
        ext.Segments[0].Text.Should().Contain("50%");

        var completed = downloading with { Status = AiModelDownloadStatus.Completed, Progress = 1.0 };
        bus.Publish(new AiDownloadProgressChanged(completed.DownloadId, request, completed));

        ext.Segments.Should().BeEmpty();
    }
}
