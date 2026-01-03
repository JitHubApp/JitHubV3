namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class OnnxPickerViewModel : PickerCategoryViewModel
{
    public override string TemplateKey => "OnnxTemplate";

    public override string FooterSummary => "Custom models";

    public override bool CanApply => false;

    public override Task ApplyAsync(CancellationToken ct) => Task.CompletedTask;
}
