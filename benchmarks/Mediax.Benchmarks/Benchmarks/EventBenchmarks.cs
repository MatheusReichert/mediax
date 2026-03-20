using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Mediax.Benchmarks.Handlers;
using Mediax.Core;
using Mediax.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Benchmarks.Benchmarks;

/// <summary>
/// Compares event publishing (fire-and-forget pub/sub) across Mediax, Mediator and MediatR.
///
///   Category "SingleSubscriber"  — one handler registered for the event
///   Category "MultiSubscriber"   — three handlers registered for the same event (fan-out)
///   Category "ParallelFanOut"    — Mediax ParallelWhenAll vs MediatR Publish (always parallel)
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 3, iterationCount: 5, id: "Short")]
public class EventBenchmarks
{
    // ── Mediax requests ───────────────────────────────────────────────────────
    private static readonly MediaxOrderCreatedEvent  _mediaxSingle = new(1);
    private static readonly MediaxOrderShippedEvent  _mediaxMulti  = new(1);

    // ── MediatR notifications ─────────────────────────────────────────────────
    private static readonly MediatROrderCreatedNotification _mediatRSingle = new() { OrderId = 1 };
    private static readonly MediatROrderShippedNotification _mediatRMulti  = new() { OrderId = 1 };

    // ── Mediator notifications ────────────────────────────────────────────────
    private static readonly MediatorOrderCreatedNotification _mediatorSingle = new(1);

    private global::MediatR.IMediator  _mediatR  = null!;
    private global::Mediator.IMediator _mediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        // ── Mediax ────────────────────────────────────────────────────────────
        var mediaxSvc = new ServiceCollection();
        mediaxSvc.AddLogging();
        DispatchTable.RegisterAll(mediaxSvc);
        MediaxRuntime.Init(mediaxSvc.BuildServiceProvider());

        // ── MediatR ───────────────────────────────────────────────────────────
        var mediatRSvc = new ServiceCollection();
        mediatRSvc.AddLogging();
        mediatRSvc.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(EventBenchmarks).Assembly));
        _mediatR = mediatRSvc.BuildServiceProvider()
                             .GetRequiredService<global::MediatR.IMediator>();

        // ── Mediator ──────────────────────────────────────────────────────────
        var mediatorSvc = new ServiceCollection();
        mediatorSvc.AddLogging();
        mediatorSvc.AddMediator(o => o.ServiceLifetime = ServiceLifetime.Singleton);
        _mediator = mediatorSvc.BuildServiceProvider()
                               .GetRequiredService<global::Mediator.IMediator>();
    }

    // ── Single subscriber ─────────────────────────────────────────────────────

    [BenchmarkCategory("SingleSubscriber"), Benchmark(Baseline = true)]
    public ValueTask<Result<Unit>> SingleSubscriber_Mediax_Sequential()
        => _mediaxSingle.Publish(EventStrategy.Sequential, CancellationToken.None);

    [BenchmarkCategory("SingleSubscriber"), Benchmark]
    public Task SingleSubscriber_MediatR()
        => _mediatR.Publish(_mediatRSingle, CancellationToken.None);

    [BenchmarkCategory("SingleSubscriber"), Benchmark]
    public ValueTask SingleSubscriber_Mediator()
        => _mediator.Publish(_mediatorSingle, CancellationToken.None);

    // ── Three subscribers (sequential) ───────────────────────────────────────

    [BenchmarkCategory("MultiSubscriber"), Benchmark(Baseline = true)]
    public ValueTask<Result<Unit>> MultiSubscriber_Mediax_Sequential()
        => _mediaxMulti.Publish(EventStrategy.Sequential, CancellationToken.None);

    [BenchmarkCategory("MultiSubscriber"), Benchmark]
    public Task MultiSubscriber_MediatR()
        => _mediatR.Publish(_mediatRMulti, CancellationToken.None);

    // ── Three subscribers (parallel WhenAll) ─────────────────────────────────

    [BenchmarkCategory("ParallelFanOut"), Benchmark(Baseline = true)]
    public ValueTask<Result<Unit>> ParallelFanOut_Mediax_WhenAll()
        => _mediaxMulti.Publish(EventStrategy.ParallelWhenAll, CancellationToken.None);

    [BenchmarkCategory("ParallelFanOut"), Benchmark]
    public Task ParallelFanOut_MediatR()
        => _mediatR.Publish(_mediatRMulti, CancellationToken.None);
}
