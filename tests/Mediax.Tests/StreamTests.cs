using Mediax.Core;
using Mediax.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Mediax.Tests;

public class StreamTests
{
    private readonly IServiceProvider _sp;

    public StreamTests()
    {
        _sp = TestServiceProvider.Create();
    }

    [Fact]
    public async Task Should_Stream_Results_From_Handler()
    {
        // Arrange
        var dispatcher = _sp.GetRequiredService<IMediaxDispatcher>();
        var request = new SampleStreamRequest(3);
        var results = new List<int>();

        // Act
        await foreach (var item in dispatcher.Stream(request, default))
        {
            results.Add(item);
        }

        // Assert
        results.Should().HaveCount(3);
        results.Should().ContainInOrder(0, 1, 2);
    }

    [Fact]
    public async Task Should_Stream_Results_Via_Static_Extension()
    {
        // Arrange
        Mediax.Runtime.MediaxRuntime.Init(_sp);
        var request = new SampleStreamRequest(2);
        var results = new List<int>();

        // Act
        await foreach (var item in request.Stream())
        {
            results.Add(item);
        }

        // Assert
        results.Should().HaveCount(2);
        results.Should().ContainInOrder(0, 1);
    }
}
