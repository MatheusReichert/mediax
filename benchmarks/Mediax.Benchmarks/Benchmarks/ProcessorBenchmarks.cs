using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Mediax.Benchmarks.Handlers;
using Mediax.Behaviors;
using Mediax.Core;
using Mediax.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Benchmarks.Benchmarks;

/// <summary>
/// Measures the overhead of pre/post processors vs raw pipeline behaviors.
///
///   Category "PrePostProcessor" — Mediax ProcessorBehavior with 1 pre + 1 post processor
///                                 vs MediatR/Mediator simulating the same via IPipelineBehavior
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[SimpleJob(warmupCount: 3, iterationCount: 5, id: "Short")]
public class ProcessorBenchmarks
{
    private static readonly MediaxProcessCommand   _mediaxCmd   = new(21);
    private static readonly MediatRProcessCommand  _mediatRCmd  = new(21);
    private static readonly MediatorProcessCommand _mediatorCmd = new(21);

    private global::MediatR.IMediator  _mediatR  = null!;
    private global::Mediator.IMediator _mediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        // ── Mediax: handler uses [UseBehavior(ProcessorBehavior<,>)];
        //           processors are registered via AddMediaxFromAssemblies.
        var mediaxSvc = new ServiceCollection();
        mediaxSvc.AddLogging();
        DispatchTable.RegisterAll(mediaxSvc);
        // Register processors for the benchmark command
        mediaxSvc.AddScoped<IRequestPreProcessor<MediaxProcessCommand>, NoOpPreProcessor>();
        mediaxSvc.AddScoped<IRequestPostProcessor<MediaxProcessCommand, int>, NoOpPostProcessor>();
        MediaxRuntime.Init(mediaxSvc.BuildServiceProvider());

        // ── MediatR: simulate pre/post via IPipelineBehavior ─────────────────
        var mediatRSvc = new ServiceCollection();
        mediatRSvc.AddLogging();
        mediatRSvc.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ProcessorBenchmarks).Assembly));
        mediatRSvc.AddSingleton(
            typeof(global::MediatR.IPipelineBehavior<,>),
            typeof(MediatRProcessorBehavior<,>));
        _mediatR = mediatRSvc.BuildServiceProvider()
                             .GetRequiredService<global::MediatR.IMediator>();

        // ── Mediator: simulate pre/post via IPipelineBehavior ─────────────────
        var mediatorSvc = new ServiceCollection();
        mediatorSvc.AddLogging();
        mediatorSvc.AddMediator(o => o.ServiceLifetime = ServiceLifetime.Singleton);
        mediatorSvc.AddSingleton(
            typeof(global::Mediator.IPipelineBehavior<,>),
            typeof(MediatorProcessorBehavior<,>));
        _mediator = mediatorSvc.BuildServiceProvider()
                               .GetRequiredService<global::Mediator.IMediator>();
    }

    [BenchmarkCategory("PrePostProcessor"), Benchmark(Baseline = true)]
    public ValueTask<Result<int>> PrePostProcessor_Mediax()
        => _mediaxCmd.Send(CancellationToken.None);

    [BenchmarkCategory("PrePostProcessor"), Benchmark]
    public Task<int> PrePostProcessor_MediatR()
        => _mediatR.Send(_mediatRCmd, CancellationToken.None);

    [BenchmarkCategory("PrePostProcessor"), Benchmark]
    public ValueTask<int> PrePostProcessor_Mediator()
        => _mediator.Send(_mediatorCmd, CancellationToken.None);
}
