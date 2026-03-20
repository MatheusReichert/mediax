namespace Mediax.Core;

/// <summary>Base marker interface for all request types.</summary>
public interface IRequest<out TResponse> { }

/// <summary>Semantic marker for command requests (write operations).</summary>
public interface ICommand<out TResponse> : IRequest<TResponse> { }

/// <summary>Semantic marker for query requests (read operations).</summary>
public interface IQuery<out TResponse> : IRequest<TResponse> { }

/// <summary>Semantic marker for domain events.</summary>
public interface IEvent : IRequest<Unit> { }

/// <summary>Semantic marker for streaming requests.</summary>
public interface IStreamRequest<out TResponse> : IRequest<TResponse> { }

/// <summary>
/// Handles a domain event. Multiple <see cref="IEventHandler{TEvent}"/> implementations
/// can be registered for the same event type — all will be invoked during Publish.
/// Prefer this over <see cref="IHandler{TRequest,TResponse}"/> for event subscribers.
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    ValueTask<Result<Unit>> Handle(TEvent @event, CancellationToken ct);
}
