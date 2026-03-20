// MediatR request types and handlers for benchmarks.
// MediatR defines IRequest<T>, IRequestHandler<,> in namespace MediatR.

namespace Mediax.Benchmarks.Handlers;

// ── Requests ─────────────────────────────────────────────────────────────────

public sealed record MediatREchoQuery(string Message) : global::MediatR.IRequest<string>;

public sealed record MediatRAddCommand(int A, int B) : global::MediatR.IRequest<int>;

// Pipeline benchmark requests
public sealed record MediatRAddWithBehaviorCommand(int A, int B) : global::MediatR.IRequest<int>;

// ── Handlers ─────────────────────────────────────────────────────────────────

public sealed class MediatREchoHandler : global::MediatR.IRequestHandler<MediatREchoQuery, string>
{
    public Task<string> Handle(MediatREchoQuery request, CancellationToken ct)
        => Task.FromResult(request.Message);
}

public sealed class MediatRAddHandler : global::MediatR.IRequestHandler<MediatRAddCommand, int>
{
    public Task<int> Handle(MediatRAddCommand request, CancellationToken ct)
        => Task.FromResult(request.A + request.B);
}

public sealed class MediatRAddWithBehaviorHandler : global::MediatR.IRequestHandler<MediatRAddWithBehaviorCommand, int>
{
    public Task<int> Handle(MediatRAddWithBehaviorCommand request, CancellationToken ct)
        => Task.FromResult(request.A + request.B);
}

// ── No-op pipeline behavior ───────────────────────────────────────────────────

public sealed class MediatRNoOpBehavior<TRequest, TResponse>
    : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(
        TRequest request,
        global::MediatR.RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
        => next(ct);
}
