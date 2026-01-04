using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed class PickerCategoryTemplateSelector : DataTemplateSelector
{
    public DataTemplate? LocalModelsTemplate { get; set; }

    public DataTemplate? FoundryLocalTemplate { get; set; }

    public DataTemplate? WinAiApisTemplate { get; set; }

    public DataTemplate? OnnxTemplate { get; set; }

    public DataTemplate? OllamaTemplate { get; set; }

    public DataTemplate? OpenAiTemplate { get; set; }

    public DataTemplate? LemonadeTemplate { get; set; }

    public DataTemplate? AnthropicTemplate { get; set; }

    public DataTemplate? FoundryTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
        => SelectForItem(item);

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectForItem(item);

    private DataTemplate? SelectForItem(object item)
        => item switch
        {
            LocalModelsPickerViewModel => LocalModelsTemplate,
            FoundryLocalPickerViewModel => FoundryLocalTemplate,
            WinAiApisPickerViewModel => WinAiApisTemplate,
            OnnxPickerViewModel => OnnxTemplate,
            OllamaPickerViewModel => OllamaTemplate,
            OpenAiPickerViewModel => OpenAiTemplate,
            LemonadePickerViewModel => LemonadeTemplate,
            AnthropicPickerViewModel => AnthropicTemplate,
            AzureAiFoundryPickerViewModel => FoundryTemplate,
            _ => null,
        };
}
