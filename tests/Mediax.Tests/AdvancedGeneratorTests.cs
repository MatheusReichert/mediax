using FluentAssertions;
using Mediax.Core;
using Mediax.Runtime;
using Mediax.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Mediax.Testing;

namespace Mediax.Tests;

/// <summary>
/// Creative integration tests ensuring advanced Source Generator behavior,
/// including AsyncLocal testability and Scope lifecycle management.
/// </summary>
public sealed class AdvancedGeneratorTests : IDisposable
{
    private readonly IServiceProvider _sp;

    public AdvancedGeneratorTests()
    {
        _sp = TestServiceProvider.Create(services =>
        {
            services.AddScoped<DisposableTracker>();
        });
    }

    public void Dispose()
    {
        // Revert any TestDouble overriding from MediaxRuntime
        MediaxRuntimeAccessor.IsTestMode = false;
        MediaxRuntimeAccessor._testOverride.Value = null;
    }

    [Fact]
    public async Task AsyncLocal_ParallelTests_DoNotInterfere()
    {
        // Act: We spin up two concurrent execution contexts.
        // Each context installs its own TestDouble.
        // Since it uses AsyncLocal, they should not overwrite each other.

        var task1 = Task.Run(async () =>
        {
            var fake1 = new FakeDispatcher();
            fake1.Returns<EchoQuery, string>("response_1");
            MediaxRuntime.UseTestDouble(fake1);

            // Wait a bit to ensure concurrency overlap
            await Task.Delay(50);
            
            var result = await new EchoQuery("test").Send();
            return result.Value;
        });

        var task2 = Task.Run(async () =>
        {
            var fake2 = new FakeDispatcher();
            fake2.Returns<EchoQuery, string>("response_2");
            MediaxRuntime.UseTestDouble(fake2);

            // Wait a bit to ensure concurrency overlap
            await Task.Delay(50);

            var result = await new EchoQuery("test").Send();
            return result.Value;
        });

        var results = await Task.WhenAll(task1, task2);

        // Assert: Both got their respective injected responses,
        // proving MediaxRuntimeAccessor._testOverride.Value is truly local to the async flow.
        results[0].Should().Be("response_1");
        results[1].Should().Be("response_2");
    }

    [Fact]
    public async Task ScopedHandler_UsingStatement_DisposesAfterSend()
    {
        // Arrange
        var req = new TrackedScopedRequest("data");
        
        // Assert state before
        DisposableTracker.DisposedInstances.Should().Be(0);

        // Act
        var result = await req.Send();

        // Assert state after
        // Because the generated Dispatch_X method for Scoped handlers uses:
        // "using var scope = _sp.CreateScope();"
        // The embedded DisposableTracker should be finalized right after the Send() awaited method completes.
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("processed_data");
        
        DisposableTracker.DisposedInstances.Should().BeGreaterThan(0);
        // Reset for other tests
        DisposableTracker.Reset();
    }
}

// ── Test Artifacts ────────────────────────────────────────────────────────────

// A simple tracking class to verify IDisposable gets fired by the GC / Scope.Dispose()
public sealed class DisposableTracker : IDisposable
{
    private static int _disposedInstances;

    public static int DisposedInstances => _disposedInstances;
    public static void Reset() => Interlocked.Exchange(ref _disposedInstances, 0);

    public void Dispose()
    {
        Interlocked.Increment(ref _disposedInstances);
    }
}

public sealed record TrackedScopedRequest(string Input) : IQuery<string>;

// Register as Scoped (Lifetime = 1).
// The Source Generator will emit a Dispatch method with "using var scope = _sp.CreateScope()".
[Handler(Lifetime = HandlerLifetime.Scoped)]
public sealed class TrackedScopedHandler : IHandler<TrackedScopedRequest, string>
{
    private readonly DisposableTracker _tracker;

    public TrackedScopedHandler(DisposableTracker tracker)
    {
        _tracker = tracker; // Injection forces the DI to create it in the new scope via ServiceProvider.GetRequiredService<TrackedScopedHandler>()
    }

    public ValueTask<Result<string>> Handle(TrackedScopedRequest request, CancellationToken ct)
    {
        return new ValueTask<Result<string>>(Result<string>.Ok("processed_" + request.Input));
    }
}
