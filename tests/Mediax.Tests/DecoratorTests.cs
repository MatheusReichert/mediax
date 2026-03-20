using FluentAssertions;
using Mediax.Core;
using Mediax.Runtime;
using Mediax.Tests.Fixtures;
using Xunit;

namespace Mediax.Tests;

public sealed class DecoratorTests
{
    public DecoratorTests()
    {
        TestServiceProvider.Create();
    }

    [Fact]
    public void WithTimeout_WrapsRequest_InTimeoutDecorator()
    {
        IRequest<string> query = new EchoQuery("test");
        var withTimeout = query.WithTimeout(TimeSpan.FromSeconds(5));

        withTimeout.Should().BeOfType<TimeoutDecorator<string>>();
    }

    [Fact]
    public void WithRetry_WrapsRequest_InRetryDecorator()
    {
        IRequest<string> query = new EchoQuery("test");
        var withRetry = query.WithRetry(3);

        withRetry.Should().BeOfType<RetryDecorator<string>>();
    }

    [Fact]
    public async Task WithTimeout_DispatchesInnerRequest()
    {
        IRequest<string> query = new EchoQuery("timed");
        var wrapped = query.WithTimeout(TimeSpan.FromSeconds(10));
        var result = await wrapped.Send(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("timed");
    }

    [Fact]
    public async Task WithRetry_DispatchesInnerRequest()
    {
        IRequest<int> cmd = new AddNumbersCommand(10, 20);
        var wrapped = cmd.WithRetry(2);
        var result = await wrapped.Send(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(30);
    }
}
