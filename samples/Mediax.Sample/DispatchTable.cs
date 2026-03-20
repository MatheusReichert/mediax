// This file is what Mediax.SourceGenerator would auto-generate.
// For the sample project we provide it manually so the project compiles without
// having the generator wired up as an Analyzer reference.
#pragma warning disable MA0047 // Declare type in a namespace — intentional: mirrors generated output
using System.Collections.Frozen;
using Mediax.Sample.Orders;
using Microsoft.Extensions.DependencyInjection;

internal static class DispatchTable
{
    internal static readonly FrozenDictionary<Type, Type> Handlers =
        new Dictionary<Type, Type>
        {
            [typeof(CreateOrderCommand)] = typeof(CreateOrderHandler),
            [typeof(GetOrderQuery)]      = typeof(GetOrderHandler),
            [typeof(OrderCreatedEvent)]  = typeof(OrderCreatedEventHandler),
        }.ToFrozenDictionary();

    internal static void RegisterAll(IServiceCollection services)
    {
        services.AddScoped<CreateOrderHandler>();
        services.AddScoped<GetOrderHandler>();
        services.AddScoped<OrderCreatedEventHandler>();
    }
}
