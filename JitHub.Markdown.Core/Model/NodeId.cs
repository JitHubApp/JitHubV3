using System.Diagnostics;

namespace JitHub.Markdown;

[DebuggerDisplay("{Value}")]
public readonly record struct NodeId(ulong Value)
{
    public override string ToString() => Value.ToString("X16");

    public static NodeId Create(NodeKind kind, SourceSpan span, int ordinal, NodeId parent)
    {
        // Deterministic, stable id (no random GUIDs) based on structure + source span.
        // This is intentionally simple for Phase 1 and can be evolved later.
        return new NodeId(Hash64(
            (ulong)kind,
            (ulong)span.Start,
            (ulong)span.EndExclusive,
            unchecked((ulong)ordinal),
            parent.Value));
    }

    private static ulong Hash64(ulong a, ulong b, ulong c, ulong d, ulong e)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;

        var h = offset;
        h = (h ^ a) * prime;
        h = (h ^ b) * prime;
        h = (h ^ c) * prime;
        h = (h ^ d) * prime;
        h = (h ^ e) * prime;
        return h;
    }
}
