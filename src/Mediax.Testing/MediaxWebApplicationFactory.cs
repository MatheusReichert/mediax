using Mediax.Core;
using Mediax.Runtime;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mediax.Testing;

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> that replaces the Mediax dispatcher
/// with a <see cref="FakeDispatcher"/> for integration tests.
/// <para>
/// Usage:
/// <code>
/// public class MyTests : MediaxWebApplicationFactory&lt;Program&gt;
/// {
///     [Fact]
///     public async Task MyTest()
///     {
///         Dispatcher.Returns&lt;MyQuery, MyResult&gt;(new MyResult("ok"));
///         var client = CreateClient();
///         var response = await client.GetAsync("/my-endpoint");
///         Assert.True(Dispatcher.WasDispatched&lt;MyQuery&gt;());
///     }
/// }
/// </code>
/// </para>
/// </summary>
public abstract class MediaxWebApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    /// <summary>The fake dispatcher wired into the application's DI container.</summary>
    public FakeDispatcher Dispatcher { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace the real IMediaxDispatcher with the fake
            for (int i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == typeof(IMediaxDispatcher))
                {
                    services.RemoveAt(i);
                    break;
                }
            }
            services.AddSingleton<IMediaxDispatcher>(Dispatcher);
        });

        IHost host = base.CreateHost(builder);

        // Wire the fake into the static accessor so .Send() calls resolve through it
        MediaxRuntime.UseTestDouble(Dispatcher);

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Dispatcher.Reset();
        base.Dispose(disposing);
    }
}
