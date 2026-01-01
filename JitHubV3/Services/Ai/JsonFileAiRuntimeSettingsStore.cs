using System.Text.Json;

namespace JitHubV3.Services.Ai;

public sealed class JsonFileAiRuntimeSettingsStore : IAiRuntimeSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _filePath;

    public JsonFileAiRuntimeSettingsStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? GetDefaultPath()
            : filePath;
    }

    public async ValueTask<AiRuntimeSettings> GetAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            if (!File.Exists(_filePath))
            {
                return new AiRuntimeSettings();
            }

            var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AiRuntimeSettings();
            }

            return JsonSerializer.Deserialize<AiRuntimeSettings>(json, SerializerOptions) ?? new AiRuntimeSettings();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new AiRuntimeSettings();
        }
    }

    public async ValueTask SetAsync(AiRuntimeSettings settings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings ?? new AiRuntimeSettings(), SerializerOptions);
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
        return Path.Combine(baseDir, "JitHubV3", "ai", "runtime-settings.json");
    }
}
