using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JitHubV3.Presentation;

public sealed partial class MarkdownTestViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Markdown Test";

    [ObservableProperty]
    private string _markdown = "# Markdown Test\n\nThis is a **Phase 0** placeholder view.\n\n- [x] GFM extensions enabled in parser\n- Inline `code` sample\n\n```csharp\nvar x = 1;\n```\n";
}
