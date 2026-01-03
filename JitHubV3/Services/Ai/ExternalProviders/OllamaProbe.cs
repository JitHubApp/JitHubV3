using System.Diagnostics;

namespace JitHubV3.Services.Ai.ExternalProviders;

public interface IOllamaProbe
{
    ValueTask<bool> IsAvailableAsync(CancellationToken ct);
}

public sealed class OllamaProbe : IOllamaProbe
{
    private readonly TimeSpan _timeout;

    public OllamaProbe(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(2);
    }

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct)
    {
        // Parity with AI Dev Gallery (gap report 2.4): availability-gated.
        // AI Dev Gallery uses `ollama list`. We'll do the same on Windows.
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var result = await TryRunAsync("ollama", "list", ct).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private async ValueTask<(int ExitCode, string? StdOut)> TryRunAsync(string exe, string args, CancellationToken ct)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo.FileName = exe;
            proc.StartInfo.Arguments = args;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;

            proc.Start();

            var outputTask = proc.StandardOutput.ReadToEndAsync();
            var completed = await Task.WhenAny(Task.Delay(_timeout, ct), outputTask).ConfigureAwait(false);
            if (completed != outputTask)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return (-1, null);
            }

            await outputTask.ConfigureAwait(false);
            return (proc.ExitCode, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (-1, null);
        }
    }
}
