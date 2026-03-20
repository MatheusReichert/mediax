using Mediax.Core;

namespace Mediax.Sample.Orders;

public sealed record CreateOrderCommand(string CustomerId, decimal Amount) : ICommand<Guid>;

public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto?>;

public sealed record OrderDto(Guid Id, string CustomerId, decimal Amount, DateTime CreatedAt);

public sealed record OrderCreatedEvent(Guid OrderId, string CustomerId) : IEvent;
