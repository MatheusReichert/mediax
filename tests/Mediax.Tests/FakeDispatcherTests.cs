using FluentAssertions;
using Mediax.Core;
using Mediax.Testing;
using Mediax.Tests.Fixtures;
using Xunit;

namespace Mediax.Tests;

public sealed class FakeDispatcherTests
{
    [Fact]
    public async Task FakeDispatcher_ReturnsDefaultForUnregisteredRequest()
    {
        var fake = new FakeDispatcher();
        var result = await fake.Dispatch(new EchoQuery("hi"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();  // default(string) is null
    }

    [Fact]
    public async Task FakeDispatcher_Returns_ConfiguredValue()
    {
        var fake = new FakeDispatcher()
            .Returns<EchoQuery, string>("configured response");

        var result = await fake.Dispatch(new EchoQuery("anything"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("configured response");
    }

    [Fact]
    public async Task FakeDispatcher_Returns_FactoryValue()
    {
        var fake = new FakeDispatcher()
            .Returns<EchoQuery, string>(req => $"echo:{req.Message}");

        var result = await fake.Dispatch(new EchoQuery("world"), CancellationToken.None);

        result.Value.Should().Be("echo:world");
    }

    [Fact]
    public async Task FakeDispatcher_Fails_ReturnsFailureResult()
    {
        var error = Error.NotFound("Q_NF");
        var fake = new FakeDispatcher()
            .Fails<EchoQuery, string>(error);

        var result = await fake.Dispatch(new EchoQuery("x"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public async Task FakeDispatcher_TracksDispatchedRequests()
    {
        var fake = new FakeDispatcher();
        await fake.Dispatch(new EchoQuery("a"), CancellationToken.None);
        await fake.Dispatch(new EchoQuery("b"), CancellationToken.None);

        fake.Dispatched.Should().HaveCount(2);
        fake.WasDispatched<EchoQuery>().Should().BeTrue();
        fake.WasDispatched<EchoQuery>(q => string.Equals(q.Message, "b", StringComparison.Ordinal)).Should().BeTrue();
        fake.WasDispatched<EchoQuery>(q => string.Equals(q.Message, "c", StringComparison.Ordinal)).Should().BeFalse();
    }

    [Fact]
    public async Task FakeDispatcher_Reset_ClearsState()
    {
        var fake = new FakeDispatcher()
            .Returns<EchoQuery, string>("before");

        await fake.Dispatch(new EchoQuery("x"), CancellationToken.None);
        fake.Reset();

        fake.Dispatched.Should().BeEmpty();
        var result = await fake.Dispatch(new EchoQuery("x"), CancellationToken.None);
        result.Value.Should().BeNull(); // responses cleared too
    }
}
