namespace JitHubV3.Presentation;

public sealed class HardwareStatusBarExtension : IStatusBarExtension
{
    public event EventHandler? Changed;

    public IReadOnlyList<StatusBarSegment> Segments
        => new[]
        {
            // Conservative: do not claim GPU unless we can prove it (Phase 11 capability registry).
            new StatusBarSegment(
                Id: "hardware",
                Text: "HW: CPU",
                IsVisible: true,
                Priority: 10),
        };
}
