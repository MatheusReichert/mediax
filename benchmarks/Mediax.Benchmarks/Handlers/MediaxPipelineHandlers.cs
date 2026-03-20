// Mediax handlers for pipeline benchmark scenarios:
//   - Singleton + 1 Behavior
//   - Scoped (no behavior)
//   - Scoped + 1 Behavior
using Mediax.Core;

namespace Mediax.Benchmarks.Handlers;

// ── Shared no-op behavior ─────────────────────────────────────────────────────

/// <summary>No-op behavior used to measure the cost of a single pipeline step.</summary>
public sealed class NoOpBehavior<TRequest, TResponse> : IBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, allows ref struct
{
    public ValueTask<Result<TResponse>> Handle(TRequest request, HandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
        => next(request, ct);
}

// ── Singleton + Behavior ──────────────────────────────────────────────────────

public sealed record MediaxAddSingletonWithBehaviorCommand(int A, int B) : ICommand<int>;

[Handler]
[UseBehavior(typeof(NoOpBehavior<,>))]
public sealed class MediaxAddSingletonWithBehaviorHandler : IHandler<MediaxAddSingletonWithBehaviorCommand, int>
{
    public ValueTask<Result<int>> Handle(MediaxAddSingletonWithBehaviorCommand request, CancellationToken ct)
        => ValueTask.FromResult(Result<int>.Ok(request.A + request.B));
}

// ── Scoped (no behavior) ──────────────────────────────────────────────────────

public sealed record MediaxAddScopedCommand(int A, int B) : ICommand<int>;

[Handler(Lifetime = HandlerLifetime.Scoped)]
public sealed class MediaxAddScopedHandler : IHandler<MediaxAddScopedCommand, int>
{
    public ValueTask<Result<int>> Handle(MediaxAddScopedCommand request, CancellationToken ct)
        => ValueTask.FromResult(Result<int>.Ok(request.A + request.B));
}

// ── Scoped + Behavior ─────────────────────────────────────────────────────────

public sealed record MediaxAddScopedWithBehaviorCommand(int A, int B) : ICommand<int>;

[Handler(Lifetime = HandlerLifetime.Scoped)]
[UseBehavior(typeof(NoOpBehavior<,>))]
public sealed class MediaxAddScopedWithBehaviorHandler : IHandler<MediaxAddScopedWithBehaviorCommand, int>
{
    public ValueTask<Result<int>> Handle(MediaxAddScopedWithBehaviorCommand request, CancellationToken ct)
        => ValueTask.FromResult(Result<int>.Ok(request.A + request.B));
}
