using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JitHubV3.Presentation;

public sealed partial class MarkdownTestViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Markdown Test";

    [ObservableProperty]
    private string _markdown = "# Markdown Test Harness\n\nThis page is wired to the real `MarkdownView` renderer and input pipeline.\n\n## Try selection\n\n- Drag to select text across words/lines\n- Shift+click to extend the selection\n- Click a link to activate it (no selection)\n\nHere is a link: https://example.com\n\n## Wrapping\n\nThis is a long paragraph to exercise wrapping and hit testing. It should wrap naturally and still allow selection at word boundaries without jumping.\n\n## Lists\n\n- Item one\n- Item two\n  - Nested item\n\n## Code\n\nInline `code` sample.\n\n```csharp\nusing System;\n\npublic static class Demo\n{\n    public static void Main()\n    {\n        Console.WriteLine(\"Hello MarkdownView\");\n    }\n}\n```\n\n> Blockquote: select me too.\n";
}
