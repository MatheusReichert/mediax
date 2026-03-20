using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Mediax.Benchmarks.Handlers;
using Mediax.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Benchmarks.Benchmarks;

/// <summary>
/// Measures the cost of consuming a streaming handler end-to-end.
/// Count=1  — single item (overhead of Stream() call + one iteration)
/// Count=10 — ten items (pipeline + yield overhead per item)
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5, id: "Short")]
public class StreamBenchmarks
{
    private static readonly MediaxCountStreamRequest _count1  = new(1);
    private static readonly MediaxCountStreamRequest _count10 = new(10);

    [GlobalSetup]
    public void Setup()
    {
        var svc = new ServiceCollection();
        svc.AddLogging();
        svc.AddMediax(DispatchTable.Handlers);
        DispatchTable.RegisterAll(svc);
        MediaxRuntime.Init(svc.BuildServiceProvider());
    }

    [Benchmark(Baseline = true, Description = "Stream(Count=1)")]
    public async Task<int> Stream_Count1()
    {
        int last = 0;
        await foreach (var item in _count1.Stream(CancellationToken.None))
            last = item;
        return last;
    }

    [Benchmark(Description = "Stream(Count=10)")]
    public async Task<int> Stream_Count10()
    {
        int last = 0;
        await foreach (var item in _count10.Stream(CancellationToken.None))
            last = item;
        return last;
    }
}
