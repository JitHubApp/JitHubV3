// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace JitHubV3.Services.Ai.ExternalProviders.FoundryLocal;

public interface IFoundryLocalModelProvider
{
    string UrlPrefix { get; }

    /// <summary>
    /// Foundry Local base URL (e.g., http://127.0.0.1:PORT) when available.
    /// </summary>
    string Url { get; }

    Task<bool> IsAvailable(CancellationToken ct = default);

    Task<IReadOnlyList<FoundryLocalModelDetails>> GetModelsAsync(bool ignoreCached = false, CancellationToken ct = default);

    Task<IReadOnlyList<FoundryLocalModelDetails>> GetAllModelsInCatalogAsync(CancellationToken ct = default);

    Task<bool> DownloadModelAsync(FoundryLocalModelDetails modelDetails, IProgress<float>? progress, CancellationToken ct = default);

    Task<bool> DownloadModelByNameAsync(string modelName, IProgress<float>? progress, CancellationToken ct = default);
}

public sealed partial record FoundryLocalModelDetails(
    string Id,
    string Name,
    string Url,
    string Description,
    long? SizeBytes,
    string? License,
    object? ProviderModelDetails);
