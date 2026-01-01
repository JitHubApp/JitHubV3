using System.Text.Json;

namespace JitHubV3.Services.Ai;

public sealed class JsonFileAiEnablementStore : IAiEnablementStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _filePath;

    public JsonFileAiEnablementStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? GetDefaultPath()
            : filePath;
    }

    public async ValueTask<bool> GetIsEnabledAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            if (!File.Exists(_filePath))
            {
                // Preserve current behavior: AI is enabled unless explicitly turned off.
                return true;
            }

            var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return true;
            }

            var state = JsonSerializer.Deserialize<AiEnablementState>(json, SerializerOptions);
            return state?.IsEnabled ?? true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return true;
        }
    }

    public async ValueTask SetIsEnabledAsync(bool isEnabled, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(new AiEnablementState(isEnabled), SerializerOptions);
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
        return Path.Combine(baseDir, "JitHubV3", "ai", "enablement.json");
    }

    private sealed record AiEnablementState(bool IsEnabled);
}
