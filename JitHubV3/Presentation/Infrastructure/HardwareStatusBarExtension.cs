using JitHubV3.Services.Platform;

namespace JitHubV3.Presentation;

public sealed class HardwareStatusBarExtension : IStatusBarExtension
{
    private readonly IPlatformCapabilities _capabilities;

    public HardwareStatusBarExtension(IPlatformCapabilities capabilities)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

#pragma warning disable CS0067
    public event EventHandler? Changed;
#pragma warning restore CS0067

    public IReadOnlyList<StatusBarSegment> Segments
        => new[]
        {
            // Conservative: do not claim GPU unless we can prove it (Phase 11 capability registry).
            // Even if we *can* introspect, we still default to CPU until Phase 11+ adds a real detector.
            new StatusBarSegment(
                Id: "hardware",
                Text: "HW: CPU",
                IsVisible: true,
                Priority: 10),
        };
}
