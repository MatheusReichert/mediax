using Mediax.Core;
using Mediax.Runtime;

namespace Mediax.Testing;

/// <summary>
/// Base class for tests that use <see cref="FakeDispatcher"/> with static dispatch
/// (i.e. when tests call request.Send() directly without DI).
/// </summary>
public abstract class MediaxTestBase : IDisposable
{
    protected FakeDispatcher Dispatcher { get; } = new();

    protected MediaxTestBase()
    {
        // Wire the fake into the static accessor so .Send() calls resolve through it
        MediaxRuntime.UseTestDouble(Dispatcher);
    }

    public void Dispose() => Dispatcher.Reset();
}
