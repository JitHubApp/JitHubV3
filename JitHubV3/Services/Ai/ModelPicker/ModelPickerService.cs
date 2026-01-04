using System.ComponentModel;
using JitHubV3.Presentation.Controls.ModelPicker;

namespace JitHubV3.Services.Ai.ModelPicker;

public sealed class ModelPickerService : IModelPickerService
{
    private readonly ModelOrApiPickerViewModel _picker;
    private readonly IAiModelStore _modelStore;

    public event Action<ModelPickerSelectedModelsChanged>? SelectedModelsChanged;

    public ModelPickerService(ModelOrApiPickerViewModel picker, IAiModelStore modelStore)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
    }

    public async Task<ModelPickerResult> ShowAsync(ModelPickerInvocation invocation, CancellationToken ct)
    {
        if (invocation is null) throw new ArgumentNullException(nameof(invocation));

        // Phase 1 scaffolding: we expose an invocation-aware service contract (gap report section 5.1),
        // but the existing picker VM is still single-selection (gap report sections 2.3 and 6.1).

        var tcs = new TaskCompletionSource<ModelPickerResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var lastSelectedModels = Array.Empty<PickerSelectedModel>();

        void RaiseSelectedModelsChangedIfNeeded()
        {
            var snapshot = _picker.GetSelectedModelsSnapshot();
            if (SequenceEqual(lastSelectedModels, snapshot))
            {
                return;
            }

            lastSelectedModels = snapshot.ToArray();
            SelectedModelsChanged?.Invoke(new ModelPickerSelectedModelsChanged(invocation, lastSelectedModels));
        }

        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(ModelOrApiPickerViewModel.IsOpen), StringComparison.Ordinal))
            {
                // Treat any picker VM change while open as a potential selection change.
                // We de-dupe emissions by comparing snapshots.
                if (_picker.IsOpen)
                {
                    RaiseSelectedModelsChangedIfNeeded();
                }
                return;
            }

            if (_picker.IsOpen)
            {
                return;
            }

            _picker.PropertyChanged -= Handler;
            _ = CompleteAsync();
        }

        async Task CompleteAsync()
        {
            try
            {
                var selectedModels = _picker.GetSelectedModelsSnapshot();

                // Phase 2: overlay/VM now tracks close reason so confirm vs cancel is reliable.
                var wasConfirmed = _picker.LastCloseReason == ModelPickerCloseReason.Confirmed;

                tcs.TrySetResult(new ModelPickerResult(wasConfirmed, selectedModels));
            }
            catch (OperationCanceledException oce)
            {
                tcs.TrySetCanceled(oce.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        using var reg = ct.Register(() =>
        {
            _picker.IsOpen = false;
            tcs.TrySetCanceled(ct);
        });

        _picker.PropertyChanged += Handler;
        _picker.SetInvocation(invocation);
        _picker.IsOpen = true;

        // Emit initial selection snapshot once opened.
        RaiseSelectedModelsChangedIfNeeded();

        return await tcs.Task.ConfigureAwait(false);
    }

    private static bool SequenceEqual(IReadOnlyList<PickerSelectedModel> left, IReadOnlyList<PickerSelectedModel> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }
}
