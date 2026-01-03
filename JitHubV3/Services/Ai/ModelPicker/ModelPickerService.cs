using System.ComponentModel;
using JitHubV3.Presentation.Controls.ModelPicker;

namespace JitHubV3.Services.Ai.ModelPicker;

public sealed class ModelPickerService : IModelPickerService
{
    private readonly ModelOrApiPickerViewModel _picker;
    private readonly IAiModelStore _modelStore;

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

        void Handler(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(ModelOrApiPickerViewModel.IsOpen), StringComparison.Ordinal))
            {
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
                // Today we can only return a single persisted selection at best.
                var selection = await _modelStore.GetSelectionAsync(CancellationToken.None).ConfigureAwait(false);

                var slotId = invocation.Slots.FirstOrDefault()?.SlotId ?? "default";
                var selectedModels = selection is null
                    ? Array.Empty<PickerSelectedModel>()
                    : new[]
                    {
                        new PickerSelectedModel(
                            SlotId: slotId,
                            RuntimeId: selection.RuntimeId,
                            ModelId: selection.ModelId,
                            DisplayName: null)
                    };

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

        return await tcs.Task.ConfigureAwait(false);
    }
}
