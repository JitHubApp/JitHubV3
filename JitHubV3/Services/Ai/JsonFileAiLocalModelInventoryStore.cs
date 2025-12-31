using System.Text.Json;

namespace JitHubV3.Services.Ai;

public sealed class JsonFileAiLocalModelInventoryStore : IAiLocalModelInventoryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _filePath;

    public JsonFileAiLocalModelInventoryStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? GetDefaultPath()
            : filePath;
    }

    public async ValueTask<IReadOnlyList<AiLocalModelInventoryEntry>> GetInventoryAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            if (!File.Exists(_filePath))
            {
                return Array.Empty<AiLocalModelInventoryEntry>();
            }

            var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<AiLocalModelInventoryEntry>();
            }

            var items = JsonSerializer.Deserialize<List<AiLocalModelInventoryEntry>>(json, SerializerOptions);
            if (items is null)
            {
                return Array.Empty<AiLocalModelInventoryEntry>();
            }

            // Basic sanitation to avoid propagating corrupted entries.
            return items
                .Where(i => !string.IsNullOrWhiteSpace(i.ModelId)
                            && !string.IsNullOrWhiteSpace(i.RuntimeId)
                            && !string.IsNullOrWhiteSpace(i.InstallPath))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Corruption / IO failure should not crash the app.
            return Array.Empty<AiLocalModelInventoryEntry>();
        }
    }

    public async ValueTask SetInventoryAsync(IReadOnlyList<AiLocalModelInventoryEntry> inventory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(inventory ?? Array.Empty<AiLocalModelInventoryEntry>(), SerializerOptions);
            await File.WriteAllTextAsync(_filePath, json, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // ignore
        }
    }

    private static string GetDefaultPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "JitHubV3", "ai", "local-models.json");
    }
}
