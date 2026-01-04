// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text.Json;

namespace JitHubV3.SourceGenerator;

internal static class Helpers
{
    internal static string EscapeUnicodeString(string unicodeString)
    {
        return JsonSerializer.Serialize(unicodeString, Models.SourceGenerationContext.Default.String);
    }
}
