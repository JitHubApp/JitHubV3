using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;

namespace JitHubV3.Presentation;

public sealed partial class MarkdownTestViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Markdown Test";

    [ObservableProperty]
    private string _markdown = DefaultMarkdownLtr;

    [ObservableProperty]
    private bool _forceRtl;

    [ObservableProperty]
    private TextAlignment _previewTextAlignment = GetPlatformIsRtl() ? TextAlignment.Right : TextAlignment.Left;

    [ObservableProperty]
    private bool _previewIsRightToLeft = GetPlatformIsRtl();

    private string _markdownLtrDraft = DefaultMarkdownLtr;
    private string _markdownRtlDraft = DefaultMarkdownRtl;

    partial void OnForceRtlChanged(bool value)
    {
        // Preserve edits per-mode so you can flip back and forth while testing.
        if (value)
        {
            _markdownLtrDraft = Markdown;
            Markdown = _markdownRtlDraft;
        }
        else
        {
            _markdownRtlDraft = Markdown;
            Markdown = _markdownLtrDraft;
        }

        PreviewIsRightToLeft = value || GetPlatformIsRtl();
        PreviewTextAlignment = PreviewIsRightToLeft ? TextAlignment.Right : TextAlignment.Left;
    }

    partial void OnMarkdownChanged(string value)
    {
        if (ForceRtl)
        {
            _markdownRtlDraft = value;
        }
        else
        {
            _markdownLtrDraft = value;
        }
    }

    private static bool GetPlatformIsRtl()
        => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    private const string DefaultMarkdownLtr = "# Markdown Test Harness\n\nThis page is wired to the real `MarkdownView` renderer and input pipeline.\n\n## Quick keyboard check\n\n- Click the markdown area so it has focus\n- Press Tab to focus the next link\n- Press Enter to activate the focused link\n- Use Arrow keys to move the caret (when selection is enabled)\n\n## Links (many targets)\n\nInline links: [Example](https://example.com) · [Docs](https://learn.microsoft.com) · [Uno Platform](https://platform.uno) · [GitHub](https://github.com) · [NUnit](https://nunit.org).\n\nThis paragraph forces wrapping and includes lots of small links so you can tab through them: [One](https://example.com/one) [Two](https://example.com/two) [Three](https://example.com/three) [Four](https://example.com/four) [Five](https://example.com/five) [Six](https://example.com/six) [Seven](https://example.com/seven) [Eight](https://example.com/eight) [Nine](https://example.com/nine) [Ten](https://example.com/ten).\n\nA multi-line link target that wraps in the middle of the sentence: Go to [the official Uno Platform documentation site](https://platform.uno/docs/articles/intro.html) to confirm focus outlines and activation behavior.\n\n## Lists with links\n\n- Basics\n  - [Getting Started](https://example.com/start)\n  - [Configuration](https://example.com/config)\n  - [FAQ](https://example.com/faq)\n- Nested\n  - Level 1\n    - Level 2: [Link A](https://example.com/a) and [Link B](https://example.com/b)\n    - Level 2: [Link C](https://example.com/c)\n  - Back to Level 1: [Link D](https://example.com/d)\n- Mixed text and link\n  - Here is some text, then a link: [Open resource](https://example.com/resource) and more trailing text.\n\n## Table with links\n\n| Name | Link | Notes |\n|---|---|---|\n| Alpha | [alpha](https://example.com/alpha) | First row |\n| Beta | [beta](https://example.com/beta) | Second row |\n| Gamma | [gamma](https://example.com/gamma) | Third row |\n| Delta | [delta](https://example.com/delta) | Fourth row |\n\n## Blockquote with links\n\n> A quote that includes keyboard-focusable links: [Inside quote 1](https://example.com/q1) and [Inside quote 2](https://example.com/q2).\n\n## Wrapping / hit testing\n\nThis is a long paragraph to exercise wrapping and hit testing. It should wrap naturally and still allow selection at word boundaries without jumping. It also includes links mid-paragraph so you can test focus bounds within wrapped text: [Wrapped Link 1](https://example.com/w1) [Wrapped Link 2](https://example.com/w2) [Wrapped Link 3](https://example.com/w3).\n\n## Code (should not be focusable)\n\nInline `code` sample, plus a code block containing a fake link that should remain plain text: `https://example.com/not-a-link`.\n\n```csharp\nusing System;\n\npublic static class Demo\n{\n    public static void Main()\n    {\n        Console.WriteLine(\"Hello MarkdownView\");\n        // [Not a link](https://example.com) inside code block\n    }\n}\n```\n\n## Selection\n\nTry selection behaviors:\n\n- Drag to select text across words/lines\n- Shift+click to extend the selection\n- Click a link to activate it (no selection)\n\n> Blockquote: select me too.\n";

    private const string DefaultMarkdownRtl = "# اختبار RTL\n\nاستخدم هذه الصفحة لتأكيد دعم RTL من طرف إلى طرف (محاذاة الأسطر، موضع علامات القوائم، النقر/التحديد).\n\n## فقرات\n\nمرحبا بالعالم — هذا سطر عربي بسيط لاختبار المحاذاة إلى اليمين والتفاف الأسطر.\n\nעברית: זהו משפט בעברית כדי לבדוק יישור לימין והצגת רשימות.\n\nمزج: English ثم العربية: Hello مرحبا world.\n\n## روابط\n\nروابط داخل السطر: [مثال](https://example.com) · [Uno](https://platform.uno) · [Docs](https://learn.microsoft.com).\n\nهدف رابط طويل يجب أن يلتف عبر أسطر متعددة: اذهب إلى [موقع التوثيق الرسمي لمنصة أونو](https://platform.uno/docs/articles/intro.html) للتأكد من سلوك التركيز.\n\n## قوائم\n\n- عنصر أول\n- عنصر ثانٍ مع رابط: [افتح](https://example.com/open)\n- عنصر ثالث\n\n> اقتباس: هذا نص عربي داخل اقتباس لاختبار المحاذاة والتفاف الأسطر.\n";
}
