using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Mediax.Benchmarks.Handlers;
using Mediax.Core;
using Mediax.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Benchmarks.Benchmarks;

/// <summary>
/// Compares Mediax, Mediator (martinothamar) and MediatR across pipeline scenarios:
///
///   Category "Singleton"         — no pipeline behavior, Singleton handler
///   Category "WithBehavior"      — 1 no-op behavior, Singleton handler
///   Category "Scoped"            — no pipeline behavior, Scoped handler (CreateScope per call)
///   Category "ScopedWithBehavior"— 1 no-op behavior, Scoped handler
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 3, iterationCount: 5, id: "Short")]
public class PipelineBenchmarks
{
    // ── Mediax requests ───────────────────────────────────────────────────────
    private static readonly MediaxAddCommand                      _mediaxSingleton        = new(21, 21);
    private static readonly MediaxAddSingletonWithBehaviorCommand _mediaxSingletonWithBeh = new(21, 21);
    private static readonly MediaxAddScopedCommand                _mediaxScoped           = new(21, 21);
    private static readonly MediaxAddScopedWithBehaviorCommand    _mediaxScopedWithBeh    = new(21, 21);

    // ── Mediator (martinothamar) requests ─────────────────────────────────────
    private static readonly MediatorAddCommand             _mediatorSingleton      = new(21, 21);
    private static readonly MediatorAddWithBehaviorCommand _mediatorSingletonBeh   = new(21, 21);

    // ── MediatR requests ──────────────────────────────────────────────────────
    private static readonly MediatRAddCommand             _mediatRSingleton    = new(21, 21);
    private static readonly MediatRAddWithBehaviorCommand _mediatRSingletonBeh = new(21, 21);

    private global::Mediator.IMediator _mediator = null!;
    private global::MediatR.IMediator  _mediatR  = null!;

    [GlobalSetup]
    public void Setup()
    {
        // ── Mediax ────────────────────────────────────────────────────────────
        var mediaxSvc = new ServiceCollection();
        mediaxSvc.AddLogging();
        mediaxSvc.AddMediax(DispatchTable.Handlers);
        DispatchTable.RegisterAll(mediaxSvc);
        MediaxRuntime.Init(mediaxSvc.BuildServiceProvider());

        // ── Mediator (Singleton lifetime + open-generic no-op behavior) ───────
        var mediatorSvc = new ServiceCollection();
        mediatorSvc.AddLogging();
        mediatorSvc.AddMediator(o => o.ServiceLifetime = ServiceLifetime.Singleton);
        mediatorSvc.AddSingleton(
            typeof(global::Mediator.IPipelineBehavior<,>),
            typeof(MediatorNoOpBehavior<,>));
        _mediator = mediatorSvc.BuildServiceProvider()
                               .GetRequiredService<global::Mediator.IMediator>();

        // ── MediatR (open-generic no-op behavior, Singleton) ──────────────────
        var mediatRSvc = new ServiceCollection();
        mediatRSvc.AddLogging();
        mediatRSvc.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PipelineBenchmarks).Assembly));
        mediatRSvc.AddSingleton(
            typeof(global::MediatR.IPipelineBehavior<,>),
            typeof(MediatRNoOpBehavior<,>));
        _mediatR = mediatRSvc.BuildServiceProvider()
                             .GetRequiredService<global::MediatR.IMediator>();
    }

    // ── Singleton (no behavior) ───────────────────────────────────────────────

    [BenchmarkCategory("Singleton"), Benchmark(Baseline = true)]
    public ValueTask<Result<int>> Singleton_Mediax()
        => _mediaxSingleton.Send(CancellationToken.None);

    [BenchmarkCategory("Singleton"), Benchmark]
    public ValueTask<int> Singleton_Mediator()
        => _mediator.Send(_mediatorSingleton, CancellationToken.None);

    [BenchmarkCategory("Singleton"), Benchmark]
    public Task<int> Singleton_MediatR()
        => _mediatR.Send(_mediatRSingleton, CancellationToken.None);

    // ── Singleton + 1 Behavior ────────────────────────────────────────────────

    [BenchmarkCategory("WithBehavior"), Benchmark(Baseline = true)]
    public ValueTask<Result<int>> WithBehavior_Mediax()
        => _mediaxSingletonWithBeh.Send(CancellationToken.None);

    [BenchmarkCategory("WithBehavior"), Benchmark]
    public ValueTask<int> WithBehavior_Mediator()
        => _mediator.Send(_mediatorSingletonBeh, CancellationToken.None);

    [BenchmarkCategory("WithBehavior"), Benchmark]
    public Task<int> WithBehavior_MediatR()
        => _mediatR.Send(_mediatRSingletonBeh, CancellationToken.None);

    // ── Scoped (no behavior) ──────────────────────────────────────────────────
    // Note: Mediator and MediatR don't natively scope per-call like Mediax does;
    // for a fair comparison we benchmark Mediax Scoped vs their Singleton path,
    // which is what a real app using those libs would use.

    [BenchmarkCategory("Scoped"), Benchmark(Baseline = true)]
    public ValueTask<Result<int>> Scoped_Mediax()
        => _mediaxScoped.Send(CancellationToken.None);

    [BenchmarkCategory("Scoped"), Benchmark]
    public ValueTask<int> Scoped_Mediator()
        => _mediator.Send(_mediatorSingleton, CancellationToken.None);

    [BenchmarkCategory("Scoped"), Benchmark]
    public Task<int> Scoped_MediatR()
        => _mediatR.Send(_mediatRSingleton, CancellationToken.None);

    // ── Scoped + 1 Behavior ───────────────────────────────────────────────────

    [BenchmarkCategory("ScopedWithBehavior"), Benchmark(Baseline = true)]
    public ValueTask<Result<int>> ScopedWithBehavior_Mediax()
        => _mediaxScopedWithBeh.Send(CancellationToken.None);

    [BenchmarkCategory("ScopedWithBehavior"), Benchmark]
    public ValueTask<int> ScopedWithBehavior_Mediator()
        => _mediator.Send(_mediatorSingletonBeh, CancellationToken.None);

    [BenchmarkCategory("ScopedWithBehavior"), Benchmark]
    public Task<int> ScopedWithBehavior_MediatR()
        => _mediatR.Send(_mediatRSingletonBeh, CancellationToken.None);
}
