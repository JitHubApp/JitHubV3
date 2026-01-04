// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace JitHubV3.Services.Ai.FoundryLocal;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
[JsonSerializable(typeof(FoundryCatalogModel))]
[JsonSerializable(typeof(List<FoundryCatalogModel>))]
[JsonSerializable(typeof(FoundryDownloadResult))]
[JsonSerializable(typeof(FoundryDownloadBody))]
internal sealed partial class FoundryJsonContext : JsonSerializerContext
{
}
