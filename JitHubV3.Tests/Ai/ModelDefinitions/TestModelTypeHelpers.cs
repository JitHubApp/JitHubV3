namespace JitHubV3.Services.Ai.ModelDefinitions;

// Test-only stub: the production app uses source-generated ModelType + ModelTypeHelpers.
// The unit test project compiles linked production sources without running the generator.
internal enum ModelType
{
    WinAiApis,
    FoundryLocal,
    Onnx,
    OpenAI,
    Ollama,
    Lemonade,
}

internal sealed class ModelGroupDetails
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public bool IsApi { get; init; }
}

internal static class ModelTypeHelpers
{
    internal static Dictionary<ModelType, ModelGroupDetails> ModelGroupDetails { get; } = new()
    {
        { ModelType.WinAiApis, new ModelGroupDetails { Id = "winai", Name = "Windows AI APIs" } },
        { ModelType.FoundryLocal, new ModelGroupDetails { Id = "local-models", Name = "Foundry local" } },
        { ModelType.Onnx, new ModelGroupDetails { Id = "onnx", Name = "Custom models" } },
        { ModelType.OpenAI, new ModelGroupDetails { Id = "openai", Name = "OpenAI" } },
        { ModelType.Ollama, new ModelGroupDetails { Id = "ollama", Name = "Ollama" } },
        { ModelType.Lemonade, new ModelGroupDetails { Id = "lemonade", Name = "Lemonade" } },
    };

    internal static int GetModelOrder(ModelType modelType)
    {
        return modelType switch
        {
            ModelType.WinAiApis => 1,
            ModelType.FoundryLocal => 2,
            ModelType.Onnx => 3,
            ModelType.OpenAI => 4,
            ModelType.Ollama => 5,
            ModelType.Lemonade => 6,
            _ => int.MaxValue,
        };
    }
}
