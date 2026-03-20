# Mediax

> Mediator CQRS para **.NET 10 + C# 14** — zero reflexão, zero injeção de `IMediator`, zero alocações no hot path.

[![CI](https://github.com/MatheusReichert/mediax/actions/workflows/ci.yml/badge.svg)](https://github.com/MatheusReichert/mediax/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Mediax.Core?label=NuGet)](https://www.nuget.org/packages/Mediax.Core)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-purple)

---

## Por que Mediax?

| Problema nas bibliotecas atuais | Solução Mediax |
| --- | --- |
| Reflexão em runtime para descoberta de handlers | Dispatch table gerada em compile-time (source generator) |
| `IMediator` injetado em todo consumer | Zero injeção — `cmd.Send()` via C# 14 extension members |
| Exceção em runtime por handler não registrado | Erro de compilação `MX0001` |
| Boxing e alocações no hot path | ~2–3 ns zero-alloc no path Singleton |
| Sem `Result<T>` nativo | `Result<T>` nativo com `Match`, `Map`, `Bind` |

---

## Instalação

```xml
<!-- Handler discovery e dispatch table (obrigatório) -->
<PackageReference Include="Mediax.Core"            Version="0.1.0" />
<PackageReference Include="Mediax.SourceGenerator" Version="0.1.0"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<PackageReference Include="Mediax.Runtime"         Version="0.1.0" />

<!-- Behaviors prontos (opcional) -->
<PackageReference Include="Mediax.Behaviors"       Version="0.1.0" />

<!-- ASP.NET Core integration (opcional) -->
<PackageReference Include="Mediax.AspNetCore"      Version="0.1.0" />

<!-- Testes (opcional) -->
<PackageReference Include="Mediax.Testing"         Version="0.1.0" />
```

---

## Início rápido

### 1. Definir o request

```csharp
// Comando (write)
public record CreateOrderCommand(IReadOnlyList<OrderItem> Items, CustomerId Customer)
    : ICommand<OrderId>;

// Query (read)
public record GetOrderQuery(OrderId Id) : IQuery<Order>;

// Evento (pub/sub)
public record OrderCreatedEvent(OrderId OrderId) : IEvent;
```

### 2. Implementar o handler

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

### 3. Registrar e usar

```csharp
// Program.cs
DispatchTable.RegisterAll(builder.Services);
app.UseMediax();

// Controller / Minimal API — zero IMediator injetado
var result = await new CreateOrderCommand(items, customerId).Send();

result.Match(
    ok    => Results.Created($"/orders/{ok}", ok),
    error => Results.Problem(error.Message));
```

---

## Interfaces de request

```csharp
IRequest<TResponse>       // base — use para requests genéricos
ICommand<TResponse>       // semântica de escrita
IQuery<TResponse>         // semântica de leitura
IEvent                    // pub/sub, resposta Unit
IStreamRequest<TResponse> // streaming IAsyncEnumerable
```

---

## Handlers

### Handler simples

```csharp
[Handler]
public sealed class EchoHandler : IHandler<EchoQuery, string>
{
    public ValueTask<Result<string>> Handle(EchoQuery q, CancellationToken ct)
        => ValueTask.FromResult(Result<string>.Ok(q.Text));
}
```

### Lifetime do handler

O padrão é `Singleton`. Para handlers com dependências escopadas:

```csharp
[Handler(Lifetime = HandlerLifetime.Scoped)]
public sealed class CreateOrderHandler : IHandler<CreateOrderCommand, OrderId>
{
    public CreateOrderHandler(AppDbContext db) => _db = db; // DbContext é Scoped
    // ...
}
```

| Lifetime | DI | Quando usar |
| --- | --- | --- |
| `Singleton` (padrão) | Uma instância global | Handler sem dependências escopadas — máxima performance |
| `Scoped` | Uma instância por request HTTP | Handler com `DbContext` ou qualquer serviço Scoped |
| `Transient` | Nova instância por dispatch | Raro; use quando o handler tem estado mutável por chamada |

### Handler de streaming

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

// Uso
await foreach (var price in new LivePricesQuery(tickers).Stream())
    Console.WriteLine(price);
```

---

## Eventos e pub/sub

### Handler único via `IHandler`

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

### Múltiplos subscribers via `IEventHandler<T>`

Registre quantos handlers quiser para o mesmo evento — todos serão invocados:

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

### Estratégias de publicação

```csharp
// Sequencial (padrão) — falha interrompe a cadeia
await new OrderCreatedEvent(orderId).Publish(EventStrategy.Sequential, ct);

// Paralelo — aguarda todos, retorna primeira falha se houver
await new OrderCreatedEvent(orderId).Publish(EventStrategy.ParallelWhenAll, ct);

// Fire-and-forget — não aguarda, não propaga falhas
await new OrderCreatedEvent(orderId).Publish(EventStrategy.ParallelFireAndForget, ct);
```

### Batch de eventos

```csharp
ReadOnlySpan<IEvent> events = [new OrderCreated(id1), new OrderCreated(id2)];
await events.PublishBatch(ct);
```

---

## Result\<T\>

`Result<T>` é um discriminated union — nunca lança exceção por falha de negócio:

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

// Checar e acessar
if (result.IsSuccess)
    Console.WriteLine(result.Value);
else
    Console.WriteLine(result.Error!.Code);
```

### Tipos de Error

```csharp
Error.NotFound("ORDER_NOT_FOUND", "Order 42 does not exist")
Error.Validation("CMD.Validation", "Invalid input", detailsDictionary)
Error.Conflict("ORDER_DUPLICATE", "Order already exists")
Error.Internal("DB_TIMEOUT", "Database timeout")
Error.Unauthorized("AUTH_REQUIRED", "Must be authenticated")
Error.Forbidden("ROLE_REQUIRED", "Insufficient permissions")
```

---

## Pipeline behaviors

Behaviors envolvem a execução do handler. Aplicados via `[UseBehavior]` no handler ou automaticamente por atributos como `[Validate]` e `[Cache]`.

### Behavior customizado

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

Múltiplos behaviors — ordem de declaração = ordem de execução (mais externo primeiro):

```csharp
[Handler]
[UseBehavior(typeof(LogBehavior<,>))]        // 1° (mais externo)
[UseBehavior(typeof(ValidationBehavior<,>))] // 2°
[UseBehavior(typeof(MetricsBehavior<,>))]    // 3° (mais interno)
public sealed class CreateOrderHandler : IHandler<CreateOrderCommand, OrderId> { ... }
// Execução: Log → Validation → Metrics → Handler
```

### Behaviors globais (para todos os handlers)

```csharp
// AssemblyInfo.cs ou Program.cs
[assembly: GlobalBehavior(typeof(LogBehavior<,>),        Order = -100)]
[assembly: GlobalBehavior(typeof(TracingBehavior<,>),    Order = -50)]
```

### Behaviors incluídos em `Mediax.Behaviors`

| Behavior | Atributo / Registro | Descrição |
| --- | --- | --- |
| `LogBehavior<,>` | `[UseBehavior]` | Loga início, fim e falhas com `ILogger` |
| `TracingBehavior<,>` | `[UseBehavior]` | Cria spans OpenTelemetry por request |
| `ValidationBehavior<,>` | `[Validate]` no request | Executa todos `IValidator<T>` (FluentValidation) |
| `CacheBehavior<,>` | `[Cache(Ttl = 60)]` no request | Cacheia resposta em `IDistributedCache` |
| `TransactionBehavior<,>` | `[UseBehavior]` | Abre/comita `DbTransaction` por request |
| `IdempotencyBehavior<,>` | `[UseBehavior]` | Deduplica requests pelo `IdempotencyKey` |
| `CircuitBreakerBehavior<,>` | `[UseBehavior]` | Circuit breaker por tipo de request |
| `ProcessorBehavior<,>` | `[UseBehavior]` | Executa pre/post processors registrados no DI |
| `PollyBehavior<,>` | `[UseBehavior]` | Integração com Polly v8 para retry/timeout/bulkhead |

### Validação automática com FluentValidation

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
// O generator auto-injeta ValidationBehavior — falha retorna Result.Fail(...), nunca lança exceção
```

### Cache automático

```csharp
[Cache(Ttl = 300)] // 5 minutos
public record GetProductQuery(ProductId Id) : IQuery<Product>;
// Requer IDistributedCache no DI
```

---

## Pre/Post Processors

Processadores leves que executam antes/depois do handler sem criar um behavior por tipo:

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

// Registro
services.AddScoped<IRequestPreProcessor<CreateOrderCommand>,        AuditPreProcessor>();
services.AddScoped<IRequestPostProcessor<CreateOrderCommand, OrderId>, NotifyPostProcessor>();

// Habilitar no handler (ProcessorBehavior faz a ligação)
[Handler]
[UseBehavior(typeof(ProcessorBehavior<,>))]
public sealed class CreateOrderHandler : IHandler<CreateOrderCommand, OrderId> { ... }
```

---

## Decorators de request

Timeout e retry sem alterar o handler:

```csharp
var result = await new GetExternalDataQuery(id)
    .WithTimeout(TimeSpan.FromSeconds(2))
    .Send(ct);

var result = await new CallPaymentGatewayCommand(payload)
    .WithRetry(maxAttempts: 3)
    .Send(ct);

// Combinados
var result = await new GetExternalDataQuery(id)
    .WithTimeout(TimeSpan.FromSeconds(5))
    .WithRetry(3)
    .Send(ct);
```

---

## Dispatch polimórfico

Quando o tipo do request é conhecido apenas em runtime:

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

O dispatcher gerado usa `switch` de pattern matching — sem reflexão, ~8 ns.

---

## Diagnósticos do compilador

| Código | Severidade | Descrição |
| --- | --- | --- |
| `MX0001` | Erro | Handler não encontrado para um tipo de request |
| `MX0002` | Erro | Múltiplos `[Handler]` para o mesmo tipo de request não-evento |
| `MX0003` | Aviso | Loop de behavior — mesmo tipo declarado duas vezes em `[UseBehavior]` |
| `MX0004` | Aviso | Dependência cativa — handler Singleton com behavior que recebe `DbContext` |

---

## Auto-discovery

```csharp
// Registra da assembly atual: IValidator<T>, IBehavior<,>, processors, IEventHandler<>
services.AddMediaxFromCallingAssembly();

// Ou explícito
services.AddMediaxFromAssemblies(
    typeof(Program).Assembly,
    typeof(SomeOtherHandler).Assembly);
```

---

## Testes

### Unitários com `FakeDispatcher`

```csharp
var fake = new FakeDispatcher();
fake.Returns<CreateOrderCommand, OrderId>(cmd => new OrderId(Guid.NewGuid()));

var result = await fake.Dispatch(new CreateOrderCommand(items, customerId), default);

Assert.True(result.IsSuccess);
Assert.True(fake.WasDispatched<CreateOrderCommand>());
```

### API do `FakeDispatcher`

```csharp
// Configurar respostas
fake.Returns<MyQuery, MyResult>(new MyResult("ok"));
fake.Returns<MyQuery, MyResult>(query => new MyResult(query.Id.ToString()));
fake.Fails<MyCommand, Unit>(Error.Internal("DB_ERROR", "timeout"));

// Streaming
fake.ReturnsStream<LiveQuery, Price>(new[] { price1, price2 });
fake.ReturnsStream<LiveQuery, Price>(req => GetPricesAsync(req.Tickers));

// Verificação
bool dispatched  = fake.WasDispatched<MyCommand>();
bool withFilter  = fake.WasDispatched<MyCommand>(cmd => cmd.Id == 42);
int  count       = fake.DispatchCount<MyCommand>();
var  allRequests = fake.GetDispatched<MyCommand>();

fake.Reset(); // limpar entre testes
```

### Integração com `MediaxWebApplicationFactory`

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

`MediaxWebApplicationFactory<TEntryPoint>` substitui automaticamente o `IMediaxDispatcher` real pelo `FakeDispatcher` — intercepta tanto chamadas via interface quanto via `.Send()`.

### Isolamento entre testes paralelos (`MediaxTestBase`)

```csharp
public class MyTests : MediaxTestBase
{
    // AsyncLocal garante que cada teste paralelo veja seu próprio FakeDispatcher
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
app.UseMediax(); // inicializa MediaxRuntime e conecta o dispatcher

// Minimal API — sem IMediator injetado
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

> **Ambiente:** BenchmarkDotNet v0.15.8 · .NET 10.0.3 · AMD Ryzen 5 8600G 4.35 GHz · Windows 11 25H2
> `Job=Short  WarmupCount=3  IterationCount=5`

### Dispatch básico (Singleton, sem behaviors)

| Method | Mean | Alloc | vs MediatR |
| --- | ---: | ---: | ---: |
| **Mediax** `.Send()` | **2.6 ns** | **0 B** | **29×** |
| Mediator | 10.1 ns | 0 B | 7.7× |
| MediatR | 77.9 ns | 384 B | 1× |

### Dispatch com behavior (Singleton)

| Method | Mean | Alloc | vs MediatR |
| --- | ---: | ---: | ---: |
| **Mediax** | **3.3 ns** | **0 B** | **22×** |
| Mediator | 9.3 ns | 0 B | 8× |
| MediatR | 73.8 ns | 384 B | 1× |

### Dispatch polimórfico (`IRequest<T>` como variável)

| Method | Mean | Alloc |
| --- | ---: | ---: |
| Mediator | 8.3 ns | 0 B |
| **Mediax** | **8.8 ns** | **0 B** |
| MediatR | 43.6 ns | 200 B |

### Scoped handler (cria `IServiceScope` por chamada)

| Method | Mean | Alloc |
| --- | ---: | ---: |
| Mediator | 9.8 ns | 0 B |
| **Mediax** | 71 ns | 336 B |
| MediatR | 74.6 ns | 384 B |

> Mediator resolve handlers Scoped sem `IServiceScope`; essa otimização ainda não está no Mediax.

### Pre/Post Processors

| Method | Mean | Alloc |
| --- | ---: | ---: |
| Mediator | 20.8 ns | 0 B |
| **Mediax** | **32 ns** | **0 B** |
| MediatR | 84.6 ns | 456 B |

### Eventos — single subscriber

| Method | Mean | Alloc | vs MediatR |
| --- | ---: | ---: | ---: |
| Mediator | 7.8 ns | 0 B | 22× |
| **Mediax Sequential** | **38.7 ns** | **0 B** | **4.5×** |
| MediatR | 175 ns | 768 B | 1× |

### Eventos — 3 subscribers (sequential)

| Method | Mean | Alloc | vs MediatR |
| --- | ---: | ---: | ---: |
| **Mediax Sequential** | **130 ns** | **0 B** | **1.76×** |
| MediatR | 229 ns | 1248 B | 1× |

### Eventos — 3 subscribers (parallel WhenAll)

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

### Rodar os benchmarks

```bash
cd benchmarks/Mediax.Benchmarks
dotnet run -c Release                               # todos
dotnet run -c Release -- --filter "*Event*"         # eventos
dotnet run -c Release -- --filter "*Polymorphic*"   # dispatch polimórfico
dotnet run -c Release -- --filter "*Decorator*"     # timeout / retry
dotnet run -c Release -- --filter "*Processor*"     # pre/post processors
dotnet run -c Release -- --list flat                # listar todos
```

---

## Arquitetura — 3 arquivos gerados por compilação

### `DispatchTable.g.cs`

`FrozenDictionary<Type, Type>` + método `RegisterAll()` que registra todos os handlers e o `MediaxDispatcher` no DI.

### `MediaxDispatcher.g.cs`

`internal sealed class MediaxDispatcher : IMediaxDispatcher` com switch de pattern matching por tipo. Usado no path polimórfico e como fallback.

### `MediaxStaticDispatch.g.cs`

O núcleo da performance. Contém:

- `MediaxStaticHandlers` com campo `volatile MediaxDispatcher? _dispatcher` preenchido via `[ModuleInitializer]`
- **Extension members** por tipo concreto (C# 14) — um bloco `extension(ConcreteType)` por handler

```csharp
// Código gerado — zero boxing, zero switch, zero dicionário
extension(MyApp.CreateOrderCommand request)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<Result<OrderId>> Send(CancellationToken ct = default)
        => MediaxStaticHandlers._dispatcher is { } d
            ? d.Dispatch_CreateOrderHandler(request, ct)   // ~2-3 ns
            : MediaxRuntimeAccessor.Dispatcher.Dispatch(request, ct);
}
```

C# 14 resolve `extension(CreateOrderCommand)` com precedência sobre `extension<T>(IRequest<T>)` quando o receiver é estaticamente tipado — `cmd.Send()` compila para chamada direta ao método do dispatcher singleton.

---

## Estrutura de pacotes

```
Mediax.Core            Interfaces, Result<T>, Error, extension members, decorators
Mediax.SourceGenerator Roslyn generator: [Handler], 3 arquivos gerados, MX0001–MX0004
Mediax.Runtime         MediaxRuntime, MediaxStartupHooks, DI extensions
Mediax.Behaviors       Log, Tracing, Validation, Cache, Transaction,
                       Idempotency, CircuitBreaker, Processor, Polly
Mediax.AspNetCore      app.UseMediax(), IResult helpers
Mediax.Testing         FakeDispatcher, MediaxTestBase, MediaxWebApplicationFactory
```

---

## Contribuindo

```bash
git clone https://github.com/MatheusReichert/mediax
cd mediax
dotnet build Mediax.slnx
dotnet test  Mediax.slnx
```

PRs são bem-vindos. Abra uma issue antes de mudanças grandes.

---

## Roadmap

| Versão | Escopo |
| --- | --- |
| **v0.1** Alpha | IRequest, [Handler], source generator, .Send(), Result\<T\>, behaviors, eventos, streaming ✅ |
| **v0.2** Beta | Mediax.Aspire — integração com .NET Aspire 13 |
| **v1.0** | API congelada, documentação completa, benchmarks publicados, .NET 10 LTS |

---

## Licença

MIT © Mediax Contributors
