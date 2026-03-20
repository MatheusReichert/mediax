using FluentAssertions;
using Mediax.Core;
using Mediax.Runtime;
using Mediax.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mediax.Tests;

public sealed class EventTests
{
    private static IServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediax(DispatchTable.Handlers);
        DispatchTable.RegisterAll(services);
        var sp = services.BuildServiceProvider();
        MediaxRuntime.Init(sp);
        return sp;
    }

    [Fact]
    public async Task Event_Publish_CallsAllHandlers()
    {
        BuildSp();
        SampleEventHandler.ReceivedPayloads.Clear();

        var evt = new SampleEvent("event-test");
        var result = await evt.Publish(EventStrategy.Sequential, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        SampleEventHandler.ReceivedPayloads.Should().ContainSingle("event-test");
    }
}
