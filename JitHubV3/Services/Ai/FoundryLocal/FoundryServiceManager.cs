// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace JitHubV3.Services.Ai.FoundryLocal;

internal sealed class FoundryServiceManager
{
    public static FoundryServiceManager? TryCreate()
    {
        if (IsAvailable())
        {
            return new FoundryServiceManager();
        }

        return null;
    }

    private static bool IsAvailable()
    {
        // AI Dev Gallery checks with: `where foundry` (Windows).
        // Per Phase 1.3, use `which foundry` on macOS/Linux.
        var locatorExe = OperatingSystem.IsWindows() ? "where" : "which";

        using var process = new Process();
        process.StartInfo.FileName = locatorExe;
        process.StartInfo.Arguments = "foundry";
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        process.WaitForExit();

        return process.ExitCode == 0;
    }

    private static string? GetUrl(string output)
    {
        var match = Regex.Match(output, @"https?:\/\/[^\/]+:\d+");
        if (match.Success)
        {
            return match.Value;
        }

        return null;
    }

    public async Task<string?> GetServiceUrl()
    {
        var status = await Utils.RunFoundryWithArguments("service status").ConfigureAwait(false);

        if (status.ExitCode != 0 || string.IsNullOrWhiteSpace(status.Output))
        {
            return null;
        }

        return GetUrl(status.Output);
    }

    public async Task<bool> IsRunning()
    {
        var url = await GetServiceUrl().ConfigureAwait(false);
        return url != null;
    }

    public async Task<bool> StartService()
    {
        if (await IsRunning().ConfigureAwait(false))
        {
            return true;
        }

        var status = await Utils.RunFoundryWithArguments("service start").ConfigureAwait(false);
        if (status.ExitCode != 0 || string.IsNullOrWhiteSpace(status.Output))
        {
            return false;
        }

        return GetUrl(status.Output) != null;
    }
}
