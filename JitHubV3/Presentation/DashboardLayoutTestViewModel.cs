using CommunityToolkit.Mvvm.ComponentModel;
using JitHub.Dashboard.Layouts;

namespace JitHubV3.Presentation;

public sealed partial class DashboardLayoutTestViewModel : ObservableObject
{
    private readonly INavigator _navigator;

    public DashboardLayoutTestViewModel(INavigator navigator)
    {
        _navigator = navigator;
        GoBack = new AsyncRelayCommand(DoGoBack);

        var noop = new AsyncRelayCommand(Noop);

        Cards =
        [
            new DashboardCardModel(
                1,
                DashboardCardKind.SelectedRepo,
                "Selected Repository",
                "octocat/Hello-World",
                "A compact summary to exercise wrapping.",
                Importance: 100,
                Actions:
                [
                    new DashboardCardActionModel("Open", noop),
                    new DashboardCardActionModel("Refresh", noop),
                ],
                TintVariant: 1 % 5),
            new DashboardCardModel(2, DashboardCardKind.Unknown, "Longer card title that should wrap across multiple lines", "Subtitle that also wraps when the width is constrained", "This is a longer summary to validate multi-line text layout inside a fixed-height card surface.", TintVariant: 2 % 5),
            new DashboardCardModel(3, DashboardCardKind.Unknown, "No subtitle", null, "Summary only.", TintVariant: 3 % 5),
            new DashboardCardModel(4, DashboardCardKind.Unknown, "No summary", "Only subtitle", null, TintVariant: 4 % 5),
            new DashboardCardModel(5, DashboardCardKind.Unknown, "Empty body", null, null, TintVariant: 0),
            new DashboardCardModel(6, DashboardCardKind.Unknown, "Metrics", "Stars: 1234 · Forks: 56", "This line includes separators and bullets to test trimming/wrapping.", TintVariant: 1),
            new DashboardCardModel(7, DashboardCardKind.Unknown, "Edge cases", "Multiple\nlines", "Newlines in text are not expected but should remain stable.", TintVariant: 2),
            new DashboardCardModel(8, DashboardCardKind.Unknown, "Accessibility", "Focus order + hit testing", "Use Tab to move focus between controls and ensure the card layout doesn't trap focus.", TintVariant: 3),
            new DashboardCardModel(9, DashboardCardKind.Unknown, "Deck depth", "Card 9", "In Deck mode, cards should stack with rotation and Y offset.", TintVariant: 4),
            new DashboardCardModel(10, DashboardCardKind.Unknown, "Deck depth", "Card 10", "In Deck mode, only a few should be visible (clamped).", TintVariant: 0),
            new DashboardCardModel(11, DashboardCardKind.Unknown, "Grid fill", "Card 11", "In Grid mode, columns should center with consistent gutters.", TintVariant: 1),
            new DashboardCardModel(12, DashboardCardKind.Unknown, "Grid fill", "Card 12", "Resize the window to watch Auto mode switch.", TintVariant: 2),
        ];
    }

    private string _title = "Dashboard Layout Test";
    private CardDeckLayoutMode _selectedMode = CardDeckLayoutMode.Auto;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public CardDeckLayoutMode[] Modes { get; } =
    [
        CardDeckLayoutMode.Auto,
        CardDeckLayoutMode.Grid,
        CardDeckLayoutMode.Deck,
    ];

    public CardDeckLayoutMode SelectedMode
    {
        get => _selectedMode;
        set => SetProperty(ref _selectedMode, value);
    }

    public IReadOnlyList<DashboardCardModel> Cards { get; }

    public ICommand GoBack { get; }

    private static Task Noop(CancellationToken ct) => Task.CompletedTask;

    private Task DoGoBack(CancellationToken ct)
        => _navigator.NavigateBackAsync(this, cancellation: ct);
}
