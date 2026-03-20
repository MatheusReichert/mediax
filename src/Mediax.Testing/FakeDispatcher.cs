using Mediax.Core;

namespace Mediax.Testing;

/// <summary>
/// An in-memory <see cref="IMediaxDispatcher"/> for unit tests.
/// Returns configurable responses without requiring a real DI container.
/// </summary>
public sealed class FakeDispatcher : IMediaxDispatcher
{
    private readonly Dictionary<Type, Delegate> _responses = new();
    private readonly Dictionary<Type, Func<object, IAsyncEnumerable<object>>> _streamResponses = new();
    private readonly List<object> _dispatched = new();

    /// <summary>All requests dispatched through this fake (Send, Publish, Stream).</summary>
    public IReadOnlyList<object> Dispatched => _dispatched;

    // ── Dispatch ──────────────────────────────────────────────────────────────

    public ValueTask<Result<T>> Dispatch<T>(IRequest<T> request, CancellationToken ct)
    {
        _dispatched.Add(request);

        if (_responses.TryGetValue(request.GetType(), out Delegate? factory))
        {
            object result = factory.DynamicInvoke(request)!;
            return ValueTask.FromResult((Result<T>)result);
        }

        return ValueTask.FromResult(Result<T>.Ok(default(T)!));
    }

    // ── Publish ───────────────────────────────────────────────────────────────

    public ValueTask<Result<Unit>> Publish(IEvent @event, EventStrategy strategy, CancellationToken ct)
    {
        _dispatched.Add(@event);

        if (_responses.TryGetValue(@event.GetType(), out Delegate? factory))
        {
            object result = factory.DynamicInvoke(@event)!;
            return ValueTask.FromResult((Result<Unit>)result);
        }

        return ValueTask.FromResult(Result<Unit>.Ok(Unit.Value));
    }

    // ── Stream ────────────────────────────────────────────────────────────────

    public IAsyncEnumerable<T> Stream<T>(IStreamRequest<T> request, CancellationToken ct)
    {
        _dispatched.Add(request);

        if (_streamResponses.TryGetValue(request.GetType(), out Func<object, IAsyncEnumerable<object>>? factory))
            return (IAsyncEnumerable<T>)factory(request);

        return System.Linq.AsyncEnumerable.Empty<T>();
    }

    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>Configures a fixed successful response for requests of type <typeparamref name="TRequest"/>.</summary>
    public FakeDispatcher Returns<TRequest, TResponse>(TResponse value)
        where TRequest : IRequest<TResponse>
    {
        _responses[typeof(TRequest)] = (TRequest _) => Result<TResponse>.Ok(value);
        return this;
    }

    /// <summary>Configures a factory that produces the response given the request instance.</summary>
    public FakeDispatcher Returns<TRequest, TResponse>(Func<TRequest, TResponse> factory)
        where TRequest : IRequest<TResponse>
    {
        _responses[typeof(TRequest)] = (TRequest req) => Result<TResponse>.Ok(factory(req));
        return this;
    }

    /// <summary>Configures a failure result for requests of type <typeparamref name="TRequest"/>.</summary>
    public FakeDispatcher Fails<TRequest, TResponse>(Error error)
        where TRequest : IRequest<TResponse>
    {
        _responses[typeof(TRequest)] = (TRequest _) => Result<TResponse>.Fail(error);
        return this;
    }

    /// <summary>Configures an async-enumerable stream response for a streaming request.</summary>
    public FakeDispatcher ReturnsStream<TRequest, TResponse>(IEnumerable<TResponse> items)
        where TRequest : IStreamRequest<TResponse>
    {
        _streamResponses[typeof(TRequest)] = _ => (IAsyncEnumerable<object>)items.ToAsyncEnumerable();
        return this;
    }

    /// <summary>Configures a factory that produces the stream given the request instance.</summary>
    public FakeDispatcher ReturnsStream<TRequest, TResponse>(Func<TRequest, IAsyncEnumerable<TResponse>> factory)
        where TRequest : IStreamRequest<TResponse>
    {
        _streamResponses[typeof(TRequest)] = req => (IAsyncEnumerable<object>)factory((TRequest)req);
        return this;
    }

    // ── Verification ──────────────────────────────────────────────────────────

    /// <summary>Returns true if a request of the given type was dispatched at least once.</summary>
    public bool WasDispatched<TRequest>()
        => _dispatched.OfType<TRequest>().Any();

    /// <summary>Returns true if a request matching the predicate was dispatched.</summary>
    public bool WasDispatched<TRequest>(Func<TRequest, bool> predicate)
        => _dispatched.OfType<TRequest>().Any(predicate);

    /// <summary>Returns the number of times a request of the given type was dispatched.</summary>
    public int DispatchCount<TRequest>()
        => _dispatched.OfType<TRequest>().Count();

    /// <summary>Returns the number of times a request matching the predicate was dispatched.</summary>
    public int DispatchCount<TRequest>(Func<TRequest, bool> predicate)
        => _dispatched.OfType<TRequest>().Count(predicate);

    /// <summary>Returns all dispatched requests of type <typeparamref name="TRequest"/>.</summary>
    public IReadOnlyList<TRequest> GetDispatched<TRequest>()
        => _dispatched.OfType<TRequest>().ToList();

    /// <summary>Clears all dispatched records and configured responses.</summary>
    public void Reset()
    {
        _dispatched.Clear();
        _responses.Clear();
        _streamResponses.Clear();
    }
}
