// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Diagnostics;

namespace JitHubV3.Services.Ai.FoundryLocal;

internal sealed class FoundryClient
{
    public static async Task<FoundryClient?> CreateAsync(HttpClient httpClient)
    {
        var serviceManager = FoundryServiceManager.TryCreate();
        if (serviceManager == null)
        {
            return null;
        }

        if (!await serviceManager.IsRunning().ConfigureAwait(false))
        {
            if (!await serviceManager.StartService().ConfigureAwait(false))
            {
                return null;
            }
        }

        var serviceUrl = await serviceManager.GetServiceUrl().ConfigureAwait(false);

        if (string.IsNullOrEmpty(serviceUrl))
        {
            return null;
        }

        return new FoundryClient(serviceUrl, serviceManager, httpClient);
    }

    public FoundryServiceManager ServiceManager { get; }

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly List<FoundryCatalogModel> _catalogModels = [];

    private FoundryClient(string baseUrl, FoundryServiceManager serviceManager, HttpClient httpClient)
    {
        ServiceManager = serviceManager;
        _baseUrl = baseUrl;
        _httpClient = httpClient;
    }

    public async Task<List<FoundryCatalogModel>> ListCatalogModels()
    {
        if (_catalogModels.Count > 0)
        {
            return _catalogModels;
        }

        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/foundry/list").ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var models = await JsonSerializer.DeserializeAsync(
                response.Content.ReadAsStream(),
                FoundryJsonContext.Default.ListFoundryCatalogModel).ConfigureAwait(false);

            if (models != null && models.Count > 0)
            {
                models.ForEach(_catalogModels.Add);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FoundryLocal] Failed to list catalog models from '{_baseUrl}/foundry/list': {ex}");
        }

        return _catalogModels;
    }

    public async Task<List<FoundryCachedModel>> ListCachedModels()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/openai/models").ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var catalogModels = await ListCatalogModels().ConfigureAwait(false);

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var modelIds = content
                .Trim('[', ']')
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim('"'));

            List<FoundryCachedModel> models = [];

            foreach (var id in modelIds)
            {
                var model = catalogModels.FirstOrDefault(m => m.Name == id);
                if (model != null)
                {
                    models.Add(new FoundryCachedModel(id, model.Alias));
                }
                else
                {
                    models.Add(new FoundryCachedModel(id, null));
                }
            }

            return models;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FoundryLocal] Failed to list cached models from '{_baseUrl}/openai/models': {ex}");
            return [];
        }
    }

    public async Task<FoundryDownloadResult> DownloadModel(FoundryCatalogModel model, IProgress<float>? progress, CancellationToken cancellationToken = default)
    {
        var models = await ListCachedModels().ConfigureAwait(false);

        if (models.Any(m => m.Name == model.Name))
        {
            return new FoundryDownloadResult(true, "Model already downloaded");
        }

        return await Task.Run(async () =>
        {
            try
            {
                var uploadBody = new FoundryDownloadBody(
                    new FoundryModelDownload(
                        Name: model.Name,
                        Uri: model.Uri,
                        Path: await GetModelPath(model.Uri).ConfigureAwait(false), // temporary
                        ProviderType: model.ProviderType,
                        PromptTemplate: model.PromptTemplate),
                    IgnorePipeReport: true);

                var body = JsonSerializer.Serialize(
                    uploadBody,
                    FoundryJsonContext.Default.FoundryDownloadBody);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/openai/download")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream);

                string? finalJson = null;

                while (!cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    line = line.Trim();

                    // Final response starts with '{'
                    if (finalJson != null || line.StartsWith('{'))
                    {
                        finalJson += line;
                        continue;
                    }

                    var match = Regex.Match(line, @"\d+(\.\d+)?%");
                    if (match.Success)
                    {
                        var percentage = match.Value;
                        if (float.TryParse(percentage.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var progressValue))
                        {
                            progress?.Report(progressValue / 100);
                        }
                    }
                }

                // Parse closing JSON; default if malformed
                var result = finalJson is not null
                    ? JsonSerializer.Deserialize(finalJson, FoundryJsonContext.Default.FoundryDownloadResult)!
                    : new FoundryDownloadResult(false, "Missing final result from server.");

                return result;
            }
            catch (Exception e)
            {
                return new FoundryDownloadResult(false, e.Message);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    // this is a temporary function to get the model path from the blob storage
    //  it will be removed once the tag is available in the list response
    private async Task<string> GetModelPath(string assetId)
    {
        var registryUri = $"https://eastus.api.azureml.ms/modelregistry/v1.0/registry/models/nonazureaccount?assetId={Uri.EscapeDataString(assetId)}";

        using var resp = await _httpClient.GetAsync(registryUri).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var jsonStream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var jsonRoot = await JsonDocument.ParseAsync(jsonStream).ConfigureAwait(false);
        var blobSasUri = jsonRoot.RootElement.GetProperty("blobSasUri").GetString()!;

        var uriBuilder = new UriBuilder(blobSasUri);
        var existingQuery = string.IsNullOrWhiteSpace(uriBuilder.Query)
            ? string.Empty
            : uriBuilder.Query.TrimStart('?') + "&";

        uriBuilder.Query = existingQuery + "restype=container&comp=list&delimiter=/";

        var listXml = await _httpClient.GetStringAsync(uriBuilder.Uri).ConfigureAwait(false);

        var match = Regex.Match(listXml, @"<Name>(.*?)\/</Name>");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }
}
