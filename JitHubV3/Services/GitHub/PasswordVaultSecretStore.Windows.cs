#if WINDOWS
using JitHub.GitHub.Abstractions.Security;
using Windows.Security.Credentials;

namespace JitHubV3.Services.GitHub;

public sealed class PasswordVaultSecretStore : ISecretStore
{
    private const string ResourceName = "JitHubV3";

    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        var vault = new PasswordVault();
        try
        {
            var credential = vault.Retrieve(ResourceName, key);
            credential.RetrievePassword();
            return ValueTask.FromResult<string?>(credential.Password);
        }
        catch
        {
            return ValueTask.FromResult<string?>(null);
        }
    }

    public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var vault = new PasswordVault();
        vault.Add(new PasswordCredential(ResourceName, key, value));
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        var vault = new PasswordVault();
        try
        {
            var existing = vault.Retrieve(ResourceName, key);
            vault.Remove(existing);
        }
        catch
        {
            // ignore
        }

        return ValueTask.CompletedTask;
    }
}
#endif
