using Mediax.Core;

namespace Mediax.Sample.Orders;

[Handler]
public sealed class CreateOrderHandler : IHandler<CreateOrderCommand, Guid>
{
    public ValueTask<Result<Guid>> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        Console.WriteLine($"Creating order for customer {request.CustomerId}, amount {request.Amount}");
        return ValueTask.FromResult(Result<Guid>.Ok(id));
    }
}

[Handler]
public sealed class GetOrderHandler : IHandler<GetOrderQuery, OrderDto?>
{
    public ValueTask<Result<OrderDto?>> Handle(GetOrderQuery request, CancellationToken ct)
    {
        // In real code this would query a database
        OrderDto? order = null;
        return ValueTask.FromResult(Result<OrderDto?>.Ok(order));
    }
}

[Handler]
public sealed class OrderCreatedEventHandler : IHandler<OrderCreatedEvent, Unit>
{
    public ValueTask<Result<Unit>> Handle(OrderCreatedEvent request, CancellationToken ct)
    {
        Console.WriteLine($"Order created event received: OrderId={request.OrderId}");
        return ValueTask.FromResult(Result<Unit>.Ok(Unit.Value));
    }
}
