using System.Diagnostics;
using System.Text.RegularExpressions;

namespace JitHubV3.Services.Ai.ExternalProviders;

public interface ILemonadeProbe
{
    ValueTask<bool> IsAvailableAsync(CancellationToken ct);
}

public sealed class LemonadeProbe : ILemonadeProbe
{
    private readonly TimeSpan _timeout;

    public LemonadeProbe(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(2);
    }

    public async ValueTask<bool> IsAvailableAsync(CancellationToken ct)
    {
        // Parity with AI Dev Gallery (gap report 2.4): availability-gated.
        // AI Dev Gallery checks for `lemonade-server` and reads `lemonade-server status`.
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var where = await TryRunAndCaptureAsync("where", "lemonade-server", ct).ConfigureAwait(false);
        if (where.ExitCode != 0 || string.IsNullOrWhiteSpace(where.StdOut))
        {
            return false;
        }

        var status = await TryRunAndCaptureAsync("lemonade-server", "status", ct).ConfigureAwait(false);
        if (status.ExitCode != 0 || string.IsNullOrWhiteSpace(status.StdOut))
        {
            return false;
        }

        // Extract port number from the end of the output (AI Dev Gallery approach).
        Match m = Regex.Match(status.StdOut.Trim(), @"\b(\d{1,5})\b$");
        return m.Success;
    }

    private async ValueTask<(int ExitCode, string? StdOut)> TryRunAndCaptureAsync(string exe, string args, CancellationToken ct)
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

            var stdout = await outputTask.ConfigureAwait(false);
            return (proc.ExitCode, stdout);
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
