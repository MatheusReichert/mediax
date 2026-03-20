namespace Mediax.Core;

/// <summary>
/// Extension members for <see cref="IRequest{TResponse}"/> using C# 14 extension member syntax.
/// These provide a fluent API so callers can write <c>myRequest.Send()</c> instead of
/// <c>MediaxRuntime.Dispatch(myRequest)</c>.
/// </summary>
public static class MediaxExtensions
{
    extension<TResponse>(IRequest<TResponse> request)
    {
        /// <summary>Dispatches the request and awaits a single <see cref="Result{TResponse}"/>.</summary>
        public ValueTask<Result<TResponse>> Send(CancellationToken ct = default)
            => MediaxExtensions.DispatchRecursive(request, ct);

        /// <summary>Streams multiple responses for a request that supports server-sent sequences.</summary>
        public IAsyncEnumerable<TResponse> Stream(CancellationToken ct = default)
        {
            if (request is IStreamRequest<TResponse> streamRequest)
                return MediaxRuntimeAccessor.Stream(streamRequest, ct);
            throw new InvalidOperationException($"Type '{request.GetType().Name}' does not implement IStreamRequest<{typeof(TResponse).Name}>.");
        }

        /// <summary>
        /// Publishes an event (fire-and-forget semantics).
        /// Only meaningful when TResponse is <see cref="Unit"/> (i.e., the request is an <see cref="IEvent"/>).
        /// </summary>
        public ValueTask Publish(CancellationToken ct = default)
        {
            if (request is IRequest<Unit> unitRequest)
                return MediaxRuntimeAccessor.Publish(unitRequest, ct);
            throw new InvalidOperationException(
                $"Publish() can only be called on IEvent requests (IRequest<Unit>). " +
                $"Type '{request.GetType().Name}' returns '{typeof(TResponse).Name}'.");
        }

        /// <summary>Returns true when this request is a query (read-only).</summary>
        public bool IsQuery => request is IQuery<TResponse>;

        /// <summary>Returns true when this request is a command (write).</summary>
        public bool IsCommand => request is ICommand<TResponse>;

        /// <summary>Wraps the request with a timeout that cancels dispatch after <paramref name="timeout"/>.</summary>
        public IRequest<TResponse> WithTimeout(TimeSpan timeout)
            => new TimeoutDecorator<TResponse>(request, timeout);

        /// <summary>Wraps the request with automatic retry on transient failures.</summary>
        public IRequest<TResponse> WithRetry(int maxAttempts)
            => new RetryDecorator<TResponse>(request, maxAttempts);
    }

    private static ValueTask<Result<TResponse>> DispatchRecursive<TResponse>(IRequest<TResponse> request, CancellationToken ct)
    {
        if (request is TimeoutDecorator<TResponse> timeout)
            return DispatchWithTimeout(timeout.Inner, timeout.Timeout, ct);

        if (request is RetryDecorator<TResponse> retry)
            return DispatchWithRetry(retry.Inner, retry.MaxAttempts, ct);

        return MediaxRuntimeAccessor.Dispatcher.Dispatch(request, ct);
    }

    private static async ValueTask<Result<TResponse>> DispatchWithTimeout<TResponse>(
        IRequest<TResponse> inner, TimeSpan timeout, CancellationToken ct)
    {
        // Avoid linking when caller has no cancellation token — saves one allocation
        using var cts = ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : new CancellationTokenSource();
        cts.CancelAfter(timeout);
        return await DispatchRecursive(inner, cts.Token);
    }

    private static async ValueTask<Result<TResponse>> DispatchWithRetry<TResponse>(
        IRequest<TResponse> inner, int maxAttempts, CancellationToken ct)
    {
        Exception? last = null;
        for (int i = 0; i < maxAttempts; i++)
        {
            try { return await DispatchRecursive(inner, ct); }
            catch (Exception ex) when (i < maxAttempts - 1) { last = ex; }
        }
        throw last!;
    }

    extension(ReadOnlySpan<IEvent> events)
    {
        /// <summary>Publishes all events in the span sequentially.</summary>
        public ValueTask PublishBatch(CancellationToken ct = default)
            => MediaxRuntimeAccessor.DispatchBatch(events.ToArray(), ct);
    }
}
