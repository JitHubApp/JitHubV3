using System.Collections.Immutable;

namespace JitHub.Markdown;

public enum MarkdownKeyCommand
{
    Left,
    Right,
    Up,
    Down,
    Tab,
    Enter,
}

public readonly record struct FocusedLink(NodeId Id, string Url, RectF Bounds);

public readonly record struct KeyboardInteractionResult(
    bool SelectionChanged,
    SelectionRange? Selection,
    bool FocusChanged,
    FocusedLink? FocusedLink,
    string? ActivateLinkUrl,
    bool Handled);

/// <summary>
/// Core (platform-agnostic) keyboard interaction for a markdown layout.
/// </summary>
public sealed class SelectionKeyboardInteraction
{
    public SelectionRange? Selection { get; set; }

    public FocusedLink? FocusedLink { get; private set; }

    public void ClearLinkFocus()
    {
        FocusedLink = null;
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

        foreach (var (lineIndex, line) in EnumerateLinesWithIndex(layout))
        {
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
        foreach (var (lineIndex, line) in EnumerateLinesWithIndex(layout))
        {
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

        foreach (var (lineIndex, line) in EnumerateLinesWithIndex(layout))
        {
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
        var lines = EnumerateLinesWithIndex(layout).ToList();
        if (lines.Count == 0)
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

    private static bool TryMoveLeft(List<(int lineIndex, LineLayout line)> lines, MarkdownHitTestResult caret, out MarkdownHitTestResult next)
    {
        if (!TryFindLine(lines, caret.LineIndex, out var linePos))
        {
            next = default;
            return false;
        }

        var line = lines[linePos].line;
        if (line.Runs.Length == 0)
        {
            next = default;
            return false;
        }

        var runIndex = Math.Clamp(caret.RunIndex, 0, line.Runs.Length - 1);
        var run = line.Runs[runIndex];

        if (run.Kind != NodeKind.Image && caret.TextOffset > 0)
        {
            var newOffset = Math.Max(0, caret.TextOffset - 1);
            next = new MarkdownHitTestResult(caret.LineIndex, runIndex, run, line, newOffset, MarkdownHitTester.GetCaretX(run, newOffset));
            return true;
        }

        // Move to previous run/line.
        if (runIndex > 0)
        {
            var prevIndex = runIndex - 1;
            var prevRun = line.Runs[prevIndex];
            var prevOffset = GetRunLogicalLength(prevRun);
            next = new MarkdownHitTestResult(caret.LineIndex, prevIndex, prevRun, line, prevOffset, MarkdownHitTester.GetCaretX(prevRun, prevOffset));
            return true;
        }

        // Move to previous non-empty line.
        for (var i = linePos - 1; i >= 0; i--)
        {
            var pl = lines[i].line;
            if (pl.Runs.Length == 0)
            {
                continue;
            }

            var lastRunIndex = pl.Runs.Length - 1;
            var lastRun = pl.Runs[lastRunIndex];
            var offset = GetRunLogicalLength(lastRun);
            next = new MarkdownHitTestResult(lines[i].lineIndex, lastRunIndex, lastRun, pl, offset, MarkdownHitTester.GetCaretX(lastRun, offset));
            return true;
        }

        // Already at start.
        next = caret;
        return false;
    }

    private static bool TryMoveRight(List<(int lineIndex, LineLayout line)> lines, MarkdownHitTestResult caret, out MarkdownHitTestResult next)
    {
        if (!TryFindLine(lines, caret.LineIndex, out var linePos))
        {
            next = default;
            return false;
        }

        var line = lines[linePos].line;
        if (line.Runs.Length == 0)
        {
            next = default;
            return false;
        }

        var runIndex = Math.Clamp(caret.RunIndex, 0, line.Runs.Length - 1);
        var run = line.Runs[runIndex];

        var runLen = GetRunLogicalLength(run);
        if (run.Kind != NodeKind.Image && caret.TextOffset < runLen)
        {
            var newOffset = Math.Min(runLen, caret.TextOffset + 1);
            next = new MarkdownHitTestResult(caret.LineIndex, runIndex, run, line, newOffset, MarkdownHitTester.GetCaretX(run, newOffset));
            return true;
        }

        // Move to next run/line.
        if (runIndex < line.Runs.Length - 1)
        {
            var nextIndex = runIndex + 1;
            var nextRun = line.Runs[nextIndex];
            next = new MarkdownHitTestResult(caret.LineIndex, nextIndex, nextRun, line, 0, MarkdownHitTester.GetCaretX(nextRun, 0));
            return true;
        }

        // Move to next non-empty line.
        for (var i = linePos + 1; i < lines.Count; i++)
        {
            var nl = lines[i].line;
            if (nl.Runs.Length == 0)
            {
                continue;
            }

            var firstRun = nl.Runs[0];
            next = new MarkdownHitTestResult(lines[i].lineIndex, 0, firstRun, nl, 0, MarkdownHitTester.GetCaretX(firstRun, 0));
            return true;
        }

        // Already at end.
        next = caret;
        return false;
    }

    private static bool TryMoveVertical(List<(int lineIndex, LineLayout line)> lines, MarkdownHitTestResult caret, int delta, out MarkdownHitTestResult next)
    {
        if (!TryFindLine(lines, caret.LineIndex, out var linePos))
        {
            next = default;
            return false;
        }

        var targetX = caret.CaretX;

        for (var i = linePos + delta; i >= 0 && i < lines.Count; i += delta)
        {
            var lineIndex = lines[i].lineIndex;
            var line = lines[i].line;
            if (line.Runs.Length == 0)
            {
                continue;
            }

            var runIndex = FindRunIndex(line.Runs, targetX);
            var run = line.Runs[runIndex];
            var textOffset = GetTextOffset(run, targetX);
            var caretX = MarkdownHitTester.GetCaretX(run, textOffset);
            next = new MarkdownHitTestResult(lineIndex, runIndex, run, line, textOffset, caretX);
            return true;
        }

        next = caret;
        return false;
    }

    private static bool TryFindLine(List<(int lineIndex, LineLayout line)> lines, int lineIndex, out int position)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].lineIndex == lineIndex)
            {
                position = i;
                return true;
            }
        }

        position = -1;
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

    // Duplicated from MarkdownHitTester to keep keyboard navigation consistent with pointer hit-testing.
    private static int FindRunIndex(ImmutableArray<InlineRunLayout> runs, float x)
    {
        for (var i = 0; i < runs.Length; i++)
        {
            var r = runs[i].Bounds;
            if (x >= r.X && x <= r.Right)
            {
                return i;
            }
        }

        if (x < runs[0].Bounds.X)
        {
            return 0;
        }

        return runs.Length - 1;
    }

    // Duplicated from MarkdownHitTester to keep keyboard navigation consistent with pointer hit-testing.
    private static int GetTextOffset(InlineRunLayout run, float x)
    {
        if (string.IsNullOrEmpty(run.Text))
        {
            return 0;
        }

        if (run.Kind == NodeKind.Image)
        {
            return 0;
        }

        var gx = run.GlyphX;
        if (gx.IsDefault || gx.Length == 0)
        {
            var w = Math.Max(1f, run.Bounds.Width);
            var t = Math.Clamp((x - run.Bounds.X) / w, 0f, 1f);
            return (int)MathF.Round(t * run.Text.Length);
        }

        if (x <= gx[0])
        {
            return 0;
        }

        var last = gx[gx.Length - 1];
        if (x >= last)
        {
            return run.Text.Length;
        }

        var lo = 0;
        var hi = gx.Length - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (gx[mid] <= x)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return Math.Clamp(lo - 1, 0, run.Text.Length);
    }

    private static IEnumerable<(int lineIndex, LineLayout line)> EnumerateLinesWithIndex(MarkdownLayout layout)
    {
        var i = 0;
        for (var bi = 0; bi < layout.Blocks.Length; bi++)
        {
            foreach (var line in EnumerateLines(layout.Blocks[bi]))
            {
                yield return (i, line);
                i++;
            }
        }
    }

    private static IEnumerable<LineLayout> EnumerateLines(BlockLayout block)
    {
        switch (block)
        {
            case ParagraphLayout p:
                foreach (var l in p.Lines) yield return l;
                yield break;

            case HeadingLayout h:
                foreach (var l in h.Lines) yield return l;
                yield break;

            case CodeBlockLayout c:
                foreach (var l in c.Lines) yield return l;
                yield break;

            case BlockQuoteLayout q:
                foreach (var child in q.Blocks)
                {
                    foreach (var l in EnumerateLines(child)) yield return l;
                }
                yield break;

            case ListLayout l:
                foreach (var item in l.Items)
                {
                    foreach (var ll in EnumerateLines(item)) yield return ll;
                }
                yield break;

            case ListItemLayout li:
                foreach (var child in li.Blocks)
                {
                    foreach (var ll in EnumerateLines(child)) yield return ll;
                }
                yield break;

            case TableLayout t:
                for (var r = 0; r < t.Rows.Length; r++)
                {
                    var row = t.Rows[r];
                    for (var c = 0; c < row.Cells.Length; c++)
                    {
                        var cell = row.Cells[c];
                        for (var bi = 0; bi < cell.Blocks.Length; bi++)
                        {
                            foreach (var ll in EnumerateLines(cell.Blocks[bi])) yield return ll;
                        }
                    }
                }
                yield break;

            default:
                yield break;
        }
    }
}
