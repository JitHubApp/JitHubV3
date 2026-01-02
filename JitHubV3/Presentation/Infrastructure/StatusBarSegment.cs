namespace JitHubV3.Presentation;

public sealed partial record StatusBarSegment(
    string Id,
    string Text,
    bool IsVisible = true,
    int Priority = 0);
