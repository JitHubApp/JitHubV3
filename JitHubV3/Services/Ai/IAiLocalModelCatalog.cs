namespace JitHubV3.Services.Ai;

public interface IAiLocalModelCatalog
{
    ValueTask<IReadOnlyList<AiLocalModelCatalogItem>> GetCatalogAsync(CancellationToken ct);
}
