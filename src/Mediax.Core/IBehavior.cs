namespace Mediax.Core;

/// <summary>
/// Delegate representing the next step in a pipeline chain.
/// The request is passed through so the pipeline can be pre-built once at startup
/// without capturing a specific request instance — enabling zero per-call allocation.
/// </summary>
public delegate ValueTask<Result<TResponse>> HandlerDelegate<TRequest, TResponse>(TRequest request, CancellationToken ct)
    where TRequest : allows ref struct;

/// <summary>A cross-cutting concern that wraps handler execution in a pipeline.</summary>
public interface IBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, allows ref struct
{
    ValueTask<Result<TResponse>> Handle(TRequest request, HandlerDelegate<TRequest, TResponse> next, CancellationToken ct);
}

/// <summary>
/// Delegate representing the next step in a streaming pipeline chain.
/// The request is passed through so the pipeline can be pre-built once at startup.
/// </summary>
public delegate global::System.Collections.Generic.IAsyncEnumerable<TResponse> StreamHandlerDelegate<TRequest, TResponse>(TRequest request, CancellationToken ct);

/// <summary>A cross-cutting concern that wraps streaming handler execution.</summary>
public interface IStreamBehavior<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    global::System.Collections.Generic.IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TRequest, TResponse> next, global::System.Threading.CancellationToken ct);
}
