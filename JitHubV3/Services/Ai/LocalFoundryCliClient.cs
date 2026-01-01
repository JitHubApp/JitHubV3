using System.Diagnostics;
using System.Text.Json;

namespace JitHubV3.Services.Ai;

public sealed class LocalFoundryCliClient : ILocalFoundryClient
{
    private readonly TimeSpan _timeout;

    public LocalFoundryCliClient(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    public bool IsAvailable() => !string.IsNullOrWhiteSpace(TryFindFoundryExecutable());

    public async ValueTask<IReadOnlyList<LocalFoundryModel>> ListModelsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var exe = TryFindFoundryExecutable();
        if (string.IsNullOrWhiteSpace(exe))
        {
            return Array.Empty<LocalFoundryModel>();
        }

        // Best-effort: we don't assume a specific CLI shape.
        // Try a few common commands that might exist.
        var candidates = new[]
        {
            "models list --json",
            "list models --json",
            "models --json",
            "models list",
            "list models",
        };

        foreach (var args in candidates)
        {
            var stdout = await TryRunAsync(exe, args, ct).ConfigureAwait(false);
            var parsed = TryParseModels(stdout);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return Array.Empty<LocalFoundryModel>();
    }

    public async ValueTask<string?> TryBuildQueryPlanJsonAsync(string modelId, string input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var exe = TryFindFoundryExecutable();
        if (string.IsNullOrWhiteSpace(exe) || string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        // Best-effort: try a few common shapes.
        var safeInput = EscapeArg(input ?? string.Empty);
        var candidates = new[]
        {
            $"run --model {modelId} --prompt \"{safeInput}\" --output json",
            $"inference --model {modelId} --prompt \"{safeInput}\" --json",
            $"execute --model {modelId} --input \"{safeInput}\" --format json",
        };

        foreach (var args in candidates)
        {
            var stdout = await TryRunAsync(exe, args, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                return stdout;
            }
        }

        return null;
    }

    private async ValueTask<string?> TryRunAsync(string exePath, string args, CancellationToken ct)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo.FileName = exePath;
            proc.StartInfo.Arguments = args;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;

            proc.Start();

            var outputTask = proc.StandardOutput.ReadToEndAsync();
            var errorTask = proc.StandardError.ReadToEndAsync();

            var completed = await Task.WhenAny(Task.Delay(_timeout, ct), outputTask).ConfigureAwait(false);
            if (completed != outputTask)
            {
                try { proc.Kill(true); } catch { }
                return null;
            }

            var stdout = await outputTask.ConfigureAwait(false);
            var stderr = await errorTask.ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                return stdout;
            }

            if (!string.IsNullOrWhiteSpace(stderr) && stderr.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                return stderr;
            }

            return null;
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

    private static IReadOnlyList<LocalFoundryModel>? TryParseModels(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        // If it's JSON, try parse a few likely shapes.
        var trimmed = output.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    return ParseModelArray(root);
                }

                // { models: [...] }
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
                {
                    return ParseModelArray(models);
                }
            }
            catch
            {
                // fall through
            }
        }

        // Otherwise assume plain text list.
        var lines = trimmed
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Take(50)
            .Select(l => new LocalFoundryModel(ModelId: l, DisplayName: l))
            .ToArray();

        return lines.Length > 0 ? lines : null;
    }

    private static IReadOnlyList<LocalFoundryModel> ParseModelArray(JsonElement array)
    {
        var items = new List<LocalFoundryModel>(capacity: Math.Min(array.GetArrayLength(), 50));
        foreach (var el in array.EnumerateArray())
        {
            if (items.Count >= 50)
            {
                break;
            }

            if (el.ValueKind == JsonValueKind.String)
            {
                var id = el.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    items.Add(new LocalFoundryModel(ModelId: id, DisplayName: id));
                }

                continue;
            }

            if (el.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Try common keys: id, modelId, name
            string? modelId = null;
            if (el.TryGetProperty("modelId", out var modelIdProp)) modelId = modelIdProp.GetString();
            else if (el.TryGetProperty("id", out var idProp)) modelId = idProp.GetString();
            else if (el.TryGetProperty("name", out var nameProp)) modelId = nameProp.GetString();

            if (string.IsNullOrWhiteSpace(modelId))
            {
                continue;
            }

            string? displayName = null;
            if (el.TryGetProperty("displayName", out var dnProp)) displayName = dnProp.GetString();
            else if (el.TryGetProperty("name", out var nameProp2)) displayName = nameProp2.GetString();

            items.Add(new LocalFoundryModel(ModelId: modelId, DisplayName: displayName ?? modelId));
        }

        return items;
    }

    private static string EscapeArg(string s) => s.Replace("\"", "\\\"");

    private static string? TryFindFoundryExecutable()
    {
        var env = Environment.GetEnvironmentVariable("FOUNDRY_HOME");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var exe = Path.Combine(env, "foundry");
            if (OperatingSystem.IsWindows()) exe += ".exe";
            if (File.Exists(exe)) return exe;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(part, OperatingSystem.IsWindows() ? "foundry.exe" : "foundry");
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }
}
