using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;

namespace JitHub.Markdown.Benchmarks;

public static class Program
{
    public static int Main(string[] args)
    {
        var config = DefaultConfig.Instance
            .AddLogger(ConsoleLogger.Default)
            .AddColumnProvider(DefaultColumnProviders.Instance)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddJob(Job.ShortRun
                .WithWarmupCount(2)
                .WithIterationCount(6));

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return 0;
    }
}
