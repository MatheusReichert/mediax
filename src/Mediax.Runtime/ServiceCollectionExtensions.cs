using System.Reflection;
using Mediax.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Mediax.Runtime;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediax(
        this IServiceCollection services,
        Action<MediaxBuilder>? configure = null)
    {
        var builder = new MediaxBuilder(services);
        configure?.Invoke(builder);
        return services;
    }

    public static IServiceCollection AddMediax(
        this IServiceCollection services,
        IDictionary<Type, Type> handlerMap,
        Action<MediaxBuilder>? configure = null)
    {
        var builder = new MediaxBuilder(services);
        configure?.Invoke(builder);
        return services;
    }

    /// <summary>
    /// Scans the given assemblies and registers:
    /// <list type="bullet">
    ///   <item>All <c>IValidator&lt;T&gt;</c> (FluentValidation) implementations as <c>IValidator&lt;T&gt;</c>.</item>
    ///   <item>All <c>IBehavior&lt;TRequest, TResponse&gt;</c> implementations as open-generic or closed.</item>
    ///   <item>All <c>IRequestPreProcessor&lt;T&gt;</c> implementations.</item>
    ///   <item>All <c>IRequestPostProcessor&lt;T, TResponse&gt;</c> implementations.</item>
    ///   <item>All <c>IEventHandler&lt;TEvent&gt;</c> implementations (multi-subscriber pub/sub).</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddMediaxFromAssemblies(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;

                RegisterValidators(services, type);
                RegisterBehaviors(services, type);
                RegisterPreProcessors(services, type);
                RegisterPostProcessors(services, type);
                RegisterEventHandlers(services, type);
            }
        }

        return services;
    }

    /// <summary>
    /// Scans the calling assembly automatically.
    /// </summary>
    public static IServiceCollection AddMediaxFromCallingAssembly(this IServiceCollection services)
        => services.AddMediaxFromAssemblies(Assembly.GetCallingAssembly());

    // ── Private helpers ───────────────────────────────────────────────────────

    private static readonly Type _validatorOpenType = Type.GetType(
        "FluentValidation.IValidator`1, FluentValidation") ?? typeof(object);

    private static void RegisterValidators(IServiceCollection services, Type type)
    {
        if (_validatorOpenType == typeof(object)) return;

        foreach (var iface in type.GetInterfaces())
        {
            if (!iface.IsGenericType) continue;
            if (iface.GetGenericTypeDefinition() != _validatorOpenType) continue;
            // Register as IValidator<T> → concrete type (scoped per request)
            services.AddScoped(iface, type);
        }
    }

    private static readonly Type _behaviorOpen = typeof(IBehavior<,>);

    private static void RegisterBehaviors(IServiceCollection services, Type type)
    {
        foreach (var iface in type.GetInterfaces())
        {
            if (!iface.IsGenericType) continue;
            if (iface.GetGenericTypeDefinition() != _behaviorOpen) continue;
            services.AddScoped(iface, type);
        }
    }

    private static readonly Type _preProcessorOpen = typeof(IRequestPreProcessor<>);

    private static void RegisterPreProcessors(IServiceCollection services, Type type)
    {
        foreach (var iface in type.GetInterfaces())
        {
            if (!iface.IsGenericType) continue;
            if (iface.GetGenericTypeDefinition() != _preProcessorOpen) continue;
            services.AddScoped(iface, type);
        }
    }

    private static readonly Type _postProcessorOpen = typeof(IRequestPostProcessor<,>);

    private static void RegisterPostProcessors(IServiceCollection services, Type type)
    {
        foreach (var iface in type.GetInterfaces())
        {
            if (!iface.IsGenericType) continue;
            if (iface.GetGenericTypeDefinition() != _postProcessorOpen) continue;
            services.AddScoped(iface, type);
        }
    }

    private static readonly Type _eventHandlerOpen = typeof(IEventHandler<>);

    private static void RegisterEventHandlers(IServiceCollection services, Type type)
    {
        foreach (var iface in type.GetInterfaces())
        {
            if (!iface.IsGenericType) continue;
            if (iface.GetGenericTypeDefinition() != _eventHandlerOpen) continue;
            services.AddScoped(iface, type);
        }
    }
}
