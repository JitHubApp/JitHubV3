using Microsoft.Extensions.Configuration;

namespace JitHubV3.Services.Ai;

public static class AiLocalModelDefinitionsConfiguration
{
    public static IReadOnlyList<AiLocalModelDefinition> FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Ai:LocalModels");
        if (!section.Exists())
        {
            return Array.Empty<AiLocalModelDefinition>();
        }

        var items = new List<AiLocalModelDefinition>();
        foreach (var child in section.GetChildren())
        {
            var modelId = child["ModelId"];
            var runtimeId = child["RuntimeId"];

            if (string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(runtimeId))
            {
                continue;
            }

            var expectedBytes = TryReadLong(child["ExpectedBytes"]);
            var downloadUri = child["DownloadUri"];
            var expectedSha256 = child["ExpectedSha256"];

            items.Add(new AiLocalModelDefinition(
                ModelId: modelId,
                DisplayName: child["DisplayName"],
                RuntimeId: runtimeId,
                DefaultInstallFolderName: child["DefaultInstallFolderName"],
                DownloadUri: downloadUri,
                ArtifactFileName: child["ArtifactFileName"],
                ExpectedBytes: expectedBytes,
                ExpectedSha256: expectedSha256));
        }

        return items;
    }

    private static long? TryReadLong(string? s)
        => long.TryParse(s, out var v) ? v : null;
}
