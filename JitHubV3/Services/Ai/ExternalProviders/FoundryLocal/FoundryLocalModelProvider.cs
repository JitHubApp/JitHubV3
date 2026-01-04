// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using JitHubV3.Services.Ai.FoundryLocal;


namespace JitHubV3.Services.Ai.ExternalProviders.FoundryLocal;

public sealed class FoundryLocalModelProvider : IFoundryLocalModelProvider
{
    private IReadOnlyList<FoundryLocalModelDetails>? _downloadedModels;
    private IReadOnlyList<FoundryLocalModelDetails>? _catalogModels;
    private FoundryClient? _foundryClient;
    private string? _url;

    private readonly HttpClient _http;

    public FoundryLocalModelProvider(HttpClient http)
    {
        _http = http;
    }

    public string UrlPrefix => "fl://";

    public string Url => _url ?? string.Empty;

    public async Task<IReadOnlyList<FoundryLocalModelDetails>> GetModelsAsync(bool ignoreCached = false, CancellationToken ct = default)
    {
        if (ignoreCached)
        {
            Reset();
        }

        await InitializeAsync(ct).ConfigureAwait(false);

        return _downloadedModels ?? Array.Empty<FoundryLocalModelDetails>();
    }

    public async Task<IReadOnlyList<FoundryLocalModelDetails>> GetAllModelsInCatalogAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        return _catalogModels ?? Array.Empty<FoundryLocalModelDetails>();
    }

    public async Task<bool> DownloadModelAsync(FoundryLocalModelDetails modelDetails, IProgress<float>? progress, CancellationToken ct = default)
    {
        if (_foundryClient == null)
        {
            return false;
        }

        if (modelDetails.ProviderModelDetails is not FoundryCatalogModel model)
        {
            return false;
        }

        var result = await _foundryClient.DownloadModel(model, progress, ct).ConfigureAwait(false);

        // Refresh cached view.
        Reset();
        await InitializeAsync(ct).ConfigureAwait(false);

        return result.Success;
    }

    public async Task<bool> DownloadModelByNameAsync(string modelName, IProgress<float>? progress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return false;
        }

        await InitializeAsync(ct).ConfigureAwait(false);

        if (_foundryClient is null)
        {
            return false;
        }

        var catalogModels = await _foundryClient.ListCatalogModels().ConfigureAwait(false);
        var match = catalogModels.FirstOrDefault(m => string.Equals(m.Name, modelName, StringComparison.Ordinal));
        if (match is null)
        {
            return false;
        }

        var result = await _foundryClient.DownloadModel(match, progress, ct).ConfigureAwait(false);

        Reset();
        await InitializeAsync(ct).ConfigureAwait(false);

        return result.Success;
    }

    public async Task<bool> IsAvailable(CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        return _foundryClient != null;
    }

    private void Reset()
    {
        _downloadedModels = null;
        _catalogModels = null;
        // Keep _foundryClient so we don't restart service more than needed.
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (_foundryClient != null && _downloadedModels is not null && _downloadedModels.Count > 0)
        {
            return;
        }

        _foundryClient ??= await FoundryClient.CreateAsync(_http).ConfigureAwait(false);

        if (_foundryClient == null)
        {
            return;
        }

        _url ??= await _foundryClient.ServiceManager.GetServiceUrl().ConfigureAwait(false);

        if (_catalogModels is null || _catalogModels.Count == 0)
        {
            var catalog = await _foundryClient.ListCatalogModels().ConfigureAwait(false);
            _catalogModels = catalog.Select(ToModelDetails).ToArray();
        }

        var cachedModels = await _foundryClient.ListCachedModels().ConfigureAwait(false);

        var downloadedModels = new List<FoundryLocalModelDetails>();

        // Map catalog entries that are present in cache.
        foreach (var model in _catalogModels)
        {
            var cachedModel = cachedModels.FirstOrDefault(m => m.Name == model.Name);

            if (cachedModel != default)
            {
                downloadedModels.Add(model with
                {
                    Id = $"{UrlPrefix}{cachedModel.Id}",
                    Url = $"{UrlPrefix}{model.Name}",
                });

                cachedModels.Remove(cachedModel);
            }
        }

        // Add any remaining cached items not in catalog.
        foreach (var model in cachedModels)
        {
            downloadedModels.Add(new FoundryLocalModelDetails(
                Id: $"fl-{model.Name}",
                Name: model.Name,
                Url: $"{UrlPrefix}{model.Name}",
                Description: $"{model.Name} running locally with Foundry Local",
                SizeBytes: null,
                License: null,
                ProviderModelDetails: model));
        }

        _downloadedModels = downloadedModels;
    }

    private static FoundryLocalModelDetails ToModelDetails(FoundryCatalogModel model)
    {
        return new FoundryLocalModelDetails(
            Id: $"fl-{model.Name}",
            Name: model.Name,
            Url: $"fl://{model.Name}",
            Description: $"{model.Alias} running locally with Foundry Local",
            SizeBytes: model.FileSizeMb * 1024L * 1024L,
            License: model.License?.ToLowerInvariant(),
            ProviderModelDetails: model);
    }
}
