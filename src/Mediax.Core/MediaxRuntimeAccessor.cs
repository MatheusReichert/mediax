namespace Mediax.Core;

/// <summary>
/// Static accessor used by extension members in Core to reach the runtime dispatcher
/// without creating a circular project reference. The runtime project sets this during startup.
/// </summary>
public static class MediaxRuntimeAccessor
{
    private static IMediaxDispatcher? _dispatcher;
    private static Func<IRequest<Unit>, CancellationToken, ValueTask>? _publishFunc;
    private static Func<IEvent[], CancellationToken, ValueTask>? _batchPublishFunc;

    /// <summary>
    /// Fast-path boolean to avoid AsyncLocal overhead in production.
    /// </summary>
    public static bool IsTestMode { get; set; }

    /// <summary>
    /// Per-async-context override used by test doubles.
    /// When set, it takes precedence over the global <see cref="_dispatcher"/> for the current
    /// async call chain only — allowing parallel xUnit tests to each use their own fake.
    /// Production code never sets this; it remains null and adds zero cost to the hot path.
    /// </summary>
    public static readonly AsyncLocal<IMediaxDispatcher?> _testOverride = new();

    public static IMediaxDispatcher Dispatcher
        => _testOverride.Value
           ?? _dispatcher
           ?? throw new InvalidOperationException(
               "Mediax runtime has not been initialized. Call app.UseMediax() or MediaxRuntime.Init(serviceProvider).");

    public static void SetDispatcher(IMediaxDispatcher dispatcher)
        => _dispatcher = dispatcher;

    public static void SetPublishFunc(Func<IRequest<Unit>, CancellationToken, ValueTask> func)
        => _publishFunc = func;

    public static void SetBatchPublishFunc(Func<IEvent[], CancellationToken, ValueTask> func)
        => _batchPublishFunc = func;
    
    public static global::System.Collections.Generic.IAsyncEnumerable<T> Stream<T>(
        global::Mediax.Core.IStreamRequest<T> request, global::System.Threading.CancellationToken ct)
        => Dispatcher.Stream(request, ct);

    internal static ValueTask Publish(IRequest<Unit> @event, CancellationToken ct)
    {
        if (_publishFunc != null)
            return _publishFunc(@event, ct);

        // Fallback: dispatch and discard the result
        var task = Dispatcher.Dispatch(@event, ct).AsTask().ContinueWith(static _ => { });
        return new ValueTask(task);
    }

    internal static async ValueTask DispatchBatch(IEvent[] events, CancellationToken ct)
    {
        if (_batchPublishFunc != null)
        {
            await _batchPublishFunc(events, ct);
            return;
        }
        foreach (var e in events)
            await Publish(e, ct);
    }
}
