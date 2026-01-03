using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace JitHubV3.Services.Ai;

public sealed class LocalModelShellActions : ILocalModelShellActions
{
    public async Task LaunchUriAsync(Uri uri)
    {
        if (uri is null)
        {
            return;
        }

        try
        {
            _ = await Launcher.LaunchUriAsync(uri);
        }
        catch
        {
            // ignore
        }
    }

    public Task CopyTextAsync(string text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Task.CompletedTask;
            }

            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch
        {
            // ignore
        }

        return Task.CompletedTask;
    }

    public Task OpenFolderAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return Task.CompletedTask;
        }

        try
        {
            var normalized = folderPath.Replace('\\', '/');
            var uri = new Uri($"file:///{normalized}");
            return LaunchUriAsync(uri);
        }
        catch
        {
            // ignore
        }

        return Task.CompletedTask;
    }
}
