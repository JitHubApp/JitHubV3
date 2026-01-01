namespace JitHubV3.Presentation.Controls.ModelPicker;

public abstract partial class PickerCategoryViewModel : ObservableObject
{
    public abstract string FooterSummary { get; }

    public abstract bool CanApply { get; }

    public abstract Task ApplyAsync(CancellationToken ct);

    public virtual Task RefreshAsync(CancellationToken ct) => Task.CompletedTask;
}
