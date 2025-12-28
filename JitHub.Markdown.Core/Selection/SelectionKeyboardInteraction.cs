using System.Collections.Immutable;

namespace JitHub.Markdown;

internal enum MarkdownKeyCommand
{
    Left,
    Right,
    Up,
    Down,
    Tab,
    Enter,
}

internal readonly record struct FocusedLink(NodeId Id, string Url, RectF Bounds);

internal readonly record struct KeyboardInteractionResult(
    bool SelectionChanged,
    SelectionRange? Selection,
    bool FocusChanged,
    FocusedLink? FocusedLink,
    string? ActivateLinkUrl,
    bool Handled);

/// <summary>
/// Core (platform-agnostic) keyboard interaction for a markdown layout.
/// </summary>
internal sealed class SelectionKeyboardInteraction
{
    public SelectionRange? Selection { get; set; }

    public FocusedLink? FocusedLink { get; private set; }

    public void ClearLinkFocus()
    {
        FocusedLink = null;
    }

    internal bool TryFocusLink(MarkdownLayout layout, NodeId id, string url)
    {
        _ = layout ?? throw new ArgumentNullException(nameof(layout));

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var links = BuildLinkList(layout);
        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            if (link.Id != id)
            {
                continue;
            }

            if (!string.Equals(link.Url, url, StringComparison.Ordinal))
            {
                continue;
            }

            var nextFocused = new FocusedLink(link.Id, link.Url, link.Bounds);
            var nextSelection = new SelectionRange(link.Caret, link.Caret);

            var focusChanged = FocusedLink is null || FocusedLink.Value.Id != nextFocused.Id || !string.Equals(FocusedLink.Value.Url, nextFocused.Url, StringComparison.Ordinal);
            var selectionChanged = !Selection.Equals(nextSelection);

            FocusedLink = nextFocused;
            Selection = nextSelection;

            return focusChanged || selectionChanged;
        }

        return false;
    }

    /// <summary>
    /// Handles a key command against the current layout.
    /// </summary>
    public KeyboardInteractionResult OnKeyCommand(MarkdownLayout layout, MarkdownKeyCommand command, bool selectionEnabled, bool shift)
    {
        _ = layout ?? throw new ArgumentNullException(nameof(layout));

        switch (command)
        {
            case MarkdownKeyCommand.Tab:
                return MoveLinkFocus(layout, forward: !shift);

            case MarkdownKeyCommand.Enter:
                return ActivateFocusedOrCaretLink(selectionEnabled);

            case MarkdownKeyCommand.Left:
            case MarkdownKeyCommand.Right:
            case MarkdownKeyCommand.Up:
            case MarkdownKeyCommand.Down:
                if (!selectionEnabled)
                {
                    return new KeyboardInteractionResult(
                        SelectionChanged: false,
                        Selection: Selection,
                        FocusChanged: false,
                        FocusedLink: FocusedLink,
                        ActivateLinkUrl: null,
                        Handled: false);
                }

                return MoveCaret(layout, command);

            default:
                return new KeyboardInteractionResult(
                    SelectionChanged: false,
                    Selection: Selection,
                    FocusChanged: false,
                    FocusedLink: FocusedLink,
                    ActivateLinkUrl: null,
                    Handled: false);
        }
    }

    private KeyboardInteractionResult MoveCaret(MarkdownLayout layout, MarkdownKeyCommand command)
    {
        // Moving the caret breaks link focus.
        var oldFocus = FocusedLink;
        FocusedLink = null;

        if (!TryGetCaret(layout, Selection, out var caret))
        {
            return new KeyboardInteractionResult(
                SelectionChanged: false,
                Selection: Selection,
                FocusChanged: oldFocus is not null,
                FocusedLink: FocusedLink,
                ActivateLinkUrl: null,
                Handled: false);
        }

        if (!TryMoveCaret(layout, caret, command, out var nextCaret))
        {
            return new KeyboardInteractionResult(
                SelectionChanged: false,
                Selection: Selection,
                FocusChanged: oldFocus is not null,
                FocusedLink: FocusedLink,
                ActivateLinkUrl: null,
                Handled: oldFocus is not null);
        }

        var nextSelection = new SelectionRange(nextCaret, nextCaret);
        var changed = !Selection.Equals(nextSelection);
        Selection = nextSelection;

        return new KeyboardInteractionResult(
            SelectionChanged: changed,
            Selection: Selection,
            FocusChanged: oldFocus is not null,
            FocusedLink: FocusedLink,
            ActivateLinkUrl: null,
            Handled: true);
    }

    private KeyboardInteractionResult ActivateFocusedOrCaretLink(bool selectionEnabled)
    {
        if (FocusedLink is { } fl)
        {
            return new KeyboardInteractionResult(
                SelectionChanged: false,
                Selection: Selection,
                FocusChanged: false,
                FocusedLink: FocusedLink,
                ActivateLinkUrl: fl.Url,
                Handled: true);
        }

        if (selectionEnabled && Selection is { } sel)
        {
            var caret = sel.Active;
            if (caret.Run.Kind == NodeKind.Link && !string.IsNullOrWhiteSpace(caret.Run.Url))
            {
                return new KeyboardInteractionResult(
                    SelectionChanged: false,
                    Selection: Selection,
                    FocusChanged: false,
                    FocusedLink: FocusedLink,
                    ActivateLinkUrl: caret.Run.Url,
                    Handled: true);
            }
        }

        return new KeyboardInteractionResult(
            SelectionChanged: false,
            Selection: Selection,
            FocusChanged: false,
            FocusedLink: FocusedLink,
            ActivateLinkUrl: null,
            Handled: false);
    }

    private KeyboardInteractionResult MoveLinkFocus(MarkdownLayout layout, bool forward)
    {
        var links = BuildLinkList(layout);
        if (links.Count == 0)
        {
            // Let focus move out of the control.
            return new KeyboardInteractionResult(
                SelectionChanged: false,
                Selection: Selection,
                FocusChanged: false,
                FocusedLink: FocusedLink,
                ActivateLinkUrl: null,
                Handled: false);
        }

        var previous = FocusedLink;
        var currentIndex = FindFocusedIndex(links, previous);

        var nextIndex = forward
            ? (currentIndex + 1) % links.Count
            : (currentIndex - 1 + links.Count) % links.Count;

        var next = links[nextIndex];
        FocusedLink = new FocusedLink(next.Id, next.Url, next.Bounds);

        // When focusing a link, set a collapsed selection caret at the start of the link.
        var nextSelection = new SelectionRange(next.Caret, next.Caret);
        var selectionChanged = !Selection.Equals(nextSelection);
        Selection = nextSelection;

        return new KeyboardInteractionResult(
            SelectionChanged: selectionChanged,
            Selection: Selection,
            FocusChanged: previous is null || previous.Value.Id != next.Id,
            FocusedLink: FocusedLink,
            ActivateLinkUrl: null,
            Handled: true);
    }

    private static int FindFocusedIndex(List<LinkEntry> links, FocusedLink? focused)
    {
        if (focused is null)
        {
            return -1;
        }

        for (var i = 0; i < links.Count; i++)
        {
            if (links[i].Id == focused.Value.Id && string.Equals(links[i].Url, focused.Value.Url, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed record LinkEntry(NodeId Id, string Url, RectF Bounds, MarkdownHitTestResult Caret);

    private static List<LinkEntry> BuildLinkList(MarkdownLayout layout)
    {
        // Collect link runs and merge multi-run links (same NodeId) into single focus target.
        var dict = new Dictionary<(NodeId id, string url), (RectF bounds, MarkdownHitTestResult caret)>();

        var lines = MarkdownLineIndexCache.Get(layout).Lines;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (line.Runs.Length == 0)
            {
                continue;
            }

            for (var runIndex = 0; runIndex < line.Runs.Length; runIndex++)
            {
                var run = line.Runs[runIndex];
                if (run.Kind != NodeKind.Link || string.IsNullOrWhiteSpace(run.Url))
                {
                    continue;
                }

                var key = (run.Id, run.Url);
                var caret = new MarkdownHitTestResult(
                    LineIndex: lineIndex,
                    RunIndex: runIndex,
                    Run: run,
                    Line: line,
                    TextOffset: 0,
                    CaretX: MarkdownHitTester.GetCaretX(run, 0));

                if (dict.TryGetValue(key, out var existing))
                {
                    dict[key] = (Union(existing.bounds, run.Bounds), existing.caret);
                }
                else
                {
                    dict[key] = (run.Bounds, caret);
                }
            }
        }

        var list = dict
            .Select(kvp => new LinkEntry(kvp.Key.id, kvp.Key.url, kvp.Value.bounds, kvp.Value.caret))
            .OrderBy(l => l.Bounds.Y)
            .ThenBy(l => l.Bounds.X)
            .ToList();

        return list;
    }

    private static RectF Union(RectF a, RectF b)
    {
        var x1 = Math.Min(a.X, b.X);
        var y1 = Math.Min(a.Y, b.Y);
        var x2 = Math.Max(a.Right, b.Right);
        var y2 = Math.Max(a.Bottom, b.Bottom);
        return new RectF(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }

    private static bool TryGetCaret(MarkdownLayout layout, SelectionRange? selection, out MarkdownHitTestResult caret)
    {
        if (selection is { } sel)
        {
            // Collapse to the active end.
            caret = sel.Active;
            return true;
        }

        return TryGetFirstCaret(layout, out caret);
    }

    private static bool TryGetFirstCaret(MarkdownLayout layout, out MarkdownHitTestResult caret)
    {
        var lines = MarkdownLineIndexCache.Get(layout).Lines;
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (line.Runs.Length == 0)
            {
                continue;
            }

            var run = line.Runs[0];
            caret = new MarkdownHitTestResult(
                LineIndex: lineIndex,
                RunIndex: 0,
                Run: run,
                Line: line,
                TextOffset: 0,
                CaretX: MarkdownHitTester.GetCaretX(run, 0));
            return true;
        }

        caret = default;
        return false;
    }

    private static bool TryGetLastCaret(MarkdownLayout layout, out MarkdownHitTestResult caret)
    {
        MarkdownHitTestResult? last = null;

        var lines = MarkdownLineIndexCache.Get(layout).Lines;
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (line.Runs.Length == 0)
            {
                continue;
            }

            var runIndex = line.Runs.Length - 1;
            var run = line.Runs[runIndex];
            var offset = GetRunLogicalLength(run);

            last = new MarkdownHitTestResult(
                LineIndex: lineIndex,
                RunIndex: runIndex,
                Run: run,
                Line: line,
                TextOffset: offset,
                CaretX: MarkdownHitTester.GetCaretX(run, offset));
        }

        if (last is { } l)
        {
            caret = l;
            return true;
        }

        caret = default;
        return false;
    }

    private static bool TryMoveCaret(MarkdownLayout layout, MarkdownHitTestResult caret, MarkdownKeyCommand move, out MarkdownHitTestResult next)
    {
        var lines = MarkdownLineIndexCache.Get(layout).Lines;
        if (lines.Length == 0)
        {
            next = default;
            return false;
        }

        switch (move)
        {
            case MarkdownKeyCommand.Left:
                return TryMoveLeft(lines, caret, out next);

            case MarkdownKeyCommand.Right:
                return TryMoveRight(lines, caret, out next);

            case MarkdownKeyCommand.Up:
                return TryMoveVertical(lines, caret, delta: -1, out next);

            case MarkdownKeyCommand.Down:
                return TryMoveVertical(lines, caret, delta: +1, out next);

            default:
                next = caret;
                return false;
        }
    }

    private static bool TryMoveLeft(ImmutableArray<LineLayout> lines, MarkdownHitTestResult caret, out MarkdownHitTestResult next)
    {
        var linePos = caret.LineIndex;
        if (linePos < 0 || linePos >= lines.Length)
        {
            next = default;
            return false;
        }

        var line = lines[linePos];
        if (line.Runs.Length == 0)
        {
            next = default;
            return false;
        }

        var runIndex = Math.Clamp(caret.RunIndex, 0, line.Runs.Length - 1);
        var run = line.Runs[runIndex];
        var runLen = GetRunLogicalLength(run);

        // Move within the current run in *visual* order (RTL inverts logical offsets).
        if (run.Kind != NodeKind.Image && runLen > 0)
        {
            var visual = run.IsRightToLeft ? (runLen - caret.TextOffset) : caret.TextOffset;
            if (visual > 0)
            {
                var visualNew = visual - 1;
                var logicalNew = run.IsRightToLeft ? (runLen - visualNew) : visualNew;
                logicalNew = Math.Clamp(logicalNew, 0, runLen);
                next = new MarkdownHitTestResult(caret.LineIndex, runIndex, run, line, logicalNew, MarkdownHitTester.GetCaretX(run, logicalNew));
                return true;
            }
        }

        // Cross-run fallback: probe slightly to the left of the current caret.
        var probeX = caret.CaretX - 0.5f;
        if (MarkdownHitTester.TryHitTestLine(caret.LineIndex, line, probeX, out var hit) && (hit.RunIndex != caret.RunIndex || hit.TextOffset != caret.TextOffset))
        {
            next = hit;
            return true;
        }

        // Move to previous non-empty line.
        for (var i = linePos - 1; i >= 0; i--)
        {
            var pl = lines[i];
            if (pl.Runs.Length == 0)
            {
                continue;
            }

            var lastRunIndex = pl.Runs.Length - 1;
            var lastRun = pl.Runs[lastRunIndex];
            var offset = GetRunLogicalLength(lastRun);
            next = new MarkdownHitTestResult(i, lastRunIndex, lastRun, pl, offset, MarkdownHitTester.GetCaretX(lastRun, offset));
            return true;
        }

        // Already at start.
        next = caret;
        return false;
    }

    private static bool TryMoveRight(ImmutableArray<LineLayout> lines, MarkdownHitTestResult caret, out MarkdownHitTestResult next)
    {
        var linePos = caret.LineIndex;
        if (linePos < 0 || linePos >= lines.Length)
        {
            next = default;
            return false;
        }

        var line = lines[linePos];
        if (line.Runs.Length == 0)
        {
            next = default;
            return false;
        }

        var runIndex = Math.Clamp(caret.RunIndex, 0, line.Runs.Length - 1);
        var run = line.Runs[runIndex];
        var runLen = GetRunLogicalLength(run);

        // Move within the current run in *visual* order (RTL inverts logical offsets).
        if (run.Kind != NodeKind.Image && runLen > 0)
        {
            var visual = run.IsRightToLeft ? (runLen - caret.TextOffset) : caret.TextOffset;
            if (visual < runLen)
            {
                var visualNew = visual + 1;
                var logicalNew = run.IsRightToLeft ? (runLen - visualNew) : visualNew;
                logicalNew = Math.Clamp(logicalNew, 0, runLen);
                next = new MarkdownHitTestResult(caret.LineIndex, runIndex, run, line, logicalNew, MarkdownHitTester.GetCaretX(run, logicalNew));
                return true;
            }
        }

        // Cross-run fallback: probe slightly to the right of the current caret.
        var probeX = caret.CaretX + 0.5f;
        if (MarkdownHitTester.TryHitTestLine(caret.LineIndex, line, probeX, out var hit) && (hit.RunIndex != caret.RunIndex || hit.TextOffset != caret.TextOffset))
        {
            next = hit;
            return true;
        }

        // Move to next non-empty line.
        for (var i = linePos + 1; i < lines.Length; i++)
        {
            var nl = lines[i];
            if (nl.Runs.Length == 0)
            {
                continue;
            }

            var firstRun = nl.Runs[0];
            next = new MarkdownHitTestResult(i, 0, firstRun, nl, 0, MarkdownHitTester.GetCaretX(firstRun, 0));
            return true;
        }

        // Already at end.
        next = caret;
        return false;
    }

    private static bool TryMoveVertical(ImmutableArray<LineLayout> lines, MarkdownHitTestResult caret, int delta, out MarkdownHitTestResult next)
    {
        var linePos = caret.LineIndex;
        if (linePos < 0 || linePos >= lines.Length)
        {
            next = default;
            return false;
        }

        var targetX = caret.CaretX;

        for (var i = linePos + delta; i >= 0 && i < lines.Length; i += delta)
        {
            var lineIndex = i;
            var line = lines[i];
            if (line.Runs.Length == 0)
            {
                continue;
            }

            if (MarkdownHitTester.TryHitTestLine(lineIndex, line, targetX, out var hit))
            {
                next = hit;
                return true;
            }

            // If hit-testing fails for this line (unexpected), keep searching.
        }

        next = caret;
        return false;
    }

    private static int GetRunLogicalLength(InlineRunLayout run)
    {
        if (run.Kind == NodeKind.Image)
        {
            return 1;
        }

        return run.Text?.Length ?? 0;
    }

    // Note: line enumeration is centralized in MarkdownLineIndexCache.
}
