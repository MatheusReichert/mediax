// Mediax request types and handlers for benchmarks.
// Mediax.Core defines IQuery<T>, ICommand<T> in namespace Mediax.Core.
using Mediax.Core;

namespace Mediax.Benchmarks.Handlers;

// ── Requests ─────────────────────────────────────────────────────────────────

public sealed record MediaxEchoQuery(string Message) : IQuery<string>;

public sealed record MediaxAddCommand(int A, int B) : ICommand<int>;

// ── Handlers ─────────────────────────────────────────────────────────────────

[Handler]
public sealed class MediaxEchoHandler : IHandler<MediaxEchoQuery, string>
{
    public ValueTask<Result<string>> Handle(MediaxEchoQuery request, CancellationToken ct)
        => ValueTask.FromResult(Result<string>.Ok(request.Message));
}

[Handler]
public sealed class MediaxAddHandler : IHandler<MediaxAddCommand, int>
{
    public ValueTask<Result<int>> Handle(MediaxAddCommand request, CancellationToken ct)
        => ValueTask.FromResult(Result<int>.Ok(request.A + request.B));
}
