# Benchmarks (Phase 9.2)

This repo uses **BenchmarkDotNet** for Markdown pipeline performance.

## Project

- `JitHub.Markdown.Benchmarks` benchmarks:
  - Parse (Markdig → model)
  - Layout (model → layout)
  - Render (layout → Skia bitmap)
- Memory allocations are captured via `MemoryDiagnoser`.

## Run

From repo root:

```powershell
# Full suite (short run config)
dotnet run -c Release --project .\JitHub.Markdown.Benchmarks\JitHub.Markdown.Benchmarks.csproj

# Filter (example: render only)
dotnet run -c Release --project .\JitHub.Markdown.Benchmarks\JitHub.Markdown.Benchmarks.csproj -- --filter *Render*
```

## Notes

- Inputs are embedded markdown samples under `JitHub.Markdown.Benchmarks/Samples`.
- The `large` sample is expanded deterministically in code to simulate a larger document.
- The benchmark theme avoids system font family names to keep font selection deterministic.
