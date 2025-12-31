using System.Text.Json;

namespace JitHubV3.Services.Ai;

public sealed class JsonFileAiModelStore : IAiModelStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _filePath;

    public JsonFileAiModelStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? GetDefaultPath()
            : filePath;
    }

    public async ValueTask<AiModelSelection?> GetSelectionAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<AiModelSelection>(json, SerializerOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask SetSelectionAsync(AiModelSelection? selection, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (selection is null)
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }

                return;
            }

            var json = JsonSerializer.Serialize(selection, SerializerOptions);
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
        return Path.Combine(baseDir, "JitHubV3", "ai", "selection.json");
    }
}
