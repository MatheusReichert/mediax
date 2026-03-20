using Mediax.Core;
using Mediax.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Tests.Fixtures;

/// <summary>Builds a fully-wired DI container for integration-style tests.</summary>
public static class TestServiceProvider
{
    public static IServiceProvider Create(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Pass the test handler map to AddMediax
        services.AddMediax(DispatchTable.Handlers, configure: null);

        // Register the concrete handler types
        DispatchTable.RegisterAll(services);

        configure?.Invoke(services);

        var sp = services.BuildServiceProvider();
        MediaxRuntime.Init(sp);
        return sp;
    }
}
