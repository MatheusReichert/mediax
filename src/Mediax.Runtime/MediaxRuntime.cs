using Mediax.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Runtime;

/// <summary>
/// Central runtime that manages the service provider and generated dispatchers.
/// Initialize by calling <see cref="Init(IServiceProvider)"/> (typically via <c>app.UseMediax()</c>).
/// </summary>
public static class MediaxRuntime
{
    private static IServiceProvider? _serviceProvider;
    private static IMediaxDispatcher? _testDouble;

    public static void Init(IServiceProvider sp)
    {
        _serviceProvider = sp;
        var dispatcher = sp.GetService<IMediaxDispatcher>();
        _testDouble = null;

        if (dispatcher != null)
        {
            MediaxRuntimeAccessor.SetDispatcher(dispatcher);
            MediaxRuntimeAccessor.SetPublishFunc(async (evt, ct) => 
            {
                if (evt is IEvent e) await dispatcher.Publish(e, EventStrategy.Sequential, ct);
                else await dispatcher.Dispatch(evt, ct);
            });
            MediaxRuntimeAccessor.SetBatchPublishFunc(async (events, ct) =>
            {
                foreach (var e in events)
                    await dispatcher.Publish(e, EventStrategy.Sequential, ct);
            });
        }

        // Run all hooks registered by generated [ModuleInitializer] code
        MediaxStartupHooks.RunAll(sp);
    }

    /// <summary>
    /// Installs a per-async-context test double for the current async call chain.
    /// Safe for parallel xUnit tests: each test's async context gets its own dispatcher
    /// without touching the global static fields used by production code.
    /// </summary>
    public static void UseTestDouble(IMediaxDispatcher dispatcher)
    {
        MediaxRuntimeAccessor.IsTestMode = true;
        _testDouble = dispatcher;
        MediaxRuntimeAccessor._testOverride.Value = dispatcher;
    }
}
