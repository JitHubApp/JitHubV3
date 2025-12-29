namespace JitHub.Dashboard.Layouts;

public static class CardDeckLayoutMath
{
    public static IReadOnlyList<CardDeckLayoutItem> Compute(LayoutSize available, int itemCount, CardDeckLayoutSettings settings)
    {
        if (itemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemCount));
        }

        if (itemCount == 0)
        {
            return Array.Empty<CardDeckLayoutItem>();
        }

        var safeWidth = SanitizeSize(available.Width);
        var safeHeight = SanitizeSize(available.Height);

        var cardMinWidth = Math.Max(1, SanitizeSize(settings.CardMinWidth));
        var cardHeight = Math.Max(1, SanitizeSize(settings.CardHeight));
        var spacing = Math.Max(0, SanitizeSize(settings.Spacing));

        var mode = settings.Mode;
        if (mode == CardDeckLayoutMode.Auto)
        {
            var threshold = Math.Max(0, SanitizeSize(settings.ModeWidthThreshold));
            mode = safeWidth >= threshold ? CardDeckLayoutMode.Grid : CardDeckLayoutMode.Deck;
        }

        return mode switch
        {
            CardDeckLayoutMode.Grid => ComputeGrid(
                width: safeWidth,
                height: safeHeight,
                itemCount: itemCount,
                cardWidth: cardMinWidth,
                cardHeight: cardHeight,
                spacing: spacing,
                maxColumns: Math.Max(1, settings.MaxColumns)),

            CardDeckLayoutMode.Deck => ComputeDeck(
                width: safeWidth,
                height: safeHeight,
                itemCount: itemCount,
                cardWidth: Math.Min(cardMinWidth, safeWidth <= 0 ? cardMinWidth : safeWidth),
                cardHeight: cardHeight,
                deckMaxVisible: Math.Max(1, settings.DeckMaxVisibleCount),
                deckOffsetY: SanitizeSize(settings.DeckOffsetY),
                deckAngleStepDegrees: SanitizeSize(settings.DeckAngleStepDegrees),
                deckScaleStep: SanitizeSize(settings.DeckScaleStep)),

            _ => ComputeDeck(
                width: safeWidth,
                height: safeHeight,
                itemCount: itemCount,
                cardWidth: Math.Min(cardMinWidth, safeWidth <= 0 ? cardMinWidth : safeWidth),
                cardHeight: cardHeight,
                deckMaxVisible: Math.Max(1, settings.DeckMaxVisibleCount),
                deckOffsetY: SanitizeSize(settings.DeckOffsetY),
                deckAngleStepDegrees: SanitizeSize(settings.DeckAngleStepDegrees),
                deckScaleStep: SanitizeSize(settings.DeckScaleStep)),
        };
    }

    private static IReadOnlyList<CardDeckLayoutItem> ComputeGrid(
        double width,
        double height,
        int itemCount,
        double cardWidth,
        double cardHeight,
        double spacing,
        int maxColumns)
    {
        var columns = ComputeColumnCount(width, cardWidth, spacing, maxColumns);
        var actualRowWidth = columns * cardWidth + (columns - 1) * spacing;
        var gutterX = Math.Max(0, (width - actualRowWidth) / 2.0);

        var list = new CardDeckLayoutItem[itemCount];
        for (var i = 0; i < itemCount; i++)
        {
            var col = i % columns;
            var row = i / columns;

            var x = gutterX + col * (cardWidth + spacing);
            var y = row * (cardHeight + spacing);

            list[i] = new CardDeckLayoutItem(
                Rect: new LayoutRect(x, y, cardWidth, cardHeight),
                Transform: new LayoutTransform(0, 0, 0, 1));
        }

        return list;
    }

    private static IReadOnlyList<CardDeckLayoutItem> ComputeDeck(
        double width,
        double height,
        int itemCount,
        double cardWidth,
        double cardHeight,
        int deckMaxVisible,
        double deckOffsetY,
        double deckAngleStepDegrees,
        double deckScaleStep)
    {
        var x = Math.Max(0, (width - cardWidth) / 2.0);
        var baseRect = new LayoutRect(x, 0, cardWidth, cardHeight);

        var list = new CardDeckLayoutItem[itemCount];
        for (var i = 0; i < itemCount; i++)
        {
            var depth = Math.Min(i, deckMaxVisible - 1);

            var sign = (depth % 2 == 0) ? 1 : -1;
            var rotation = sign * depth * deckAngleStepDegrees;
            var translateY = depth * deckOffsetY;

            var scale = 1.0 - depth * deckScaleStep;
            if (scale <= 0)
            {
                scale = 0.01;
            }

            list[i] = new CardDeckLayoutItem(
                Rect: baseRect,
                Transform: new LayoutTransform(0, translateY, rotation, scale));
        }

        return list;
    }

    private static int ComputeColumnCount(double width, double cardWidth, double spacing, int maxColumns)
    {
        if (width <= 0)
        {
            return 1;
        }

        // How many fixed-width cards fit with spacing between them.
        // columns*cardWidth + (columns-1)*spacing <= width
        var denom = cardWidth + spacing;
        if (denom <= 0)
        {
            return 1;
        }

        var columns = (int)Math.Floor((width + spacing) / denom);
        if (columns < 1)
        {
            columns = 1;
        }

        if (columns > maxColumns)
        {
            columns = maxColumns;
        }

        return columns;
    }

    private static double SanitizeSize(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        if (value < 0)
        {
            return 0;
        }

        return value;
    }
}
