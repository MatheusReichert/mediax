using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Mediax.Benchmarks.Handlers;
using Mediax.Core;
using Mediax.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Benchmarks.Benchmarks;

/// <summary>
/// Compares static (fast-path) vs polymorphic (switch) dispatch in Mediax,
/// and how both compare to MediatR and Mediator when the request type is known
/// only at the call site as <c>IRequest&lt;T&gt;</c>.
///
///   Category "StaticDispatch"      — Mediax per-type extension .Send() (~2-3 ns, zero alloc)
///   Category "PolymorphicDispatch" — Mediax IMediaxDispatcher.Dispatch&lt;T&gt;() switch (~8-12 ns)
///                                    vs MediatR and Mediator (both use reflection/dict lookup)
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 3, iterationCount: 5, id: "Short")]
public class PolymorphicDispatchBenchmarks
{
    // Static-dispatch path: concrete type known at call site
    private static readonly MediaxPolyQuery _mediaxConcrete = new("hello");

    // Polymorphic path: variable typed as IRequest<string>
    private IRequest<string> _mediaxPoly = null!;

    private static readonly MediatRPolyQuery  _mediatRQuery  = new("hello");
    private static readonly MediatorPolyQuery _mediatorQuery = new("hello");

    private IMediaxDispatcher              _mediaxDispatcher = null!;
    private global::MediatR.IMediator      _mediatR          = null!;
    private global::Mediator.IMediator     _mediator         = null!;

    [GlobalSetup]
    public void Setup()
    {
        // ── Mediax ────────────────────────────────────────────────────────────
        var mediaxSvc = new ServiceCollection();
        mediaxSvc.AddLogging();
        DispatchTable.RegisterAll(mediaxSvc);
        var mediaxSp = mediaxSvc.BuildServiceProvider();
        MediaxRuntime.Init(mediaxSp);
        _mediaxDispatcher = mediaxSp.GetRequiredService<IMediaxDispatcher>();
        _mediaxPoly = new MediaxPolyQuery("hello"); // same instance, but typed as interface

        // ── MediatR ───────────────────────────────────────────────────────────
        var mediatRSvc = new ServiceCollection();
        mediatRSvc.AddLogging();
        mediatRSvc.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PolymorphicDispatchBenchmarks).Assembly));
        _mediatR = mediatRSvc.BuildServiceProvider()
                             .GetRequiredService<global::MediatR.IMediator>();

        // ── Mediator ──────────────────────────────────────────────────────────
        var mediatorSvc = new ServiceCollection();
        mediatorSvc.AddLogging();
        mediatorSvc.AddMediator(o => o.ServiceLifetime = ServiceLifetime.Singleton);
        _mediator = mediatorSvc.BuildServiceProvider()
                               .GetRequiredService<global::Mediator.IMediator>();
    }

    // ── Static dispatch (per-type extension, zero-alloc) ─────────────────────

    [BenchmarkCategory("StaticDispatch"), Benchmark(Baseline = true)]
    public ValueTask<Result<string>> StaticDispatch_Mediax()
        => _mediaxConcrete.Send(CancellationToken.None);

    [BenchmarkCategory("StaticDispatch"), Benchmark]
    public ValueTask<string> StaticDispatch_Mediator()
        => _mediator.Send(_mediatorQuery, CancellationToken.None);

    [BenchmarkCategory("StaticDispatch"), Benchmark]
    public Task<string> StaticDispatch_MediatR()
        => _mediatR.Send(_mediatRQuery, CancellationToken.None);

    // ── Polymorphic dispatch (IRequest<T> variable) ───────────────────────────

    [BenchmarkCategory("PolymorphicDispatch"), Benchmark(Baseline = true)]
    public ValueTask<Result<string>> PolymorphicDispatch_Mediax()
        => _mediaxDispatcher.Dispatch(_mediaxPoly, CancellationToken.None);

    [BenchmarkCategory("PolymorphicDispatch"), Benchmark]
    public ValueTask<string> PolymorphicDispatch_Mediator()
        => _mediator.Send(_mediatorQuery, CancellationToken.None);

    [BenchmarkCategory("PolymorphicDispatch"), Benchmark]
    public Task<string> PolymorphicDispatch_MediatR()
        => _mediatR.Send(_mediatRQuery, CancellationToken.None);
}
