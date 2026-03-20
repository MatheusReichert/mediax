// Mediax event handlers for benchmark — covers single-handler and multi-handler (pub/sub) scenarios.
using Mediax.Core;

namespace Mediax.Benchmarks.Handlers;

// ── Events ────────────────────────────────────────────────────────────────────

/// <summary>Event with a single [Handler] subscriber — baseline pub/sub cost.</summary>
public sealed record MediaxOrderCreatedEvent(int OrderId) : IEvent;

/// <summary>Event with three [Handler] subscribers — measures multi-subscriber fan-out.</summary>
public sealed record MediaxOrderShippedEvent(int OrderId) : IEvent;

// ── Single-subscriber event handlers ─────────────────────────────────────────

[Handler]
public sealed class MediaxOrderCreatedHandler : IHandler<MediaxOrderCreatedEvent, Unit>
{
    public ValueTask<Result<Unit>> Handle(MediaxOrderCreatedEvent request, CancellationToken ct)
        => ValueTask.FromResult(Result<Unit>.Ok(Unit.Value));
}

// ── Multi-subscriber event handlers (three handlers for the same event type) ──
// Each implements IEventHandler<T> so the generator registers all three.

[Handler]
public sealed class MediaxOrderShippedEmailHandler : IEventHandler<MediaxOrderShippedEvent>
{
    public ValueTask<Result<Unit>> Handle(MediaxOrderShippedEvent @event, CancellationToken ct)
        => ValueTask.FromResult(Result<Unit>.Ok(Unit.Value));
}

[Handler]
public sealed class MediaxOrderShippedSmsHandler : IEventHandler<MediaxOrderShippedEvent>
{
    public ValueTask<Result<Unit>> Handle(MediaxOrderShippedEvent @event, CancellationToken ct)
        => ValueTask.FromResult(Result<Unit>.Ok(Unit.Value));
}

[Handler]
public sealed class MediaxOrderShippedAuditHandler : IEventHandler<MediaxOrderShippedEvent>
{
    public ValueTask<Result<Unit>> Handle(MediaxOrderShippedEvent @event, CancellationToken ct)
        => ValueTask.FromResult(Result<Unit>.Ok(Unit.Value));
}

// ── MediatR notification (equivalent to IEvent, single subscriber) ────────────

public sealed class MediatROrderCreatedNotification : global::MediatR.INotification
{
    public int OrderId { get; init; }
}

public sealed class MediatROrderCreatedHandler
    : global::MediatR.INotificationHandler<MediatROrderCreatedNotification>
{
    public Task Handle(MediatROrderCreatedNotification notification, CancellationToken ct)
        => Task.CompletedTask;
}

// ── MediatR multi-subscriber notification ────────────────────────────────────

public sealed class MediatROrderShippedNotification : global::MediatR.INotification
{
    public int OrderId { get; init; }
}

public sealed class MediatROrderShippedEmailHandler
    : global::MediatR.INotificationHandler<MediatROrderShippedNotification>
{
    public Task Handle(MediatROrderShippedNotification notification, CancellationToken ct)
        => Task.CompletedTask;
}

public sealed class MediatROrderShippedSmsHandler
    : global::MediatR.INotificationHandler<MediatROrderShippedNotification>
{
    public Task Handle(MediatROrderShippedNotification notification, CancellationToken ct)
        => Task.CompletedTask;
}

public sealed class MediatROrderShippedAuditHandler
    : global::MediatR.INotificationHandler<MediatROrderShippedNotification>
{
    public Task Handle(MediatROrderShippedNotification notification, CancellationToken ct)
        => Task.CompletedTask;
}

// ── Mediator (martinothamar) notification ─────────────────────────────────────

public sealed record MediatorOrderCreatedNotification(int OrderId)
    : global::Mediator.INotification;

public sealed class MediatorOrderCreatedHandler
    : global::Mediator.INotificationHandler<MediatorOrderCreatedNotification>
{
    public ValueTask Handle(MediatorOrderCreatedNotification notification, CancellationToken ct)
        => ValueTask.CompletedTask;
}
