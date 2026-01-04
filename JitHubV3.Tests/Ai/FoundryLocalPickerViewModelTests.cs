using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using JitHubV3.Presentation.Controls.ModelPicker;
using JitHubV3.Services.Ai;
using JitHubV3.Services.Ai.ExternalProviders.FoundryLocal;
using JitHubV3.Services.Ai.FoundryLocal;

namespace JitHubV3.Tests.Ai;

public sealed class FoundryLocalPickerViewModelTests
{
    private sealed class FakeFoundryProvider : IFoundryLocalModelProvider
    {
        public string UrlPrefix => "fl://";

        public string Url { get; set; } = "http://127.0.0.1:1234";

        public bool Available { get; set; } = true;

        public IReadOnlyList<FoundryLocalModelDetails> Downloaded { get; set; } = Array.Empty<FoundryLocalModelDetails>();

        public IReadOnlyList<FoundryLocalModelDetails> Catalog { get; set; } = Array.Empty<FoundryLocalModelDetails>();

        public Task<bool> IsAvailable(CancellationToken ct = default) => Task.FromResult(Available);

        public Task<IReadOnlyList<FoundryLocalModelDetails>> GetModelsAsync(bool ignoreCached = false, CancellationToken ct = default)
            => Task.FromResult(Downloaded);

        public Task<IReadOnlyList<FoundryLocalModelDetails>> GetAllModelsInCatalogAsync(CancellationToken ct = default)
            => Task.FromResult(Catalog);

        public Task<bool> DownloadModelAsync(FoundryLocalModelDetails modelDetails, IProgress<float>? progress, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> DownloadModelByNameAsync(string modelName, IProgress<float>? progress, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class FakeDownloads : IAiModelDownloadQueue
    {
        public event Action? DownloadsChanged;

        public AiModelDownloadRequest? LastRequest { get; private set; }

        public AiModelDownloadHandle Enqueue(AiModelDownloadRequest request)
        {
            LastRequest = request;

            var cts = new CancellationTokenSource();
            var handle = new AiModelDownloadHandle(Guid.NewGuid(), request, cts);

            // Immediately complete.
            handle.Publish(handle.Latest with { Status = AiModelDownloadStatus.Completed, Progress = 1.0 });
            handle.Completion.TrySetResult(AiModelDownloadStatus.Completed);

            DownloadsChanged?.Invoke();
            return handle;
        }

        public IReadOnlyList<AiModelDownloadHandle> GetActiveDownloads() => Array.Empty<AiModelDownloadHandle>();

        public AiModelDownloadHandle? TryGet(Guid downloadId) => null;

        public bool Cancel(Guid downloadId) => false;
    }

    private sealed class FakeModelStore : IAiModelStore
    {
        private AiModelSelection? _selection;

        public ValueTask<AiModelSelection?> GetSelectionAsync(CancellationToken ct)
            => ValueTask.FromResult(_selection);

        public ValueTask SetSelectionAsync(AiModelSelection? selection, CancellationToken ct)
        {
            _selection = selection;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeShell : ILocalModelShellActions
    {
        public string? LastCopiedText { get; private set; }

        public Task LaunchUriAsync(Uri uri) => Task.CompletedTask;

        public Task CopyTextAsync(string text)
        {
            LastCopiedText = text;
            return Task.CompletedTask;
        }

        public Task OpenFolderAsync(string folderPath) => Task.CompletedTask;
    }

    [Test]
    public async Task RefreshAsync_WhenNotAvailable_SetsNotAvailableState()
    {
        var provider = new FakeFoundryProvider { Available = false };
        var vm = new FoundryLocalPickerViewModel(provider, new FakeDownloads(), new FakeModelStore(), new FakeShell());

        await vm.RefreshAsync(CancellationToken.None);

        vm.IsNotAvailable.Should().BeTrue();
        vm.IsLoading.Should().BeFalse();
        vm.FoundryLocalUrl.Should().BeEmpty();
        vm.AvailableModels.Should().BeEmpty();
        vm.CatalogModels.Should().BeEmpty();
    }

    [Test]
    public async Task RefreshAsync_WhenAvailable_GroupsByAlias_AndExcludesDownloadedFromCatalogDownloadList()
    {
        var downloaded = new[]
        {
            new FoundryLocalModelDetails(
                Id: "fl-phi-3",
                Name: "phi-3",
                Url: "fl://phi-3",
                Description: "phi",
                SizeBytes: 123,
                License: "mit",
                ProviderModelDetails: new FoundryCatalogModel { Name = "phi-3", Alias = "Phi", FileSizeMb = 1, License = "mit", Uri = "asset" })
        };

        var catalog = new[]
        {
            new FoundryLocalModelDetails(
                Id: "fl-phi-3",
                Name: "phi-3",
                Url: "fl://phi-3",
                Description: "phi",
                SizeBytes: 123,
                License: "mit",
                ProviderModelDetails: new FoundryCatalogModel { Name = "phi-3", Alias = "Phi", FileSizeMb = 1, License = "mit", Uri = "asset" }),
            new FoundryLocalModelDetails(
                Id: "fl-phi-4",
                Name: "phi-4",
                Url: "fl://phi-4",
                Description: "phi",
                SizeBytes: 456,
                License: "mit",
                ProviderModelDetails: new FoundryCatalogModel { Name = "phi-4", Alias = "Phi", FileSizeMb = 1, License = "mit", Uri = "asset" }),
        };

        var provider = new FakeFoundryProvider
        {
            Available = true,
            Url = "http://127.0.0.1:9999",
            Downloaded = downloaded,
            Catalog = catalog,
        };

        var vm = new FoundryLocalPickerViewModel(provider, new FakeDownloads(), new FakeModelStore(), new FakeShell());

        await vm.RefreshAsync(CancellationToken.None);

        vm.IsNotAvailable.Should().BeFalse();
        vm.IsLoading.Should().BeFalse();
        vm.FoundryLocalUrl.Should().Be("http://127.0.0.1:9999");

        vm.AvailableModels.Select(m => m.Name).Should().Equal(["phi-3"]);

        vm.CatalogModels.Should().HaveCount(1);
        var group = vm.CatalogModels.Single();
        group.Alias.Should().Be("Phi");

        // Download list should only include non-downloaded items.
        group.Models.Select(m => m.ModelName).Should().Equal(["phi-4"]);
    }

    [Test]
    public async Task DownloadModelCommand_EnqueuesFoundryLocalUri()
    {
        var provider = new FakeFoundryProvider
        {
            Available = true,
            Downloaded = Array.Empty<FoundryLocalModelDetails>(),
            Catalog = new[]
            {
                new FoundryLocalModelDetails(
                    Id: "fl-phi-4",
                    Name: "phi-4",
                    Url: "fl://phi-4",
                    Description: "phi",
                    SizeBytes: 456,
                    License: "mit",
                    ProviderModelDetails: new FoundryCatalogModel { Name = "phi-4", Alias = "Phi", FileSizeMb = 1, License = "mit", Uri = "asset" }),
            }
        };

        var downloads = new FakeDownloads();
        var vm = new FoundryLocalPickerViewModel(provider, downloads, new FakeModelStore(), new FakeShell());

        await vm.RefreshAsync(CancellationToken.None);

        var group = vm.CatalogModels.Single();
        var downloadable = group.Models.Single();

        await vm.DownloadModelCommand.ExecuteAsync(downloadable);

        downloads.LastRequest.Should().NotBeNull();
        downloads.LastRequest!.RuntimeId.Should().Be("local-foundry");
        downloads.LastRequest!.SourceUri.Should().Be(new Uri("fl://phi-4"));
        downloads.LastRequest!.ModelId.Should().Be("phi-4");
    }
}
