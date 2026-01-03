namespace JitHubV3.Services.Ai;

public interface ILocalModelShellActions
{
    Task LaunchUriAsync(Uri uri);

    Task CopyTextAsync(string text);

    Task OpenFolderAsync(string folderPath);
}
