using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public abstract partial class PickerCategoryViewModel : ObservableObject
{
    public abstract string TemplateKey { get; }

    public abstract string FooterSummary { get; }

    public abstract bool CanApply { get; }

    public abstract Task ApplyAsync(CancellationToken ct);

    public virtual Task RefreshAsync(CancellationToken ct) => Task.CompletedTask;

    // Phase 5 (gap report section 2.3): the footer shows a chip list of selected models.
    // Current implementation is single-selection; panes may return 0-1 items.
    public virtual IReadOnlyList<PickerSelectedModel> GetSelectedModels() => Array.Empty<PickerSelectedModel>();

    public virtual void RemoveSelectedModel(PickerSelectedModel model)
    {
        // default: no-op
    }
}
