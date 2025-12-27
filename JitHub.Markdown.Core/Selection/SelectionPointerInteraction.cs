namespace JitHub.Markdown;

public readonly record struct PointerModifiers(bool Shift);

public readonly record struct PointerInteractionResult(
    bool SelectionChanged,
    SelectionRange? Selection,
    string? ActivateLinkUrl);

/// <summary>
/// Pure (platform-agnostic) pointer interaction state machine.
/// The platform adapter is responsible for hit testing and for providing modifier keys.
/// </summary>
public sealed class SelectionPointerInteraction
{
    private const float DragThreshold = 3f;

    private bool _isPressed;
    private bool _startedSelection;
    private float _pressX;
    private float _pressY;

    private MarkdownHitTestResult _anchor;
    private MarkdownHitTestResult? _pendingLinkHit;

    public SelectionRange? Selection { get; private set; }

    public void SetSelection(SelectionRange? selection)
    {
        Selection = selection;
        ResetPointerState();
    }

    public void ClearSelection()
    {
        Selection = null;
        ResetPointerState();
    }

    /// <summary>
    /// Cancels the current pointer gesture without changing the existing selection.
    /// Useful when the platform does not reliably raise PointerReleased/PointerCanceled
    /// (e.g. capture lost, browser quirks).
    /// </summary>
    public void CancelPointer()
    {
        ResetPointerState();
    }

    public PointerInteractionResult OnPointerDown(MarkdownHitTestResult hit, float x, float y, bool selectionEnabled, PointerModifiers modifiers)
    {
        _isPressed = true;
        _pressX = x;
        _pressY = y;

        _startedSelection = false;
        _pendingLinkHit = null;

        // Shift+click always extends/creates a selection (Windows behavior).
        if (selectionEnabled && modifiers.Shift)
        {
            var anchor = Selection?.Anchor ?? hit;
            _anchor = anchor;
            _startedSelection = true;
            Selection = new SelectionRange(anchor, hit);
            return new PointerInteractionResult(SelectionChanged: true, Selection: Selection, ActivateLinkUrl: null);
        }

        // For a simple click on a link, prefer activation over collapsing selection.
        if (hit.Run.Kind == NodeKind.Link && !string.IsNullOrWhiteSpace(hit.Run.Url))
        {
            _pendingLinkHit = hit;
            return new PointerInteractionResult(SelectionChanged: false, Selection: Selection, ActivateLinkUrl: null);
        }

        if (!selectionEnabled)
        {
            return new PointerInteractionResult(SelectionChanged: false, Selection: Selection, ActivateLinkUrl: null);
        }

        // Start a new selection.
        _anchor = hit;
        _startedSelection = true;
        Selection = new SelectionRange(hit, hit);
        return new PointerInteractionResult(SelectionChanged: true, Selection: Selection, ActivateLinkUrl: null);
    }

    public PointerInteractionResult OnPointerMove(MarkdownHitTestResult hit, float x, float y, bool selectionEnabled)
    {
        if (!_isPressed)
        {
            return new PointerInteractionResult(SelectionChanged: false, Selection: Selection, ActivateLinkUrl: null);
        }

        if (!_startedSelection)
        {
            // We pressed on a link; if the pointer starts dragging, either switch to selection mode
            // (when enabled) or cancel activation (when disabled).
            var dx = x - _pressX;
            var dy = y - _pressY;
            var dist2 = (dx * dx) + (dy * dy);
            if (dist2 < (DragThreshold * DragThreshold))
            {
                return new PointerInteractionResult(SelectionChanged: false, Selection: Selection, ActivateLinkUrl: null);
            }

            if (!selectionEnabled)
            {
                _pendingLinkHit = null;
                return new PointerInteractionResult(SelectionChanged: false, Selection: Selection, ActivateLinkUrl: null);
            }

            // Start selection anchored at the original press hit if available.
            var anchor = _pendingLinkHit ?? hit;
            _pendingLinkHit = null;
            _anchor = anchor;
            _startedSelection = true;
            Selection = new SelectionRange(anchor, hit);
            return new PointerInteractionResult(SelectionChanged: true, Selection: Selection, ActivateLinkUrl: null);
        }

        if (!selectionEnabled)
        {
            return new PointerInteractionResult(SelectionChanged: false, Selection: Selection, ActivateLinkUrl: null);
        }

        var next = new SelectionRange(_anchor, hit);
        if (Selection.HasValue && next.Equals(Selection.Value))
        {
            return new PointerInteractionResult(SelectionChanged: false, Selection: Selection, ActivateLinkUrl: null);
        }

        Selection = next;
        return new PointerInteractionResult(SelectionChanged: true, Selection: Selection, ActivateLinkUrl: null);
    }

    public PointerInteractionResult OnPointerUp(MarkdownHitTestResult hit, bool selectionEnabled)
    {
        // Click activation for links (tap/click to activate link).
        if (_pendingLinkHit.HasValue && !_startedSelection)
        {
            var url = _pendingLinkHit.Value.Run.Url;
            ResetPointerState();
            return new PointerInteractionResult(SelectionChanged: false, Selection: Selection, ActivateLinkUrl: url);
        }

        // Ensure we end in a consistent state.
        if (_startedSelection && selectionEnabled)
        {
            Selection = new SelectionRange(_anchor, hit);
            ResetPointerState();
            return new PointerInteractionResult(SelectionChanged: true, Selection: Selection, ActivateLinkUrl: null);
        }

        ResetPointerState();
        return new PointerInteractionResult(SelectionChanged: false, Selection: Selection, ActivateLinkUrl: null);
    }

    private void ResetPointerState()
    {
        _isPressed = false;
        _startedSelection = false;
        _pendingLinkHit = null;
    }
}
