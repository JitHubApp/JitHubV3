namespace JitHubV3.Services.Ai;

public interface IAiLocalModelDefinitionStore
{
    ValueTask<IReadOnlyList<AiLocalModelDefinition>> GetDefinitionsAsync(CancellationToken ct);

    ValueTask SetDefinitionsAsync(IReadOnlyList<AiLocalModelDefinition> definitions, CancellationToken ct);
}
