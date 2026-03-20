using Mediax.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Runtime;

/// <summary>Fluent builder for configuring the Mediax pipeline during DI setup.</summary>
public sealed class MediaxBuilder
{
    internal IServiceCollection Services { get; }

    internal MediaxBuilder(IServiceCollection services) => Services = services;

    /// <summary>Registers a behavior that runs for every request in the pipeline.</summary>
    public MediaxBuilder UseGlobal(IPipeline pipeline)
    {
        if (pipeline is IServiceRegistrar registrar)
            registrar.Register(Services);
        return this;
    }

    /// <summary>Registers a global open-generic behavior.</summary>
    public MediaxBuilder UseGlobal(Type openBehaviorType)
    {
        Services.AddScoped(typeof(IBehavior<,>), openBehaviorType);
        return this;
    }

    /// <summary>Registers a global behavior by type parameter.</summary>
    public MediaxBuilder UseGlobal<TBehavior>() where TBehavior : class
    {
        Services.AddScoped(typeof(IBehavior<,>), typeof(TBehavior));
        return this;
    }

    /// <summary>Begins a targeted pipeline configuration for a category or specific request type.</summary>
    public MediaxCommandBuilder For<TCategory>()
        => new(Services, typeof(TCategory));
}

/// <summary>Configures behaviors for a specific request category or type.</summary>
public sealed class MediaxCommandBuilder
{
    private readonly IServiceCollection _services;
    private readonly Type _targetType;

    internal MediaxCommandBuilder(IServiceCollection services, Type targetType)
    {
        _services = services;
        _targetType = targetType;
    }

    public MediaxCommandBuilder UseBehavior<TBehavior>() where TBehavior : class
    {
        _services.AddScoped(typeof(IBehavior<,>), typeof(TBehavior));
        return this;
    }
}

/// <summary>Optional interface that allows a composed pipeline to register itself with DI.</summary>
public interface IServiceRegistrar
{
    void Register(IServiceCollection services);
}
