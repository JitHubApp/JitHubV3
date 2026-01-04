namespace JitHubV3.Services.Ai.ModelDefinitions;

internal sealed class ModelDetails
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<HardwareAccelerator> HardwareAccelerators { get; set; } = [];
    public bool? SupportedOnQualcomm { get; set; }
    public long? Size { get; set; }
    public string? ParameterSize { get; set; }
    public PromptTemplate? PromptTemplate { get; set; }
    public string Icon { get; set; } = string.Empty;
    public string? License { get; set; }
    public List<string>? FileFilters { get; set; }
    public List<AIToolkitAction>? AIToolkitActions { get; set; }
    public string? AIToolkitId { get; set; }
    public string? AIToolkitFinetuningId { get; set; }
    public List<int[]>? InputDimensions { get; set; }
    public List<int[]>? OutputDimensions { get; set; }
}

internal sealed class ModelFamilyDetails
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? DocsUrl { get; init; }
    public string ReadmeUrl { get; init; } = string.Empty;
}

internal sealed class ApiDefinitionDetails
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ReadmeUrl { get; init; } = string.Empty;
    public string License { get; init; } = string.Empty;
    public string SampleIdToShowInDocs { get; init; } = string.Empty;
    public string? Category { get; init; }
}

internal sealed class ModelGroupDetails
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public bool IsApi { get; init; }
}

internal sealed class PromptTemplate
{
    public string? System { get; init; }
    public string? User { get; init; }
    public string? Assistant { get; init; }
    public string[]? Stop { get; init; }
}

internal enum PromptTemplateType
{
    Phi3,
    Mistral,
    DeepSeekR1,
    Llama3,
    Qwen,
    Gemma
}

internal static class PromptTemplateHelpers
{
    internal static Dictionary<PromptTemplateType, PromptTemplate> PromptTemplates { get; } = new()
    {
        { PromptTemplateType.Phi3, new PromptTemplate { User = string.Empty, Stop = [] } },
        { PromptTemplateType.Mistral, new PromptTemplate { User = string.Empty, Stop = [] } },
        { PromptTemplateType.DeepSeekR1, new PromptTemplate { User = string.Empty, Stop = [] } },
        { PromptTemplateType.Llama3, new PromptTemplate { User = string.Empty, Stop = [] } },
        { PromptTemplateType.Qwen, new PromptTemplate { User = string.Empty, Stop = [] } },
        { PromptTemplateType.Gemma, new PromptTemplate { User = string.Empty, Stop = [] } },
    };
}

internal enum HardwareAccelerator
{
    ACI,
    CPU,
    DML,
    QNN,
    WCRAPI,
    OLLAMA,
    OPENAI,
    FOUNDRYLOCAL,
    LEMONADE,
    NPU,
    GPU,
    VitisAI,
    OpenVINO,
    NvTensorRT
}

internal enum AIToolkitAction
{
    FineTuning,
    PromptBuilder,
    Playground,
    Conversion
}
