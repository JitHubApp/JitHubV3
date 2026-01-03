namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class WinAiApisPickerViewModel : PickerCategoryViewModel
{
    public override string TemplateKey => "WinAiApisTemplate";

    public override string FooterSummary => "Windows AI APIs";

    public override bool CanApply => false;

    public override Task ApplyAsync(CancellationToken ct) => Task.CompletedTask;
}
