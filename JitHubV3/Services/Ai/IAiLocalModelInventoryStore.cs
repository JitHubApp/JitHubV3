namespace JitHubV3.Services.Ai;

public interface IAiLocalModelInventoryStore
{
    ValueTask<IReadOnlyList<AiLocalModelInventoryEntry>> GetInventoryAsync(CancellationToken ct);

    ValueTask SetInventoryAsync(IReadOnlyList<AiLocalModelInventoryEntry> inventory, CancellationToken ct);
}
