using FluentAssertions;
using Mediax.Core;
using Mediax.Runtime;
using Mediax.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mediax.Tests;

/// <summary>Integration tests for the full dispatch pipeline.</summary>
public sealed class DispatchTests : IDisposable
{
    private readonly IServiceProvider _sp;

    public DispatchTests()
    {
        _sp = TestServiceProvider.Create();
    }

    public void Dispose() { }

    [Fact]
    public async Task Dispatch_Query_ReturnsEchoedMessage()
    {
        var query = new EchoQuery("hello world");
        var result = await query.Send(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello world");
    }

    [Fact]
    public async Task Dispatch_Command_ReturnsSum()
    {
        var cmd = new AddNumbersCommand(3, 4);
        var result = await cmd.Send(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(7);
    }

    [Fact]
    public async Task Dispatch_FailingCommand_ReturnsFailResult()
    {
        var cmd = new FailingCommand("intentional failure");
        var result = await cmd.Send(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("TEST_FAIL");
        result.Error.Message.Should().Be("intentional failure");
    }

    [Fact]
    public async Task Dispatch_UnknownRequest_ThrowsKeyNotFoundException()
    {
        var unknownRequest = new UnregisteredRequest();

#pragma warning disable MX0001 // suppress analyzer for test
        var result = await unknownRequest.Send(CancellationToken.None);
#pragma warning restore MX0001

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("MX_NO_HANDLER");
    }

    [Fact]
    public void Query_IsQuery_ReturnsTrue()
    {
        IRequest<string> query = new EchoQuery("test");
        query.IsQuery.Should().BeTrue();
        query.IsCommand.Should().BeFalse();
    }

    [Fact]
    public void Command_IsCommand_ReturnsTrue()
    {
        IRequest<int> cmd = new AddNumbersCommand(1, 2);
        cmd.IsCommand.Should().BeTrue();
        cmd.IsQuery.Should().BeFalse();
    }
}

// A request with no registered handler — used to test the KeyNotFoundException path
internal sealed record UnregisteredRequest : IQuery<string>;
