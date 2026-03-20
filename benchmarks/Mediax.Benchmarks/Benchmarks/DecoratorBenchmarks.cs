using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Mediax.Benchmarks.Handlers;
using Mediax.Core;
using Mediax.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Benchmarks.Benchmarks;

/// <summary>
/// Measures the cost of the Timeout and Retry decorators on the dispatch path.
///
///   Category "Timeout"  — WithTimeout() wrapping a Singleton handler
///   Category "Retry"    — WithRetry(1) wrapping a Singleton handler (no actual retry, measures overhead)
///
/// MediatR has no built-in timeout/retry — comparison is against raw dispatch
/// to show the decorator overhead is negligible compared to any alternative.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 3, iterationCount: 5, id: "Short")]
public class DecoratorBenchmarks
{
    private static readonly MediaxAddCommand _raw     = new(21, 21);
    private static readonly MediatRAddCommand _mediatRRaw = new(21, 21);

    // WithTimeout and WithRetry return new decorator wrappers each call —
    // we pre-create them to benchmark only the dispatch cost, not allocation.
    private IRequest<int> _withTimeout = null!;
    private IRequest<int> _withRetry   = null!;

    private global::MediatR.IMediator _mediatR = null!;

    [GlobalSetup]
    public void Setup()
    {
        var mediaxSvc = new ServiceCollection();
        mediaxSvc.AddLogging();
        DispatchTable.RegisterAll(mediaxSvc);
        MediaxRuntime.Init(mediaxSvc.BuildServiceProvider());

        _withTimeout = _raw.WithTimeout(TimeSpan.FromSeconds(5));
        _withRetry   = _raw.WithRetry(1);

        var mediatRSvc = new ServiceCollection();
        mediatRSvc.AddLogging();
        mediatRSvc.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DecoratorBenchmarks).Assembly));
        _mediatR = mediatRSvc.BuildServiceProvider()
                             .GetRequiredService<global::MediatR.IMediator>();
    }

    // ── Baseline: raw dispatch without decorator ──────────────────────────────

    [BenchmarkCategory("Timeout"), Benchmark(Baseline = true)]
    public ValueTask<Result<int>> Timeout_Mediax_Baseline()
        => _raw.Send(CancellationToken.None);

    [BenchmarkCategory("Timeout"), Benchmark]
    public ValueTask<Result<int>> Timeout_Mediax_WithTimeout()
        => _withTimeout.Send(CancellationToken.None);

    [BenchmarkCategory("Timeout"), Benchmark]
    public Task<int> Timeout_MediatR_Raw()
        => _mediatR.Send(_mediatRRaw, CancellationToken.None);

    // ── Retry decorator ───────────────────────────────────────────────────────

    [BenchmarkCategory("Retry"), Benchmark(Baseline = true)]
    public ValueTask<Result<int>> Retry_Mediax_Baseline()
        => _raw.Send(CancellationToken.None);

    [BenchmarkCategory("Retry"), Benchmark]
    public ValueTask<Result<int>> Retry_Mediax_WithRetry()
        => _withRetry.Send(CancellationToken.None);

    [BenchmarkCategory("Retry"), Benchmark]
    public Task<int> Retry_MediatR_Raw()
        => _mediatR.Send(_mediatRRaw, CancellationToken.None);
}
