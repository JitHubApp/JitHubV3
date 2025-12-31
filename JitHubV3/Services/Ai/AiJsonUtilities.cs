using System.Text.Json;

namespace JitHubV3.Services.Ai;

public static class AiJsonUtilities
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static bool TryDeserializeFirstJsonObject<T>(string? input, out T? value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = input.Trim();

        // Strip common Markdown fences if the model ignores instructions.
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = s.IndexOf('\n');
            if (firstNewline >= 0)
            {
                s = s.Substring(firstNewline + 1);
            }

            var fenceEnd = s.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0)
            {
                s = s.Substring(0, fenceEnd);
            }

            s = s.Trim();
        }

        var start = s.IndexOf('{');
        if (start < 0)
        {
            return false;
        }

        var end = s.LastIndexOf('}');
        if (end <= start)
        {
            return false;
        }

        var json = s.Substring(start, end - start + 1);

        try
        {
            value = JsonSerializer.Deserialize<T>(json, SerializerOptions);
            return value is not null;
        }
        catch
        {
            value = default;
            return false;
        }
    }
}
