using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Automation.Peers;

namespace JitHub.Markdown.Uno;

internal sealed class SkiaMarkdownViewAutomationPeer : FrameworkElementAutomationPeer
{
    public SkiaMarkdownViewAutomationPeer(SkiaMarkdownView owner)
        : base(owner)
    {
    }

    private SkiaMarkdownView OwnerView => (SkiaMarkdownView)Owner;

    protected override string GetClassNameCore() => nameof(SkiaMarkdownView);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override IList<AutomationPeer> GetChildrenCore()
    {
        var tree = OwnerView.BuildAccessibilityTreeForAutomation();
        if (tree is null)
        {
            return Array.Empty<AutomationPeer>();
        }

        var builder = new List<AutomationPeer>(tree.Root.Children.Length);
        for (var i = 0; i < tree.Root.Children.Length; i++)
        {
            builder.Add(new MarkdownAccessibilityNodeAutomationPeer(OwnerView, tree.Root.Children[i]));
        }

        return builder;
    }
}

internal sealed class MarkdownAccessibilityNodeAutomationPeer : AutomationPeer, IInvokeProvider
{
    private readonly SkiaMarkdownView _owner;
    private readonly AccessibilityNode _node;

    public MarkdownAccessibilityNodeAutomationPeer(SkiaMarkdownView owner, AccessibilityNode node)
    {
        _owner = owner;
        _node = node;
    }

    public void Invoke()
    {
        if (_node.Role != MarkdownAccessibilityRole.Link || string.IsNullOrWhiteSpace(_node.Url))
        {
            return;
        }

        _owner.ActivateLinkForAutomation(_node.Url);
    }

    protected override IList<AutomationPeer> GetChildrenCore()
    {
        if (_node.Children.IsDefaultOrEmpty)
        {
            return Array.Empty<AutomationPeer>();
        }

        var list = new List<AutomationPeer>(_node.Children.Length);
        for (var i = 0; i < _node.Children.Length; i++)
        {
            list.Add(new MarkdownAccessibilityNodeAutomationPeer(_owner, _node.Children[i]));
        }

        return list;
    }

    protected override string GetClassNameCore() => nameof(AccessibilityNode);

    protected override AutomationControlType GetAutomationControlTypeCore()
        => _node.Role switch
        {
            MarkdownAccessibilityRole.Document => AutomationControlType.Document,
            MarkdownAccessibilityRole.Heading => AutomationControlType.Header,
            MarkdownAccessibilityRole.Paragraph => AutomationControlType.Text,
            MarkdownAccessibilityRole.List => AutomationControlType.List,
            MarkdownAccessibilityRole.ListItem => AutomationControlType.ListItem,
            MarkdownAccessibilityRole.Link => AutomationControlType.Hyperlink,
            _ => AutomationControlType.Group,
        };

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;

    protected override object GetPatternCore(PatternInterface patternInterface)
    {
        if (patternInterface == PatternInterface.Invoke && _node.Role == MarkdownAccessibilityRole.Link && !string.IsNullOrWhiteSpace(_node.Url))
        {
            return this;
        }

        return base.GetPatternCore(patternInterface);
    }

    protected override string GetNameCore()
    {
        if (!string.IsNullOrWhiteSpace(_node.Name))
        {
            return _node.Name;
        }

        return _node.Role.ToString();
    }

    protected override string GetHelpTextCore()
        => _node.Role == MarkdownAccessibilityRole.Link && !string.IsNullOrWhiteSpace(_node.Url)
            ? _node.Url!
            : string.Empty;

    protected override Windows.Foundation.Rect GetBoundingRectangleCore()
        => _owner.GetAutomationBoundingRect(_node.Bounds);

    protected override bool IsOffscreenCore() => false;

    protected override bool HasKeyboardFocusCore()
    {
        if (_node.Role != MarkdownAccessibilityRole.Link)
        {
            return false;
        }

        return _owner.IsLinkFocusedForAutomation(_node.Id, _node.Url);
    }

    protected override void SetFocusCore()
    {
        if (_node.Role == MarkdownAccessibilityRole.Link && !string.IsNullOrWhiteSpace(_node.Url))
        {
            _owner.FocusLinkForAutomation(_node.Id, _node.Url!, _node.Bounds);
        }
        else
        {
            _owner.EnsureAutomationBoundsVisible(_node.Bounds);
            _owner.Focus(FocusState.Programmatic);
        }

        // Narrator relies heavily on focus change signals.
        // Uno may not implement this on all targets; ignore failures.
        try
        {
            GetType().GetMethod(
                    "RaiseAutomationEvent",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(AutomationEvents) },
                    modifiers: null)
                ?.Invoke(this, new object[] { AutomationEvents.AutomationFocusChanged });
        }
        catch
        {
        }
    }
}
