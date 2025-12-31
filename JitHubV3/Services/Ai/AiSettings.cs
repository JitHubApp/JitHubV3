using Microsoft.Extensions.Configuration;

namespace JitHubV3.Services.Ai;

public sealed record AiSettings
{
    /// <summary>
    /// When false, the app must behave deterministically and never invoke AI.
    /// </summary>
    public bool Enabled { get; init; }

    public static AiSettings FromConfiguration(IConfiguration config)
    {
        var section = config.GetSection("Ai");
        return new AiSettings
        {
            Enabled = bool.TryParse(section["Enabled"], out var enabled) && enabled,
        };
    }
}
