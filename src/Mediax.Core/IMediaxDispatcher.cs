namespace Mediax.Core;

/// <summary>Abstraction used by test doubles and the runtime to dispatch requests.</summary>
public interface IMediaxDispatcher
{
    ValueTask<Result<T>> Dispatch<T>(IRequest<T> request, CancellationToken ct);

    ValueTask<Result<Unit>> Publish(IEvent @event, EventStrategy strategy, CancellationToken ct);

    global::System.Collections.Generic.IAsyncEnumerable<T> Stream<T>(global::Mediax.Core.IStreamRequest<T> request, global::System.Threading.CancellationToken ct);
}
