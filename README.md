# Mediax

> CQRS Mediator for **.NET 10 + C# 14** — no reflection, no `IMediator` injection, zero allocations on the hot path.

[![CI](https://github.com/MatheusReichert/mediax/actions/workflows/ci.yml/badge.svg)](https://github.com/MatheusReichert/mediax/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Mediax.Core?label=NuGet)](https://www.nuget.org/packages/Mediax.Core)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-purple)

---

## Why Mediax?

| Problem with existing libraries | Mediax solution |
| --- | --- |
| Runtime reflection for handler discovery | Compile-time dispatch table (source generator) |
| `IMediator` injected into every consumer | Zero injection — `cmd.Send()` via C# 14 extension members |
| Runtime exception for missing handlers | Compile-time error `MX0001` |
| Boxing and allocations on the hot path | ~2–3 ns zero-alloc on the Singleton path |
| No native `Result<T>` | Native `Result<T>` with `Match`, `Map`, `Bind` |

---

## Installation

```xml
<!-- Handler discovery and dispatch table (required) -->
<PackageReference Include="Mediax.Core"            Version="0.1.0" />
<PackageReference Include="Mediax.SourceGenerator" Version="0.1.0"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<PackageReference Include="Mediax.Runtime"         Version="0.1.0" />

<!-- Ready-to-use behaviors (optional) -->
<PackageReference Include="Mediax.Behaviors"       Version="0.1.0" />

<!-- ASP.NET Core integration (optional) -->
<PackageReference Include="Mediax.AspNetCore"      Version="0.1.0" />

<!-- Testing utilities (optional) -->
<PackageReference Include="Mediax.Testing"         Version="0.1.0" />
```

---

## Quick Start

### 1. Define the request

```csharp
// Command (write)
public record CreateOrderCommand(IReadOnlyList<OrderItem> Items, CustomerId Customer)
    : ICommand<OrderId>;

// Query (read)
public record GetOrderQuery(OrderId Id) : IQuery<Order>;

// Event (pub/sub)
public record OrderCreatedEvent(OrderId OrderId) : IEvent;
```

### 2. Implement the handler

```csharp
[Handler]
public sealed class CreateOrderHandler : IHandler<CreateOrderCommand, OrderId>
{
    public CreateOrderHandler(IOrderRepository repo) => _repo = repo;

    public async ValueTask<Result<OrderId>> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Create(cmd.Items, cmd.Customer);
        await _repo.SaveAsync(order, ct);
        return Result<OrderId>.Ok(order.Id);
    }
}
```

### 3. Register and use

```csharp
// Program.cs
DispatchTable.RegisterAll(builder.Services);
app.UseMediax();

// Controller / Minimal API — no IMediator injected
var result = await new CreateOrderCommand(items, customerId).Send();

result.Match(
    ok    => Results.Created($"/orders/{ok}", ok),
    error => Results.Problem(error.Message));
```

---

## Request Interfaces

```csharp
IRequest<TResponse>       // base — use for generic requests
ICommand<TResponse>       // write semantics
IQuery<TResponse>         // read semantics
IEvent                    // pub/sub, returns Unit
IStreamRequest<TResponse> // streaming via IAsyncEnumerable
```

---

## Handlers

### Simple handler

```csharp
[Handler]
public sealed class EchoHandler : IHandler<EchoQuery, string>
{
    public ValueTask<Result<string>> Handle(EchoQuery q, CancellationToken ct)
        => ValueTask.FromResult(Result<string>.Ok(q.Text));
}
```

### Handler lifetime

The default is `Singleton`. Use `Scoped` for handlers with scoped dependencies:

```csharp
[Handler(Lifetime = HandlerLifetime.Scoped)]
public sealed class CreateOrderHandler : IHandler<CreateOrderCommand, OrderId>
{
    public CreateOrderHandler(AppDbContext db) => _db = db; // DbContext is Scoped
    // ...
}
```

| Lifetime | DI | When to use |
| --- | --- | --- |
| `Singleton` (default) | One global instance | Handler with no scoped dependencies — maximum performance |
| `Scoped` | One instance per HTTP request | Handler with `DbContext` or any scoped service |
| `Transient` | New instance per dispatch | Rare; use when the handler holds mutable per-call state |

### Streaming handler

```csharp
public record LivePricesQuery(string[] Tickers) : IStreamRequest<TickerPrice>;

[Handler]
public sealed class LivePricesHandler : IStreamHandler<LivePricesQuery, TickerPrice>
{
    public async IAsyncEnumerable<TickerPrice> Handle(
        LivePricesQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var price in _feed.Subscribe(q.Tickers, ct))
            yield return price;
    }
}

// Usage
await foreach (var price in new LivePricesQuery(tickers).Stream())
    Console.WriteLine(price);
```

---

## Events and Pub/Sub

### Single subscriber via `IHandler`

```csharp
[Handler]
public sealed class SendConfirmationHandler : IHandler<OrderCreatedEvent, Unit>
{
    public async ValueTask<Result<Unit>> Handle(OrderCreatedEvent e, CancellationToken ct)
    {
        await _email.SendAsync(e.OrderId, ct);
        return Result<Unit>.Ok(Unit.Value);
    }
}
```

### Multiple subscribers via `IEventHandler<T>`

Register as many handlers as you want for the same event — all will be invoked:

```csharp
[Handler]
public sealed class AuditHandler : IEventHandler<OrderCreatedEvent>
{
    public ValueTask<Result<Unit>> Handle(OrderCreatedEvent e, CancellationToken ct) { ... }
}

[Handler]
public sealed class AnalyticsHandler : IEventHandler<OrderCreatedEvent>
{
    public ValueTask<Result<Unit>> Handle(OrderCreatedEvent e, CancellationToken ct) { ... }
}
```

### Publish strategies

```csharp
// Sequential (default) — a failure stops the chain
await new OrderCreatedEvent(orderId).Publish(EventStrategy.Sequential, ct);

// Parallel — waits for all, returns first failure if any
await new OrderCreatedEvent(orderId).Publish(EventStrategy.ParallelWhenAll, ct);

// Fire-and-forget — does not wait, does not propagate failures
await new OrderCreatedEvent(orderId).Publish(EventStrategy.ParallelFireAndForget, ct);
```

### Event batch

```csharp
ReadOnlySpan<IEvent> events = [new OrderCreated(id1), new OrderCreated(id2)];
await events.PublishBatch(ct);
```

---

## Result\<T\>

`Result<T>` is a discriminated union — never throws on business failures:

```csharp
var result = await new GetOrderQuery(id).Send();

// Pattern matching
string message = result.Match(
    ok    => $"Order {ok.Id} found",
    error => $"Error: {error.Message}");

// Map / Bind
Result<string> label = result
    .Map(order => order.Status.ToString())
    .Bind(status => status == "Cancelled"
        ? Result<string>.Fail(Error.NotFound("ORDER_CANCELLED", "Order was cancelled"))
        : Result<string>.Ok(status));

// Check and access
if (result.IsSuccess)
    Console.WriteLine(result.Value);
else
    Console.WriteLine(result.Error!.Code);
```

### Error types

```csharp
Error.NotFound("ORDER_NOT_FOUND", "Order 42 does not exist")
Error.Validation("CMD.Validation", "Invalid input", detailsDictionary)
Error.Conflict("ORDER_DUPLICATE", "Order already exists")
Error.Internal("DB_TIMEOUT", "Database timeout")
Error.Unauthorized("AUTH_REQUIRED", "Must be authenticated")
Error.Forbidden("ROLE_REQUIRED", "Insufficient permissions")
```

---

## Pipeline Behaviors

Behaviors wrap handler execution. Applied via `[UseBehavior]` on the handler, or automatically via attributes like `[Validate]` and `[Cache]`.

### Custom behavior

```csharp
public sealed class MetricsBehavior<TRequest, TResponse> : IBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<Result<TResponse>> Handle(
        TRequest request, HandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = await next(request, ct);
        Metrics.Record(typeof(TRequest).Name, sw.ElapsedMilliseconds);
        return result;
    }
}

[Handler]
[UseBehavior(typeof(MetricsBehavior<,>))]
public sealed class CreateOrderHandler : IHandler<CreateOrderCommand, OrderId> { ... }
```

Multiple behaviors — declaration order = execution order (outermost first):

```csharp
[Handler]
[UseBehavior(typeof(LogBehavior<,>))]        // 1st (outermost)
[UseBehavior(typeof(ValidationBehavior<,>))] // 2nd
[UseBehavior(typeof(MetricsBehavior<,>))]    // 3rd (innermost)
public sealed class CreateOrderHandler : IHandler<CreateOrderCommand, OrderId> { ... }
// Execution: Log → Validation → Metrics → Handler
```

### Global behaviors (applied to all handlers)

```csharp
// AssemblyInfo.cs
[assembly: GlobalBehavior(typeof(LogBehavior<,>),     Order = -100)]
[assembly: GlobalBehavior(typeof(TracingBehavior<,>), Order = -50)]
```

### Behaviors included in `Mediax.Behaviors`

| Behavior | Registration | Description |
| --- | --- | --- |
| `LogBehavior<,>` | `[UseBehavior]` | Logs start, end and failures via `ILogger` |
| `TracingBehavior<,>` | `[UseBehavior]` | Creates OpenTelemetry spans per request |
| `ValidationBehavior<,>` | `[Validate]` on request | Runs all `IValidator<T>` (FluentValidation) |
| `CacheBehavior<,>` | `[Cache(Ttl = 60)]` on request | Caches response in `IDistributedCache` |
| `TransactionBehavior<,>` | `[UseBehavior]` | Opens/commits a `DbTransaction` per request |
| `IdempotencyBehavior<,>` | `[UseBehavior]` | Deduplicates requests by `IdempotencyKey` |
| `CircuitBreakerBehavior<,>` | `[UseBehavior]` | Circuit breaker per request type |
| `ProcessorBehavior<,>` | `[UseBehavior]` | Runs pre/post processors registered in DI |
| `PollyBehavior<,>` | `[UseBehavior]` | Polly v8 integration (retry/timeout/bulkhead) |

### Automatic validation with FluentValidation

```csharp
[Validate]
public record CreateOrderCommand(IReadOnlyList<OrderItem> Items) : ICommand<OrderId>;

public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("Order must have at least one item.");
    }
}
// Generator auto-injects ValidationBehavior — failures return Result.Fail(...), never throw
```

### Automatic caching

```csharp
[Cache(Ttl = 300)] // 5 minutes
public record GetProductQuery(ProductId Id) : IQuery<Product>;
// Requires IDistributedCache in DI
```

---

## Pre/Post Processors

Lightweight processors that run before/after the handler without creating a behavior per type:

```csharp
public sealed class AuditPreProcessor : IRequestPreProcessor<CreateOrderCommand>
{
    public ValueTask Process(CreateOrderCommand request, CancellationToken ct)
    {
        _audit.Log($"Incoming {nameof(CreateOrderCommand)}");
        return ValueTask.CompletedTask;
    }
}

public sealed class NotifyPostProcessor : IRequestPostProcessor<CreateOrderCommand, OrderId>
{
    public async ValueTask Process(CreateOrderCommand request, Result<OrderId> result, CancellationToken ct)
    {
        if (result.IsSuccess)
            await _notify.SendAsync($"Order {result.Value} created", ct);
    }
}

// Registration
services.AddScoped<IRequestPreProcessor<CreateOrderCommand>,           AuditPreProcessor>();
services.AddScoped<IRequestPostProcessor<CreateOrderCommand, OrderId>, NotifyPostProcessor>();

// Wire to the handler
[Handler]
[UseBehavior(typeof(ProcessorBehavior<,>))]
public sealed class CreateOrderHandler : IHandler<CreateOrderCommand, OrderId> { ... }
```

---

## Request Decorators

Timeout and retry without modifying the handler:

```csharp
var result = await new GetExternalDataQuery(id)
    .WithTimeout(TimeSpan.FromSeconds(2))
    .Send(ct);

var result = await new CallPaymentGatewayCommand(payload)
    .WithRetry(maxAttempts: 3)
    .Send(ct);

// Combined
var result = await new GetExternalDataQuery(id)
    .WithTimeout(TimeSpan.FromSeconds(5))
    .WithRetry(3)
    .Send(ct);
```

---

## Polymorphic Dispatch

When the request type is only known at runtime:

```csharp
public class RequestRouter(IMediaxDispatcher dispatcher)
{
    public async Task<object?> Route(IRequest<object> request, CancellationToken ct)
    {
        var result = await dispatcher.Dispatch(request, ct);
        return result.Value;
    }
}
```

The generated dispatcher uses a `switch` pattern match — no reflection, ~8 ns.

---

## Compiler Diagnostics

| Code | Severity | Description |
| --- | --- | --- |
| `MX0001` | Error | No handler found for a request type |
| `MX0002` | Error | Multiple `[Handler]` for the same non-event request type |
| `MX0003` | Warning | Behavior loop — same type declared twice in `[UseBehavior]` |
| `MX0004` | Warning | Captive dependency — Singleton handler with a behavior that receives `DbContext` |

---

## Auto-Discovery

```csharp
// Register from the calling assembly: IValidator<T>, IBehavior<,>, processors, IEventHandler<>
services.AddMediaxFromCallingAssembly();

// Or specify assemblies explicitly
services.AddMediaxFromAssemblies(
    typeof(Program).Assembly,
    typeof(SomeOtherHandler).Assembly);
```

---

## Testing

### Unit tests with `FakeDispatcher`

```csharp
var fake = new FakeDispatcher();
fake.Returns<CreateOrderCommand, OrderId>(cmd => new OrderId(Guid.NewGuid()));

var result = await fake.Dispatch(new CreateOrderCommand(items, customerId), default);

Assert.True(result.IsSuccess);
Assert.True(fake.WasDispatched<CreateOrderCommand>());
```

### `FakeDispatcher` API

```csharp
// Configure responses
fake.Returns<MyQuery, MyResult>(new MyResult("ok"));
fake.Returns<MyQuery, MyResult>(query => new MyResult(query.Id.ToString()));
fake.Fails<MyCommand, Unit>(Error.Internal("DB_ERROR", "timeout"));

// Streaming
fake.ReturnsStream<LiveQuery, Price>(new[] { price1, price2 });
fake.ReturnsStream<LiveQuery, Price>(req => GetPricesAsync(req.Tickers));

// Verification
bool dispatched  = fake.WasDispatched<MyCommand>();
bool withFilter  = fake.WasDispatched<MyCommand>(cmd => cmd.Id == 42);
int  count       = fake.DispatchCount<MyCommand>();
var  allRequests = fake.GetDispatched<MyCommand>();

fake.Reset(); // clear between tests
```

### Integration tests with `MediaxWebApplicationFactory`

```csharp
public class OrderEndpointTests : MediaxWebApplicationFactory<Program>
{
    [Fact]
    public async Task PostOrder_CallsCreateOrderCommand()
    {
        Dispatcher.Returns<CreateOrderCommand, OrderId>(new OrderId(Guid.NewGuid()));

        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/orders", new { items = new[] { ... } });

        response.EnsureSuccessStatusCode();
        Assert.True(Dispatcher.WasDispatched<CreateOrderCommand>());
    }
}
```

`MediaxWebApplicationFactory<TEntryPoint>` automatically replaces the real `IMediaxDispatcher` with `FakeDispatcher` — intercepting both interface calls and `.Send()`.

### Parallel test isolation (`MediaxTestBase`)

```csharp
public class MyTests : MediaxTestBase
{
    // AsyncLocal ensures each parallel test sees its own FakeDispatcher
    [Fact]
    public async Task Test1()
    {
        SetDispatcher(new FakeDispatcher());
        // ...
    }
}
```

---

## ASP.NET Core

```csharp
// Program.cs
DispatchTable.RegisterAll(builder.Services);
app.UseMediax(); // initializes MediaxRuntime and wires the dispatcher

// Minimal API — no IMediator injected
app.MapPost("/orders", async (CreateOrderCommand cmd, CancellationToken ct) =>
{
    var result = await cmd.Send(ct);
    return result.Match(
        ok    => Results.Created($"/orders/{ok}", ok),
        error => error.Type switch
        {
            ErrorType.Validation => Results.ValidationProblem(error.Details),
            ErrorType.NotFound   => Results.NotFound(error.Message),
            _                    => Results.Problem(error.Message)
        });
});
```

---

## Benchmarks

> **Environment:** BenchmarkDotNet v0.15.8 · .NET 10.0.3 · AMD Ryzen 5 8600G 4.35 GHz · Windows 11 25H2
> `Job=Short  WarmupCount=3  IterationCount=5`

### Basic dispatch (Singleton, no behaviors)

| Method | Mean | Alloc | vs MediatR |
| --- | ---: | ---: | ---: |
| **Mediax** `.Send()` | **2.6 ns** | **0 B** | **29×** |
| Mediator | 10.1 ns | 0 B | 7.7× |
| MediatR | 77.9 ns | 384 B | 1× |

### Dispatch with behavior (Singleton)

| Method | Mean | Alloc | vs MediatR |
| --- | ---: | ---: | ---: |
| **Mediax** | **3.3 ns** | **0 B** | **22×** |
| Mediator | 9.3 ns | 0 B | 8× |
| MediatR | 73.8 ns | 384 B | 1× |

### Polymorphic dispatch (`IRequest<T>` as variable)

| Method | Mean | Alloc |
| --- | ---: | ---: |
| Mediator | 8.3 ns | 0 B |
| **Mediax** | **8.8 ns** | **0 B** |
| MediatR | 43.6 ns | 200 B |

### Scoped handler (creates `IServiceScope` per call)

| Method | Mean | Alloc |
| --- | ---: | ---: |
| Mediator | 9.8 ns | 0 B |
| **Mediax** | 71 ns | 336 B |
| MediatR | 74.6 ns | 384 B |

> Mediator resolves Scoped handlers without `IServiceScope`; this optimization is not yet in Mediax.

### Pre/Post Processors

| Method | Mean | Alloc |
| --- | ---: | ---: |
| Mediator | 20.8 ns | 0 B |
| **Mediax** | **32 ns** | **0 B** |
| MediatR | 84.6 ns | 456 B |

### Events — single subscriber

| Method | Mean | Alloc | vs MediatR |
| --- | ---: | ---: | ---: |
| Mediator | 7.8 ns | 0 B | 22× |
| **Mediax Sequential** | **38.7 ns** | **0 B** | **4.5×** |
| MediatR | 175 ns | 768 B | 1× |

### Events — 3 subscribers (sequential)

| Method | Mean | Alloc | vs MediatR |
| --- | ---: | ---: | ---: |
| **Mediax Sequential** | **130 ns** | **0 B** | **1.76×** |
| MediatR | 229 ns | 1248 B | 1× |

### Events — 3 subscribers (parallel WhenAll)

| Method | Mean | Alloc | vs MediatR |
| --- | ---: | ---: | ---: |
| **Mediax WhenAll** | **168 ns** | **496 B** | **1.43×** |
| MediatR | 240 ns | 1248 B | 1× |

### Decorators

| Method | Mean | Alloc |
| --- | ---: | ---: |
| Mediax baseline | 2.6 ns | 0 B |
| Mediax `.WithRetry(1)` | 29 ns | **0 B** |
| MediatR raw | 43 ns | 200 B |
| Mediax `.WithTimeout(5s)` | 76 ns | 144 B |

### Streaming

| Count | Mean | Alloc |
| --- | ---: | ---: |
| 1 item | 566 ns | 344 B |
| 10 items | 3.7 µs | 344 B |

### Running the benchmarks

```bash
cd benchmarks/Mediax.Benchmarks
dotnet run -c Release                               # all
dotnet run -c Release -- --filter "*Event*"         # events only
dotnet run -c Release -- --filter "*Polymorphic*"   # polymorphic dispatch
dotnet run -c Release -- --filter "*Decorator*"     # timeout / retry
dotnet run -c Release -- --filter "*Processor*"     # pre/post processors
dotnet run -c Release -- --list flat                # list all benchmarks
```

---

## Architecture — 3 Generated Files per Build

### `DispatchTable.g.cs`

`FrozenDictionary<Type, Type>` + `RegisterAll()` that registers all handlers and `MediaxDispatcher` in DI.

### `MediaxDispatcher.g.cs`

`internal sealed class MediaxDispatcher : IMediaxDispatcher` with type pattern matching switch. Used for polymorphic dispatch and as fallback.

### `MediaxStaticDispatch.g.cs`

The performance core. Contains:

- `MediaxStaticHandlers` with a `volatile MediaxDispatcher? _dispatcher` field populated via `[ModuleInitializer]`
- **Per-type extension members** (C# 14) — one `extension(ConcreteType)` block per handler

```csharp
// Generated code — zero boxing, zero switch, zero dictionary lookup
extension(MyApp.CreateOrderCommand request)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<Result<OrderId>> Send(CancellationToken ct = default)
        => MediaxStaticHandlers._dispatcher is { } d
            ? d.Dispatch_CreateOrderHandler(request, ct)   // ~2-3 ns
            : MediaxRuntimeAccessor.Dispatcher.Dispatch(request, ct);
}
```

C# 14 resolves `extension(CreateOrderCommand)` with higher priority than `extension<T>(IRequest<T>)` when the receiver is statically typed — `cmd.Send()` compiles to a direct call on the singleton dispatcher.

---

## Package Structure

```
Mediax.Core            Interfaces, Result<T>, Error, extension members, decorators
Mediax.SourceGenerator Roslyn generator: [Handler], 3 generated files, MX0001–MX0004
Mediax.Runtime         MediaxRuntime, MediaxStartupHooks, DI extensions
Mediax.Behaviors       Log, Tracing, Validation, Cache, Transaction,
                       Idempotency, CircuitBreaker, Processor, Polly
Mediax.AspNetCore      app.UseMediax(), IResult helpers
Mediax.Testing         FakeDispatcher, MediaxTestBase, MediaxWebApplicationFactory
```

---

## Contributing

```bash
git clone https://github.com/MatheusReichert/mediax
cd mediax
dotnet build Mediax.slnx
dotnet test  Mediax.slnx
```

PRs are welcome. Please open an issue before large changes.

---

## Roadmap

| Version | Scope |
| --- | --- |
| **v0.1** Alpha | IRequest, [Handler], source generator, .Send(), Result\<T\>, behaviors, events, streaming ✅ |
| **v0.2** Beta | Mediax.Aspire — .NET Aspire 13 integration |
| **v1.0** | Frozen API, full documentation, published benchmarks, .NET 10 LTS |

---

## License

MIT © Mediax Contributors
