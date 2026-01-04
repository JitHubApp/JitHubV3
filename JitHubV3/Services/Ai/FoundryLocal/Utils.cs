// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;

namespace JitHubV3.Services.Ai.FoundryLocal;

internal static class Utils
{
    public static async Task<(string? Output, string? Error, int ExitCode)> RunFoundryWithArguments(string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "foundry";
            process.StartInfo.Arguments = arguments;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync().ConfigureAwait(false);

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            return (output, error, process.ExitCode);
        }
        catch
        {
            return (null, null, -1);
        }
    }
}
