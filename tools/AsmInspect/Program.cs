using System.Reflection;
using System.Runtime.Loader;

static Assembly LoadFromDir(string path)
{
    var fullPath = Path.GetFullPath(path);
    var dir = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;

    AssemblyLoadContext.Default.Resolving += (context, name) =>
    {
        var candidate = Path.Combine(dir, name.Name + ".dll");
        return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
    };

    return AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
}

var asmPath = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "JitHubV3", "bin", "Debug", "net10.0-desktop", "JitHubV3.dll"));

Console.WriteLine($"Assembly: {asmPath}");

var asm = LoadFromDir(asmPath);

var expected = new[]
{
    "JitHubV3.Services.Ai.ModelDefinitions.ModelType",
    "JitHubV3.Services.Ai.ModelDefinitions.ModelTypeHelpers",
};

Type? helpersType = null;
foreach (var t in expected)
{
    var found = asm.GetType(t, throwOnError: false);
    Console.WriteLine($"{t}: {(found is null ? "NOT FOUND" : "FOUND")}");

    if (t.EndsWith("ModelTypeHelpers", StringComparison.Ordinal))
    {
        helpersType = found;
    }
}

if (helpersType is not null)
{
    Console.WriteLine("\nModelTypeHelpers surface check:");
    var members = new[]
    {
        "ModelDetails",
        "ModelFamilyDetails",
        "ApiDefinitionDetails",
        "ModelGroupDetails",
        "ParentMapping",
        "GetModelOrder",
    };

    foreach (var m in members)
    {
        var prop = helpersType.GetProperty(m, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var method = helpersType.GetMethod(m, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Console.WriteLine($"- {m}: {(prop is not null || method is not null ? "PRESENT" : "MISSING")}");
    }

    static int? TryGetCount(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var countProp = value.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        if (countProp?.GetValue(value) is int count)
        {
            return count;
        }

        return null;
    }

    foreach (var p in new[] { "ModelDetails", "ModelFamilyDetails", "ApiDefinitionDetails", "ModelGroupDetails", "ParentMapping" })
    {
        var prop = helpersType.GetProperty(p, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var value = prop?.GetValue(null);
        var count = TryGetCount(value);
        Console.WriteLine($"  {p}.Count = {(count is null ? "(unknown)" : count.ToString())}");
    }
}

Console.WriteLine("\nTypes containing 'ModelType' (first 50):");
var hits = asm.GetTypes()
    .Select(t => t.FullName)
    .Where(n => n is not null && n.Contains("ModelType", StringComparison.Ordinal))
    .OrderBy(n => n)
    .Take(50)
    .ToArray();

foreach (var hit in hits)
{
    Console.WriteLine(hit);
}

Console.WriteLine($"\nTotal hits: {hits.Length}");
