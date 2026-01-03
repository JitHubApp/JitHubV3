namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class LemonadePickerViewModel : PickerCategoryViewModel
{
    public override string TemplateKey => "LemonadeTemplate";

    public override string FooterSummary => "Lemonade";

    public override bool CanApply => false;

    public override Task ApplyAsync(CancellationToken ct) => Task.CompletedTask;
}
