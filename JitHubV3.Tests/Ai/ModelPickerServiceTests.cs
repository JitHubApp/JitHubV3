using System.Collections.ObjectModel;
using System.ComponentModel;
using JitHubV3.Services.Ai;
using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Tests.Ai;

public sealed class ModelPickerServiceTests
{
    [Test]
    public async Task ShowAsync_EmitsSelectedModelsChanged_WhenSelectionMutates()
    {
        var pickerVm = new FakePickerViewModel();

        var sut = new ModelPickerService(pickerVm, modelStore: new FakeModelStore());

        var seen = new List<ModelPickerSelectedModelsChanged>();
        sut.SelectedModelsChanged += evt => seen.Add(evt);

        var invocation = new ModelPickerInvocation(
            PrimaryAction: PickerPrimaryAction.Apply,
            Slots: Array.Empty<ModelPickerSlot>(),
            PersistSelection: false);

        var showTask = sut.ShowAsync(invocation, CancellationToken.None);

    // Let the service open the overlay.
        await Task.Yield();

        pickerVm.SelectedModels.Add(new PickerSelectedModel(
            SlotId: "slot-1",
            RuntimeId: "local-foundry",
            ModelId: "m1",
            DisplayName: "Model 1"));

        await AssertEventuallyAsync(() => seen.Count >= 1, timeout: TimeSpan.FromSeconds(2));

        // Close the overlay to complete ShowAsync.
        pickerVm.LastCloseReason = ModelPickerCloseReason.Canceled;
        pickerVm.IsOpen = false;

        var result = await showTask;
        result.WasConfirmed.Should().BeFalse();
        result.SelectedModels.Should().NotBeEmpty();

        seen.Last().SelectedModels.Should().Contain(m => m.ModelId == "m1");
    }

    private static async Task AssertEventuallyAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        condition().Should().BeTrue("condition did not become true within the timeout");
    }

    private sealed class FakePickerViewModel : IModelPickerOverlayViewModel
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<PickerSelectedModel> SelectedModels { get; } = new();

        public ModelPickerCloseReason LastCloseReason { get; set; } = ModelPickerCloseReason.Unknown;

        private bool _isOpen;
        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                if (_isOpen == value)
                {
                    return;
                }

                _isOpen = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOpen)));
            }
        }

        public void SetInvocation(ModelPickerInvocation invocation)
        {
            // no-op for tests
        }

        public IReadOnlyList<PickerSelectedModel> GetSelectedModelsSnapshot()
            => SelectedModels.ToArray();
    }

    private sealed class FakeModelStore : IAiModelStore
    {
        public ValueTask<AiModelSelection?> GetSelectionAsync(CancellationToken ct)
            => ValueTask.FromResult<AiModelSelection?>(null);

        public ValueTask SetSelectionAsync(AiModelSelection? selection, CancellationToken ct)
            => ValueTask.CompletedTask;
    }
}
