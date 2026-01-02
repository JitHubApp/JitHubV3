namespace JitHubV3.Presentation;

public interface IStatusBarExtension
{
    event EventHandler? Changed;

    IReadOnlyList<StatusBarSegment> Segments { get; }
}
