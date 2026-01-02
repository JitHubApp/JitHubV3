using JitHubV3.Presentation.Controls.ModelPicker;
using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Ai;

public sealed class LocalModelsPickerViewModelTests
{
    [Test]
    public async Task RefreshAsync_populates_items_and_restores_local_selection()
    {
        var catalog = new FakeLocalModelCatalog(new[]
        {
            new AiLocalModelCatalogItem(
                ModelId: "phi3",
                DisplayName: "Phi-3",
                RuntimeId: "local-foundry",
                IsDownloaded: true,
                InstallPath: "C:\\models\\phi3"),
        });

        var downloads = new FakeDownloadQueue();

        var modelStore = new FakeModelStore(new AiModelSelection(RuntimeId: "local-foundry", ModelId: "phi3"));

        var definitions = new[]
        {
            new AiLocalModelDefinition(
                ModelId: "phi3",
                DisplayName: "Phi-3",
                RuntimeId: "local-foundry",
                DefaultInstallFolderName: "phi3",
                DownloadUri: "https://example.invalid/phi3.bin",
                ArtifactFileName: "phi3.bin",
                ExpectedBytes: 123,
                ExpectedSha256: null),
        };

        var events = new RecordingAiStatusEventPublisher();

        var vm = new LocalModelsPickerViewModel(catalog, downloads, modelStore, events, definitions);

        await vm.RefreshAsync(CancellationToken.None);

        vm.Items.Should().HaveCount(1);
        vm.Items[0].ModelId.Should().Be("phi3");
        vm.Items[0].IsDownloaded.Should().BeTrue();
        vm.SelectedItem.Should().NotBeNull();
        vm.SelectedItem!.ModelId.Should().Be("phi3");
        vm.CanApply.Should().BeTrue();
    }

    [Test]
    public async Task ApplyAsync_does_nothing_when_selected_item_not_downloaded()
    {
        var catalog = new FakeLocalModelCatalog(new[]
        {
            new AiLocalModelCatalogItem(
                ModelId: "m1",
                DisplayName: null,
                RuntimeId: "local-foundry",
                IsDownloaded: false,
                InstallPath: "C:\\models\\m1"),
        });

        var downloads = new FakeDownloadQueue();
        var modelStore = new FakeModelStore(selection: null);

        var events = new RecordingAiStatusEventPublisher();

        var vm = new LocalModelsPickerViewModel(
            catalog,
            downloads,
            modelStore,
            events,
            definitions: Array.Empty<AiLocalModelDefinition>());

        await vm.RefreshAsync(CancellationToken.None);
        vm.SelectedItem = vm.Items.Single();

        vm.CanApply.Should().BeFalse();

        await vm.ApplyAsync(CancellationToken.None);

        modelStore.LastSetSelection.Should().BeNull();
    }

    [Test]
    public async Task ApplyAsync_sets_selection_when_downloaded()
    {
        var catalog = new FakeLocalModelCatalog(new[]
        {
            new AiLocalModelCatalogItem(
                ModelId: "m1",
                DisplayName: "Model 1",
                RuntimeId: "local-foundry",
                IsDownloaded: true,
                InstallPath: "C:\\models\\m1"),
        });

        var downloads = new FakeDownloadQueue();
        var modelStore = new FakeModelStore(selection: null);

        var events = new RecordingAiStatusEventPublisher();

        var vm = new LocalModelsPickerViewModel(
            catalog,
            downloads,
            modelStore,
            events,
            definitions: Array.Empty<AiLocalModelDefinition>());

        await vm.RefreshAsync(CancellationToken.None);
        vm.SelectedItem = vm.Items.Single();

        vm.CanApply.Should().BeTrue();

        await vm.ApplyAsync(CancellationToken.None);

        modelStore.LastSetSelection.Should().Be(new AiModelSelection(RuntimeId: "local-foundry", ModelId: "m1"));
    }

    [Test]
    public async Task RefreshAsync_attaches_to_active_downloads_and_updates_progress()
    {
        var catalog = new FakeLocalModelCatalog(new[]
        {
            new AiLocalModelCatalogItem(
                ModelId: "m1",
                DisplayName: null,
                RuntimeId: "local-foundry",
                IsDownloaded: false,
                InstallPath: "C:\\models\\m1"),
        });

        var downloads = new FakeDownloadQueue();
        var modelStore = new FakeModelStore(selection: null);

        var events = new RecordingAiStatusEventPublisher();

        var handle = downloads.Enqueue(new AiModelDownloadRequest(
            ModelId: "m1",
            RuntimeId: "local-foundry",
            SourceUri: new Uri("https://example.invalid/m1.bin"),
            InstallPath: "C:\\models\\m1",
            ArtifactFileName: "m1.bin"));

        var vm = new LocalModelsPickerViewModel(
            catalog,
            downloads,
            modelStore,
            events,
            definitions: Array.Empty<AiLocalModelDefinition>());

        await vm.RefreshAsync(CancellationToken.None);

        var item = vm.Items.Single();
        item.CanCancel.Should().BeTrue();

        downloads.Publish(handle, status: AiModelDownloadStatus.Downloading, progress: 0.5);

        item.ProgressPercent.Should().Be(50.0);
        item.StatusText.Should().Contain("Downloading");
        item.IsDownloaded.Should().BeFalse();

        downloads.Publish(handle, status: AiModelDownloadStatus.Completed, progress: 1.0);
        item.IsDownloaded.Should().BeTrue();
        item.StatusText.Should().Be("Downloaded");

        events.Events.OfType<AiDownloadProgressChanged>().Should().NotBeEmpty();
    }

    private sealed class RecordingAiStatusEventPublisher : IAiStatusEventPublisher
    {
        public List<AiStatusEvent> Events { get; } = new();

        public void Publish(AiStatusEvent evt) => Events.Add(evt);
    }

    private sealed class FakeLocalModelCatalog : IAiLocalModelCatalog
    {
        private readonly IReadOnlyList<AiLocalModelCatalogItem> _items;

        public FakeLocalModelCatalog(IReadOnlyList<AiLocalModelCatalogItem> items) => _items = items;

        public ValueTask<IReadOnlyList<AiLocalModelCatalogItem>> GetCatalogAsync(CancellationToken ct)
            => ValueTask.FromResult(_items);
    }

    private sealed class FakeModelStore : IAiModelStore
    {
        private AiModelSelection? _selection;

        public FakeModelStore(AiModelSelection? selection) => _selection = selection;

        public AiModelSelection? LastSetSelection { get; private set; }

        public ValueTask<AiModelSelection?> GetSelectionAsync(CancellationToken ct)
            => ValueTask.FromResult(_selection);

        public ValueTask SetSelectionAsync(AiModelSelection? selection, CancellationToken ct)
        {
            _selection = selection;
            LastSetSelection = selection;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeDownloadQueue : IAiModelDownloadQueue
    {
        private readonly List<AiModelDownloadHandle> _active = new();

        public event Action? DownloadsChanged;

        public AiModelDownloadHandle Enqueue(AiModelDownloadRequest request)
        {
            var handle = new AiModelDownloadHandle(Guid.NewGuid(), request, new CancellationTokenSource());
            _active.Add(handle);
            DownloadsChanged?.Invoke();
            return handle;
        }

        public IReadOnlyList<AiModelDownloadHandle> GetActiveDownloads() => _active.ToArray();

        public AiModelDownloadHandle? TryGet(Guid downloadId)
            => _active.FirstOrDefault(h => h.Id == downloadId);

        public bool Cancel(Guid downloadId)
        {
            var h = TryGet(downloadId);
            if (h is null)
            {
                return false;
            }

            Publish(h, AiModelDownloadStatus.Canceled, progress: null);
            _active.Remove(h);
            DownloadsChanged?.Invoke();
            return true;
        }

        public void Publish(AiModelDownloadHandle handle, AiModelDownloadStatus status, double? progress)
        {
            var p = new AiModelDownloadProgress(
                DownloadId: handle.Id,
                ModelId: handle.Request.ModelId,
                RuntimeId: handle.Request.RuntimeId,
                Status: status,
                BytesReceived: 0,
                TotalBytes: handle.Request.ExpectedBytes,
                Progress: progress,
                InstallPath: handle.Request.InstallPath,
                ArtifactPath: null,
                ErrorMessage: null);

            handle.Publish(p);

            if (status is AiModelDownloadStatus.Completed or AiModelDownloadStatus.Failed or AiModelDownloadStatus.Canceled)
            {
                handle.Completion.TrySetResult(status);
            }
        }
    }
}
