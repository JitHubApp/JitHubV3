namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class OllamaPickerViewModel : PickerCategoryViewModel
{
    public override string TemplateKey => "OllamaTemplate";

    public override string FooterSummary => "Ollama";

    public override bool CanApply => false;

    public override Task ApplyAsync(CancellationToken ct) => Task.CompletedTask;
}
