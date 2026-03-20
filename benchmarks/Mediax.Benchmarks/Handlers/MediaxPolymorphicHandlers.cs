// Mediax polymorphic dispatch benchmark — IRequest<T> variable vs concrete type.
// Tests the medium-path (switch dispatcher) vs fast-path (per-type extension).
using Mediax.Core;

namespace Mediax.Benchmarks.Handlers;

// ── Request ───────────────────────────────────────────────────────────────────

public sealed record MediaxPolyQuery(string Value) : IQuery<string>;

// ── Handler ───────────────────────────────────────────────────────────────────

[Handler]
public sealed class MediaxPolyHandler : IHandler<MediaxPolyQuery, string>
{
    public ValueTask<Result<string>> Handle(MediaxPolyQuery request, CancellationToken ct)
        => ValueTask.FromResult(Result<string>.Ok(request.Value));
}

// ── MediatR polymorphic (IRequest<T> variable) ────────────────────────────────

public sealed record MediatRPolyQuery(string Value) : global::MediatR.IRequest<string>;

public sealed class MediatRPolyHandler
    : global::MediatR.IRequestHandler<MediatRPolyQuery, string>
{
    public Task<string> Handle(MediatRPolyQuery request, CancellationToken ct)
        => Task.FromResult(request.Value);
}

// ── Mediator polymorphic ──────────────────────────────────────────────────────

public sealed record MediatorPolyQuery(string Value) : global::Mediator.IQuery<string>;

public sealed class MediatorPolyHandler
    : global::Mediator.IQueryHandler<MediatorPolyQuery, string>
{
    public ValueTask<string> Handle(MediatorPolyQuery query, CancellationToken ct)
        => ValueTask.FromResult(query.Value);
}
