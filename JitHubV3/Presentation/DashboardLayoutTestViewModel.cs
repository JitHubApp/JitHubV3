using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using JitHub.Dashboard.Layouts;

namespace JitHubV3.Presentation;

public sealed partial class DashboardLayoutTestViewModel : ObservableObject
{
    private readonly INavigator _navigator;

    public Func<long, CardSwipeDirection, CancellationToken, Task<bool>>? SwipeRequestAsync { get; set; }

    public DashboardLayoutTestViewModel(INavigator navigator)
    {
        _navigator = navigator;
        GoBack = new AsyncRelayCommand(DoGoBack);

        DashboardCardActionModel DismissAction(long cardId)
            => new("Dismiss", new AsyncRelayCommand(() => DismissCard(cardId, CancellationToken.None)));

        Cards =
        new ObservableCollection<DashboardCardModel>
        {
            new DashboardCardModel(
                1,
                DashboardCardKind.SelectedRepo,
                "Selected Repository",
                "octocat/Hello-World",
                "Tap Dismiss to exercise SwipeAsync + removal.",
                Importance: 100,
                Actions: [ DismissAction(1) ],
                TintVariant: 1 % 5),
            new DashboardCardModel(2, DashboardCardKind.Unknown, "Longer card title that should wrap across multiple lines", "Subtitle that also wraps when the width is constrained", "This is a longer summary to validate multi-line text layout inside a fixed-height card surface.", Actions: [ DismissAction(2) ], TintVariant: 2 % 5),
            new DashboardCardModel(3, DashboardCardKind.Unknown, "No subtitle", null, "Summary only.", Actions: [ DismissAction(3) ], TintVariant: 3 % 5),
            new DashboardCardModel(4, DashboardCardKind.Unknown, "No summary", "Only subtitle", null, Actions: [ DismissAction(4) ], TintVariant: 4 % 5),
            new DashboardCardModel(5, DashboardCardKind.Unknown, "Empty body", null, null, Actions: [ DismissAction(5) ], TintVariant: 0),
            new DashboardCardModel(6, DashboardCardKind.Unknown, "Metrics", "Stars: 1234 · Forks: 56", "This line includes separators and bullets to test trimming/wrapping.", Actions: [ DismissAction(6) ], TintVariant: 1),
            new DashboardCardModel(7, DashboardCardKind.Unknown, "Edge cases", "Multiple\nlines", "Newlines in text are not expected but should remain stable.", Actions: [ DismissAction(7) ], TintVariant: 2),
            new DashboardCardModel(8, DashboardCardKind.Unknown, "Accessibility", "Focus order + hit testing", "Use Tab to move focus between controls and ensure the card layout doesn't trap focus.", Actions: [ DismissAction(8) ], TintVariant: 3),
            new DashboardCardModel(9, DashboardCardKind.Unknown, "Deck depth", "Card 9", "In Deck mode, cards should stack with rotation and Y offset.", Actions: [ DismissAction(9) ], TintVariant: 4),
            new DashboardCardModel(10, DashboardCardKind.Unknown, "Deck depth", "Card 10", "In Deck mode, only a few should be visible (clamped).", Actions: [ DismissAction(10) ], TintVariant: 0),
            new DashboardCardModel(11, DashboardCardKind.Unknown, "Grid fill", "Card 11", "In Grid mode, columns should center with consistent gutters.", Actions: [ DismissAction(11) ], TintVariant: 1),
            new DashboardCardModel(12, DashboardCardKind.Unknown, "Grid fill", "Card 12", "Resize the window to watch Auto mode switch.", Actions: [ DismissAction(12) ], TintVariant: 2),
        };
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

    public ObservableCollection<DashboardCardModel> Cards { get; }

    public ICommand GoBack { get; }

    private async Task DismissCard(long cardId, CancellationToken ct)
    {
        var didAnimate = false;

        if (SwipeRequestAsync is not null)
        {
            didAnimate = await SwipeRequestAsync(cardId, CardSwipeDirection.Right, ct);
        }

        // Remove regardless; if animation couldn't run (virtualized), treat as an instant dismissal.
        var item = Cards.FirstOrDefault(c => c.CardId == cardId);
        if (item is not null)
        {
            Cards.Remove(item);
        }
    }

    private Task DoGoBack(CancellationToken ct)
        => _navigator.NavigateBackAsync(this, cancellation: ct);
}
