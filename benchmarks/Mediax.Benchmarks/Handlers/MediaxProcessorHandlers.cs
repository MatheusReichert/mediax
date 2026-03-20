// Mediax handlers and processors for pre/post processor benchmark.
using Mediax.Core;
using Mediax.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Mediax.Runtime;

namespace Mediax.Benchmarks.Handlers;

// ── Request ───────────────────────────────────────────────────────────────────

public sealed record MediaxProcessCommand(int Value) : ICommand<int>;

// ── Handler ───────────────────────────────────────────────────────────────────

[Handler]
[UseBehavior(typeof(ProcessorBehavior<,>))]
public sealed class MediaxProcessHandler : IHandler<MediaxProcessCommand, int>
{
    public ValueTask<Result<int>> Handle(MediaxProcessCommand request, CancellationToken ct)
        => ValueTask.FromResult(Result<int>.Ok(request.Value * 2));
}

// ── Pre/Post processors (no-op — measure framework overhead only) ─────────────

public sealed class NoOpPreProcessor : IRequestPreProcessor<MediaxProcessCommand>
{
    public ValueTask Process(MediaxProcessCommand request, CancellationToken ct)
        => ValueTask.CompletedTask;
}

public sealed class NoOpPostProcessor : IRequestPostProcessor<MediaxProcessCommand, int>
{
    public ValueTask Process(MediaxProcessCommand request, Result<int> result, CancellationToken ct)
        => ValueTask.CompletedTask;
}

// ── MediatR equivalent: IPipelineBehavior acting as pre/post processor ─────────

public sealed record MediatRProcessCommand(int Value) : global::MediatR.IRequest<int>;

public sealed class MediatRProcessHandler
    : global::MediatR.IRequestHandler<MediatRProcessCommand, int>
{
    public Task<int> Handle(MediatRProcessCommand request, CancellationToken ct)
        => Task.FromResult(request.Value * 2);
}

/// <summary>MediatR simulates pre/post via a pipeline behavior wrapping.</summary>
public sealed class MediatRProcessorBehavior<TRequest, TResponse>
    : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        global::MediatR.RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        await Task.CompletedTask; // pre
        TResponse result = await next(ct);
        await Task.CompletedTask; // post
        return result;
    }
}

// ── Mediator equivalent ───────────────────────────────────────────────────────

public sealed record MediatorProcessCommand(int Value) : global::Mediator.ICommand<int>;

public sealed class MediatorProcessHandler
    : global::Mediator.ICommandHandler<MediatorProcessCommand, int>
{
    public ValueTask<int> Handle(MediatorProcessCommand command, CancellationToken ct)
        => ValueTask.FromResult(command.Value * 2);
}

public sealed class MediatorProcessorBehavior<TMessage, TResponse>
    : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : global::Mediator.IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken ct)
    {
        await ValueTask.CompletedTask; // pre
        TResponse result = await next(message, ct);
        await ValueTask.CompletedTask; // post
        return result;
    }
}
