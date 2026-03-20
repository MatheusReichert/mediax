# MediaxSpec

**Uma especificação legível por máquina para contratos de mediator CQRS.**

Versão do formato: `0.1`

---

## Motivação

| Especificação | Domínio |
| --- | --- |
| OpenAPI 3.1 | Endpoints HTTP (REST) |
| AsyncAPI 3.0 | Canais de message broker (Kafka, AMQP, MQTT) |
| gRPC / Proto3 | RPC com geração de código multi-linguagem |
| **MediaxSpec 0.1** | **Contratos de mediator CQRS** |

OpenAPI descreve rotas HTTP. AsyncAPI descreve canais de broker. Nenhuma das duas modela o que um mediator expõe nativamente: o **tipo do request** (command/query/event/stream), o **envelope de resultado** (`Result<T>`), os **erros tipados**, a **estratégia de despacho de eventos**, o **pipeline de behaviors**, os **processadores pre/post**, os **decoradores** (timeout, retry) ou o **lifetime do handler**.

MediaxSpec preenche esse espaço.

---

## Princípios de design

1. **Contratos, não implementações.** MediaxSpec descreve a forma das mensagens e as regras de despacho — não o corpo dos handlers, wiring de DI ou internos de framework.

2. **Todos os conceitos de mediator são first-class.** Tipo do request, lifetime do handler, ordem do pipeline, envelope de resultado, estratégias de evento, decoradores e modos de despacho são modelados explicitamente — não mapeados forçadamente para semântica HTTP ou de canal.

3. **Schemas composáveis.** Tipos são definidos uma vez na seção `schemas` e referenciados com `$ref` — igual ao modelo de componentes do OpenAPI, habilitando geração de código multi-linguagem.

4. **Superfície para tooling.** Todo campo serve geração de código, renderização de documentação ou validação de contrato — ou os três.

5. **Extensível.** Qualquer objeto pode ter campos `x-` prefixados. Extensões são convencionalmente namespaceadas por vendor (`x-mediax-`, `x-codegen-`, `x-mycompany-`).

---

## Estrutura do documento

```yaml
mediaxspec: "0.1"       # Obrigatório. Versão do formato.

info:                   # Obrigatório. Metadados do documento.
  title: string
  version: string
  description: string   # Markdown suportado.
  contact:
    name: string
    email: string
    url: string
  license:
    name: string
    url: string

servers:                # Opcional. Alvos de deployment para tooling.
  - id: string
    description: string
    runtime: string     # "dotnet9", "dotnet10", "java21", "node20"
    assembly: string    # "MyApp.Application"
    framework: string   # "mediax", "mediatr", "wolverine"
    x-mediax-registration: string

schemas:                # Opcional. Definições de tipos reutilizáveis (JSON Schema subset).
  SchemaName: SchemaObject

requests:               # Obrigatório. Todos os contratos despachável.
  RequestName: RequestObject

pipelines:              # Opcional. Stacks de behaviors nomeados.
  PipelineName: PipelineObject

errors:                 # Opcional. Catálogo de erros tipados da aplicação.
  ErrorName: ErrorDefinition

tags:                   # Opcional. Agrupamento para documentação.
  - name: string
    description: string

externalDocs:           # Opcional.
  description: string
  url: string

x-*: any               # Campos de extensão permitidos em qualquer nível.
```

---

## Objetos

### RequestObject

O objeto central. Descreve um contrato despachável.

| Campo | Tipo | Obrigatório | Descrição |
| --- | --- | --- | --- |
| `kind` | enum | sim | `command`, `query`, `event`, `stream` |
| `summary` | string | não | Descrição em uma linha |
| `description` | string | não | Markdown |
| `deprecated` | boolean | não | Default `false` |
| `tags` | string[] | não | Referências a nomes de tags |
| `payload` | SchemaRef | sim | Tipo da mensagem de entrada |
| `response` | ResponseObject | não | Ausente para eventos fire-and-forget |
| `errors` | ErrorRef[] | não | Erros tipados possíveis |
| `handler` | HandlerObject | não | Metadados do handler |
| `pipeline` | PipelineRef | não | Pipeline nomeado ou inline |
| `processors` | ProcessorObject | não | Pre/post processors |
| `decorators` | DecoratorObject[] | não | Timeout, retry, circuit breaker |
| `dispatch` | DispatchObject | não | Static vs polimórfico |
| `event` | EventObject | não | Somente quando `kind: event` |
| `stream` | StreamObject | não | Somente quando `kind: stream` |
| `x-*` | any | não | Campos de extensão |

### ResponseObject

| Campo | Tipo | Obrigatório | Descrição |
| --- | --- | --- | --- |
| `type` | enum | sim | `result`, `void`, `stream` |
| `value` | SchemaRef | não | Tipo `T` dentro de `Result<T>` ou `IAsyncEnumerable<T>` |
| `description` | string | não | |

- `result` — o handler retorna `Result<T>` onde `T` é `value`
- `void` — o handler não retorna nada (comando fire-and-forget)
- `stream` — o handler retorna `IAsyncEnumerable<T>`

### HandlerObject

| Campo | Tipo | Obrigatório | Descrição |
| --- | --- | --- | --- |
| `lifetime` | enum | não | `singleton`, `scoped`, `transient`. Default: `scoped` |
| `typeName` | string | não | Nome curto da classe |
| `namespace` | string | não | Namespace completo |
| `description` | string | não | |

### PipelineObject

| Campo | Tipo | Obrigatório | Descrição |
| --- | --- | --- | --- |
| `description` | string | não | |
| `behaviors` | BehaviorObject[] | sim | Lista ordenada de behaviors (índice 0 = mais externo) |

**BehaviorObject:**

| Campo | Tipo | Obrigatório | Descrição |
| --- | --- | --- | --- |
| `name` | string | sim | Identificador |
| `typeName` | string | não | Classe de implementação |
| `order` | integer | não | Ordem explícita |
| `description` | string | não | |
| `parameters` | map\<string, any\> | não | Configuração estática da instância |
| `applyTo` | string[] | não | Restringir a kinds: `command`, `query`, `event`, `stream` |

### ProcessorObject

Pre-processors rodam antes de todos os behaviors. Post-processors rodam após todos os behaviors.

| Campo | Tipo | Obrigatório | Descrição |
| --- | --- | --- | --- |
| `pre` | ProcessorEntry[] | não | Lista ordenada de pre-processors |
| `post` | ProcessorEntry[] | não | Lista ordenada de post-processors |

**ProcessorEntry:** campos `name`, `typeName`, `order`, `description`, `parameters`, `x-*`.

### DecoratorObject

| Campo | Tipo | Obrigatório | Descrição |
| --- | --- | --- | --- |
| `kind` | enum | sim | `timeout`, `retry`, `circuitBreaker`, `bulkhead`, `cache`, `custom` |
| `duration` | string | não | ISO 8601: `PT10S`, `PT5M`, `PT1H` |
| `maxAttempts` | integer | não | Para `retry` |
| `backoff` | enum | não | `none`, `fixed`, `linear`, `exponential` |
| `delay` | string | não | Delay base para backoff (ISO 8601) |
| `retryOn` | ErrorRef[] | não | Só fazer retry nestes erros |
| `failureThreshold` | integer | não | Para `circuitBreaker` |
| `breakDuration` | string | não | Para `circuitBreaker` |
| `maxConcurrency` | integer | não | Para `bulkhead` |
| `typeName` | string | não | Para `custom` |
| `parameters` | map\<string, any\> | não | Para `custom` |

### ErrorDefinition

| Campo | Tipo | Obrigatório | Descrição |
| --- | --- | --- | --- |
| `kind` | enum | sim | `NotFound`, `Validation`, `Conflict`, `Internal`, `Unauthorized`, `Forbidden`, `Transient`, `Custom` |
| `code` | string | sim | Código legível por máquina, ex: `ORDER_NOT_FOUND` |
| `message` | string | não | Template de mensagem default |
| `description` | string | não | Markdown |
| `httpEquivalent` | integer | não | Status HTTP sugerido para adaptadores HTTP |
| `payload` | SchemaRef | não | Tipo de detalhe estruturado do erro |
| `retryable` | boolean | não | Se clientes devem tentar novamente |

### EventObject

Somente quando `kind: event`.

| Campo | Tipo | Obrigatório | Descrição |
| --- | --- | --- | --- |
| `strategy` | enum | sim | `sequential`, `parallelWhenAll`, `parallelFireAndForget` |
| `handlers` | EventHandlerRef[] | não | Handlers inscritos neste evento |
| `ordering` | string | não | `none`, `partitioned`, `global` |
| `deduplication` | DeduplicationObject | não | Config de idempotência |

**Semântica das estratégias:**
- `sequential` — handlers invocados um após o outro; falha interrompe a cadeia
- `parallelWhenAll` — todos invocados concorrentemente; aguarda todos completarem; agrega erros
- `parallelFireAndForget` — todos iniciados concorrentemente; retorna imediatamente sem aguardar

**EventHandlerRef:** campos `name`, `typeName`, `namespace`, `description`, `pipeline`.

**DeduplicationObject:** campos `enabled`, `window` (ISO 8601), `keyExpression` (JSONPath).

### StreamObject

Somente quando `kind: stream`.

| Campo | Tipo | Obrigatório | Descrição |
| --- | --- | --- | --- |
| `itemSchema` | SchemaRef | sim | Tipo de cada item emitido |
| `backpressure` | string | não | `none`, `bounded`, `unbounded` |
| `bufferSize` | integer | não | Para `bounded` |
| `cancellable` | boolean | não | Default `true` |

### DispatchObject

| Campo | Tipo | Obrigatório | Descrição |
| --- | --- | --- | --- |
| `mode` | enum | sim | `static`, `polymorphic` |
| `baseType` | string | não | Para `polymorphic`: tipo base do request |

- `static` — handler resolvido por match exato de tipo (default)
- `polymorphic` — handler resolvido percorrendo a hierarquia de herança

### SchemaObject

Subset de JSON Schema Draft 7 com extensões para mediator:

```yaml
type: object | string | integer | number | boolean | array | enum | oneOf | allOf | anyOf
description: string           # Markdown
properties:
  fieldName:
    type: ...
    description: ...
    format: uuid | date-time | date | duration | uri | email | decimal | int64
    nullable: boolean
    minimum / maximum: number
    minLength / maxLength: integer
    pattern: string           # regex
    default: any
required: [fieldName, ...]
items: SchemaRef              # para type: array
enum: [value, ...]            # para type: enum
nullable: boolean
example: any
examples:
  exampleName:
    summary: string
    value: any
x-mediax-sensitive: boolean   # campo contém PII
x-mediax-encrypted: boolean   # campo é armazenado/transmitido cifrado
```

---

## Extensões recomendadas

```yaml
# Em qualquer RequestObject
x-mediax-idempotent: true              # handler é idempotente
x-mediax-requires-role: "admin"        # roles de autorização necessárias
x-mediax-correlation-key: "payload.id" # JSONPath para o ID de correlação
x-mediax-sla-ms: 200                   # latência máxima esperada em ms
x-docs-since: "1.0.0"                  # versão em que o contrato foi introduzido
x-docs-replaced-by: "PlaceOrderV2"     # para contratos deprecados

# Em ServerObject
x-mediax-version: "0.1.0"             # versão da biblioteca Mediax
x-mediax-registration: "DispatchTable.RegisterAll(services)"

# Em EventHandlerRef
x-mediax-consumer-group: "orders-svc" # para event buses baseados em message broker

# Em SchemaObject
x-mediax-sensitive: true               # campo contém PII
x-mediax-encrypted: true               # campo é cifrado em repouso
```

---

## Como se compara com outras especificações

| Conceito | OpenAPI | AsyncAPI | **MediaxSpec** |
| --- | --- | --- | --- |
| Tipo de operação | HTTP method | send/receive | `command`, `query`, `event`, `stream` |
| Envelope de resposta | HTTP status codes | message payload | `Result<T>` com erros tipados |
| Erros tipados | `responses` por status code | sem suporte nativo | `errors` com `kind`, `code`, `retryable` |
| Pub/sub multi-subscriber | webhooks (limitado) | nativo (channels) | `event.handlers[]` + `strategy` |
| Streaming | multipart / SSE / WebSocket | nativo | `kind: stream` + `StreamObject` |
| Pipeline / middleware | não | não | `pipelines` com `behaviors[]` ordenados |
| Pre/post processors | não | não | `processors.pre[]` / `processors.post[]` |
| Lifetime de handler | não aplicável | não aplicável | `handler.lifetime`: singleton/scoped/transient |
| Decoradores (timeout/retry) | não | não | `decorators[]` com configuração tipada |
| Modo de despacho | não aplicável | não aplicável | `dispatch.mode`: static/polymorphic |
| Deduplicação de eventos | não | parcial (bindings) | `event.deduplication` nativo |

---

## Tooling previsto

Uma especificação sem tooling é apenas um documento. A MediaxSpec é projetada para habilitar:

- **`mediaxspec generate`** — gera stubs de handler, clientes e bindings para múltiplas linguagens a partir do spec
- **`mediaxspec validate`** — valida um arquivo `.mediaxspec.yaml` contra o schema da especificação
- **`mediaxspec diff`** — detecta breaking changes entre duas versões do spec
- **`mediaxspec docs`** — gera documentação HTML/Markdown navegável
- **`mediaxspec test`** — gera casos de teste de contrato a partir dos exemplos no spec
- **Integração com source generator** — o Mediax source generator pode emitir um `.mediaxspec.yaml` automaticamente a partir dos `[Handler]` anotados, mantendo o spec sempre sincronizado com o código

---

## Exemplo completo

Veja [`specs/order-management.mediaxspec.yaml`](specs/order-management.mediaxspec.yaml) para um exemplo completo cobrindo todos os features:

- Commands com timeout, retry, pre/post processors e pipeline
- Queries com cache e audit processor
- Eventos com as três estratégias (sequential, parallelWhenAll, parallelFireAndForget) e deduplicação
- Streaming com backpressure configurado
- Catálogo de erros tipados com `httpEquivalent`
- Pipelines reutilizáveis nomeados
- Tags, exemplos e campos de extensão `x-`
