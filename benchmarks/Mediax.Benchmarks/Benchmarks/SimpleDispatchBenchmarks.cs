using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Mediax.Benchmarks.Handlers;
using Mediax.Core;
using Mediax.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 3, iterationCount: 5, id: "Short")]
public class SimpleDispatchBenchmarks
{
    private global::Mediator.IMediator _mediator = null!;
    private global::MediatR.IMediator  _mediatR  = null!;

    private static readonly MediaxEchoQuery    _mediaxEcho    = new("hello benchmark");
    private static readonly MediaxAddCommand   _mediaxAdd     = new(21, 21);
    private static readonly MediatorEchoQuery  _mediatorEcho  = new("hello benchmark");
    private static readonly MediatorAddCommand _mediatorAdd   = new(21, 21);
    private static readonly MediatREchoQuery   _mediatREcho   = new("hello benchmark");
    private static readonly MediatRAddCommand  _mediatRAdd    = new(21, 21);

    [GlobalSetup]
    public void Setup()
    {
        // ── Mediax ── generator-produced DispatchTable + MediaxDispatcher (Singleton handlers)
        var mediaxSvc = new ServiceCollection();
        mediaxSvc.AddLogging();
        mediaxSvc.AddMediax(DispatchTable.Handlers);
        DispatchTable.RegisterAll(mediaxSvc);
        MediaxRuntime.Init(mediaxSvc.BuildServiceProvider());

        // ── Mediator (martinothamar) ──
        var mediatorSvc = new ServiceCollection();
        mediatorSvc.AddLogging();
        mediatorSvc.AddMediator(o => o.ServiceLifetime = ServiceLifetime.Singleton);
        _mediator = mediatorSvc.BuildServiceProvider().GetRequiredService<global::Mediator.IMediator>();

        // ── MediatR ──
        var mediatRSvc = new ServiceCollection();
        mediatRSvc.AddLogging();
        mediatRSvc.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SimpleDispatchBenchmarks).Assembly));
        _mediatR = mediatRSvc.BuildServiceProvider().GetRequiredService<global::MediatR.IMediator>();
    }

    // ── EchoQuery ─────────────────────────────────────────────────────────────

    /// Per-type extension Send() — calls handler field directly, ~3-5 ns
    [BenchmarkCategory("EchoQuery"), Benchmark(Baseline = true)]
    public ValueTask<Result<string>> EchoQuery_Mediax()
        => _mediaxEcho.Send(CancellationToken.None);

    [BenchmarkCategory("EchoQuery"), Benchmark]
    public ValueTask<string> EchoQuery_Mediator()
        => _mediator.Send(_mediatorEcho, CancellationToken.None);

    [BenchmarkCategory("EchoQuery"), Benchmark]
    public Task<string> EchoQuery_MediatR()
        => _mediatR.Send(_mediatREcho, CancellationToken.None);

    // ── AddCommand ────────────────────────────────────────────────────────────

    /// Per-type extension Send() — calls handler field directly, ~3-5 ns
    [BenchmarkCategory("AddCommand"), Benchmark(Baseline = true)]
    public ValueTask<Result<int>> AddCommand_Mediax()
        => _mediaxAdd.Send(CancellationToken.None);

    [BenchmarkCategory("AddCommand"), Benchmark]
    public ValueTask<int> AddCommand_Mediator()
        => _mediator.Send(_mediatorAdd, CancellationToken.None);

    [BenchmarkCategory("AddCommand"), Benchmark]
    public Task<int> AddCommand_MediatR()
        => _mediatR.Send(_mediatRAdd, CancellationToken.None);
}
