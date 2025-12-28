using System.Runtime.CompilerServices;
using FluentAssertions;
using NUnit.Framework;
using SkiaSharp;

namespace JitHub.Markdown.Tests.Golden;

internal static class GoldenImageAssert
{
    internal sealed record Options(
        int PerChannelTolerance = 2,
        double MaxMismatchedPixelRatio = 0.0025,
        int PngQuality = 100);

    public static void MatchesBaseline(SKBitmap actual, string baselineName, Options? options = null)
    {
        if (actual is null) throw new ArgumentNullException(nameof(actual));
        if (string.IsNullOrWhiteSpace(baselineName)) throw new ArgumentException("Baseline name is required", nameof(baselineName));

        options ??= new Options();

        var repoRoot = RepoRootLocator.FindRepoRoot();
        var baselinePath = Path.Combine(repoRoot, "JitHub.Markdown.Tests", "Golden", "Baselines", baselineName + ".png");
        var artifactsDir = Path.Combine(repoRoot, "JitHub.Markdown.Tests", "Golden", "Artifacts");
        Directory.CreateDirectory(artifactsDir);

        var updateGoldens = string.Equals(Environment.GetEnvironmentVariable("UPDATE_GOLDENS"), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("UPDATE_GOLDENS"), "true", StringComparison.OrdinalIgnoreCase);

        if (!File.Exists(baselinePath))
        {
            if (updateGoldens)
            {
                SavePng(actual, baselinePath, options.PngQuality);
                Assert.Pass($"Wrote new baseline: {baselinePath}");
                return;
            }

            var actualPath = Path.Combine(artifactsDir, baselineName + "-actual.png");
            SavePng(actual, actualPath, options.PngQuality);

            Assert.Fail(
                $"Golden baseline missing: {baselinePath}{Environment.NewLine}" +
                $"Wrote actual render to: {actualPath}{Environment.NewLine}" +
                $"To create/update baselines: set UPDATE_GOLDENS=1 and re-run tests.");
            return;
        }

        using var expected = SKBitmap.Decode(baselinePath);
        expected.Should().NotBeNull($"baseline should be readable: {baselinePath}");

        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            if (updateGoldens)
            {
                SavePng(actual, baselinePath, options.PngQuality);
                Assert.Pass($"Updated baseline size: {baselinePath}");
                return;
            }

            var actualPath = Path.Combine(artifactsDir, baselineName + "-actual.png");
            SavePng(actual, actualPath, options.PngQuality);

            Assert.Fail(
                $"Golden baseline size mismatch for '{baselineName}'.{Environment.NewLine}" +
                $"Expected: {expected.Width}x{expected.Height}{Environment.NewLine}" +
                $"Actual:   {actual.Width}x{actual.Height}{Environment.NewLine}" +
                $"Baseline: {baselinePath}{Environment.NewLine}" +
                $"Actual:   {actualPath}{Environment.NewLine}" +
                $"To update baselines: set UPDATE_GOLDENS=1 and re-run tests.");
            return;
        }

        var mismatchCount = CountMismatchedPixels(expected, actual, options.PerChannelTolerance, out var maxChannelDelta);
        var totalPixels = (long)actual.Width * actual.Height;
        var mismatchRatio = totalPixels == 0 ? 0 : (double)mismatchCount / totalPixels;

        if (mismatchRatio <= options.MaxMismatchedPixelRatio)
        {
            return;
        }

        if (updateGoldens)
        {
            SavePng(actual, baselinePath, options.PngQuality);
            Assert.Pass($"Updated baseline after diff: {baselinePath}");
            return;
        }

        var outActualPath = Path.Combine(artifactsDir, baselineName + "-actual.png");
        SavePng(actual, outActualPath, options.PngQuality);

        Assert.Fail(
            $"Golden mismatch for '{baselineName}'.{Environment.NewLine}" +
            $"Baseline: {baselinePath}{Environment.NewLine}" +
            $"Actual:   {outActualPath}{Environment.NewLine}" +
            $"Tolerance: per-channel <= {options.PerChannelTolerance}, max mismatch ratio <= {options.MaxMismatchedPixelRatio:P3}{Environment.NewLine}" +
            $"Observed: {mismatchCount} / {totalPixels} pixels mismatched ({mismatchRatio:P3}), max channel delta={maxChannelDelta}.{Environment.NewLine}" +
            $"To update baselines: set UPDATE_GOLDENS=1 and re-run tests.");
    }

    private static void SavePng(SKBitmap bitmap, string path, int quality)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality);
        using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        data.SaveTo(stream);
    }

    private static long CountMismatchedPixels(SKBitmap expected, SKBitmap actual, int perChannelTolerance, out int maxChannelDelta)
    {
        maxChannelDelta = 0;

        var mismatch = 0L;

        // Pixel-by-pixel compare. This is slower than span-based comparisons, but it's stable
        // across SkiaSharp API versions and the image sizes in tests are small.
        for (var y = 0; y < actual.Height; y++)
        {
            for (var x = 0; x < actual.Width; x++)
            {
                var e = expected.GetPixel(x, y);
                var a = actual.GetPixel(x, y);

                var dr = Math.Abs(e.Red - a.Red);
                var dg = Math.Abs(e.Green - a.Green);
                var db = Math.Abs(e.Blue - a.Blue);

                var localMax = Math.Max(dr, Math.Max(dg, db));
                if (localMax > maxChannelDelta)
                {
                    maxChannelDelta = localMax;
                }

                if (dr > perChannelTolerance || dg > perChannelTolerance || db > perChannelTolerance)
                {
                    mismatch++;
                }
            }
        }

        return mismatch;
    }

    private static class RepoRootLocator
    {
        public static string FindRepoRoot([CallerFilePath] string? callerFilePath = null)
        {
            // Prefer a stable anchor: the current source file path (works in local + CI).
            var start = !string.IsNullOrWhiteSpace(callerFilePath)
                ? new DirectoryInfo(Path.GetDirectoryName(callerFilePath)!)
                : new DirectoryInfo(AppContext.BaseDirectory);

            for (DirectoryInfo? dir = start; dir is not null; dir = dir.Parent)
            {
                var slnx = Path.Combine(dir.FullName, "JitHubV3.slnx");
                if (File.Exists(slnx))
                {
                    return dir.FullName;
                }
            }

            throw new InvalidOperationException("Unable to locate repo root (expected to find JitHubV3.slnx in a parent directory). ");
        }
    }
}
