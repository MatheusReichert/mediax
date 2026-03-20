// martinothamar/Mediator request types and handlers for benchmarks.
// Mediator.Abstractions defines IRequest<T>, IQueryHandler<,>, ICommandHandler<,> in namespace Mediator.

namespace Mediax.Benchmarks.Handlers;

// ── Requests ─────────────────────────────────────────────────────────────────

public sealed record MediatorEchoQuery(string Message) : global::Mediator.IQuery<string>;

public sealed record MediatorAddCommand(int A, int B) : global::Mediator.ICommand<int>;

// Pipeline benchmark requests
public sealed record MediatorAddWithBehaviorCommand(int A, int B) : global::Mediator.ICommand<int>;

// ── Handlers ─────────────────────────────────────────────────────────────────

public sealed class MediatorEchoHandler : global::Mediator.IQueryHandler<MediatorEchoQuery, string>
{
    public ValueTask<string> Handle(MediatorEchoQuery query, CancellationToken ct)
        => ValueTask.FromResult(query.Message);
}

public sealed class MediatorAddHandler : global::Mediator.ICommandHandler<MediatorAddCommand, int>
{
    public ValueTask<int> Handle(MediatorAddCommand command, CancellationToken ct)
        => ValueTask.FromResult(command.A + command.B);
}

public sealed class MediatorAddWithBehaviorHandler : global::Mediator.ICommandHandler<MediatorAddWithBehaviorCommand, int>
{
    public ValueTask<int> Handle(MediatorAddWithBehaviorCommand command, CancellationToken ct)
        => ValueTask.FromResult(command.A + command.B);
}

// ── No-op pipeline behavior ───────────────────────────────────────────────────

public sealed class MediatorNoOpBehavior<TMessage, TResponse>
    : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : global::Mediator.IMessage
{
    public ValueTask<TResponse> Handle(
        TMessage message,
        global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken ct)
        => next(message, ct);
}
