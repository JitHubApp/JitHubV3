// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace JitHubV3.Services.Ai.FoundryLocal;

internal sealed record PromptTemplate
{
    [JsonPropertyName("assistant")]
    public string Assistant { get; init; } = default!;

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = default!;
}

internal sealed record Runtime
{
    [JsonPropertyName("deviceType")]
    public string DeviceType { get; init; } = default!;

    [JsonPropertyName("executionProvider")]
    public string ExecutionProvider { get; init; } = default!;
}

internal sealed record ModelSettings
{
    // The sample shows an empty array; keep it open-ended.
    [JsonPropertyName("parameters")]
    public List<JsonElement> Parameters { get; init; } = [];
}

internal sealed partial record FoundryCachedModel(string Name, string? Id);

internal sealed record FoundryDownloadResult(bool Success, string? ErrorMessage);

internal sealed record FoundryModelDownload(
    string Name,
    string Uri,
    string Path,
    string ProviderType,
    PromptTemplate PromptTemplate);

internal sealed record FoundryDownloadBody(FoundryModelDownload Model, bool IgnorePipeReport);

internal sealed record FoundryCatalogModel
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = default!;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = default!;

    [JsonPropertyName("providerType")]
    public string ProviderType { get; init; } = default!;

    [JsonPropertyName("uri")]
    public string Uri { get; init; } = default!;

    [JsonPropertyName("version")]
    public string Version { get; init; } = default!;

    [JsonPropertyName("modelType")]
    public string ModelType { get; init; } = default!;

    [JsonPropertyName("promptTemplate")]
    public PromptTemplate PromptTemplate { get; init; } = default!;

    [JsonPropertyName("publisher")]
    public string Publisher { get; init; } = default!;

    [JsonPropertyName("task")]
    public string Task { get; init; } = default!;

    [JsonPropertyName("runtime")]
    public Runtime Runtime { get; init; } = default!;

    [JsonPropertyName("fileSizeMb")]
    public long FileSizeMb { get; init; }

    [JsonPropertyName("modelSettings")]
    public ModelSettings ModelSettings { get; init; } = default!;

    [JsonPropertyName("alias")]
    public string Alias { get; init; } = default!;

    [JsonPropertyName("supportsToolCalling")]
    public bool SupportsToolCalling { get; init; }

    [JsonPropertyName("license")]
    public string License { get; init; } = default!;

    [JsonPropertyName("licenseDescription")]
    public string LicenseDescription { get; init; } = default!;

    [JsonPropertyName("parentModelUri")]
    public string ParentModelUri { get; init; } = default!;
}
