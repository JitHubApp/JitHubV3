using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using JitHub.GitHub.Abstractions.Refresh;
using JitHubV3.Services.Ai;

namespace JitHubV3.Presentation;

public sealed class FoundrySetupDashboardCardProvider : IStagedDashboardCardProvider
{
    private const string SetupUrl = "https://learn.microsoft.com/en-us/azure/ai-foundry/foundry-local/get-started?view=foundry-classic";
    private const string ExampleModelId = "qwen2.5-0.5b";

    private readonly IAiModelStore _modelStore;
    private readonly ILocalFoundryClient _foundry;

    public FoundrySetupDashboardCardProvider(IAiModelStore modelStore, ILocalFoundryClient foundry)
    {
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _foundry = foundry ?? throw new ArgumentNullException(nameof(foundry));
    }

    public string ProviderId => "foundry-setup";

    // Above compose cards; only shows when Local Foundry is selected but unavailable.
    public int Priority => 4;

    public DashboardCardProviderTier Tier => DashboardCardProviderTier.Local;

    public async Task<IReadOnlyList<DashboardCardModel>> GetCardsAsync(
        DashboardContext context,
        RefreshMode refresh,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var selected = await _modelStore.GetSelectionAsync(ct).ConfigureAwait(false);
        if (selected is null || !string.Equals(selected.RuntimeId, "local-foundry", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<DashboardCardModel>();
        }

        var openSetup = new AsyncRelayCommand(OpenSetupAsync);

        if (!_foundry.IsAvailable())
        {
            return new[]
            {
                new DashboardCardModel(
                    CardId: DashboardCardId.FoundrySetup,
                    Kind: DashboardCardKind.FoundrySetup,
                    Title: "Set up Foundry Local",
                    Subtitle: "Local Foundry is selected but not detected",
                    Summary: "Install Foundry Local (Windows: winget install Microsoft.FoundryLocal). Ensure the 'foundry' command is available on PATH, then try again.",
                    Importance: 90,
                    Actions: new[] { new DashboardCardActionModel("Open setup guide", openSetup) },
                    TintVariant: 0)
            };
        }

        // Foundry is installed; ensure the user has at least one model available.
        // This call is best-effort and timeboxed in the CLI client.
        var models = await _foundry.ListModelsAsync(ct).ConfigureAwait(false);
        if (models.Count == 0)
        {
            return new[]
            {
                new DashboardCardModel(
                    CardId: DashboardCardId.FoundrySetup,
                    Kind: DashboardCardKind.FoundrySetup,
                    Title: "Download a Foundry Local model",
                    Subtitle: "Foundry Local is installed, but no models are available",
                    Summary: $"Run 'foundry model run {ExampleModelId}' once to download a model and start the local service. Then re-select Local Foundry and try again.",
                    Importance: 90,
                    Actions: new[] { new DashboardCardActionModel("Open setup guide", openSetup) },
                    TintVariant: 0)
            };
        }

        return Array.Empty<DashboardCardModel>();
    }

    private static async Task OpenSetupAsync()
    {
        var uri = new Uri(SetupUrl);

#if WINDOWS || __WINDOWS__
    await Windows.System.Launcher.LaunchUriAsync(uri);
#else
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.ToString(),
            UseShellExecute = true,
        });

        await Task.CompletedTask;
#endif
    }
}
