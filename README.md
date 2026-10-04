# PANiXiDA.Core.Infrastructure.Messaging.Wolverine

`PANiXiDA.Core.Infrastructure.Messaging.Wolverine` is a .NET library that connects PANiXiDA.Core application messaging abstractions to WolverineFx.

It provides an in-process mediator, in-process domain event publishing by default, optional Kafka topic routing for selected event types, and durable inbox/outbox support backed by PostgreSQL.

## Status

[![CI](https://github.com/panixida-dotnet-core/infrastructure-messaging-wolverine/actions/workflows/ci.yml/badge.svg)](https://github.com/panixida-dotnet-core/infrastructure-messaging-wolverine/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/PANiXiDA.Core.Infrastructure.Messaging.Wolverine.svg)](https://www.nuget.org/packages/PANiXiDA.Core.Infrastructure.Messaging.Wolverine)
[![NuGet downloads](https://img.shields.io/nuget/dt/PANiXiDA.Core.Infrastructure.Messaging.Wolverine.svg)](https://www.nuget.org/packages/PANiXiDA.Core.Infrastructure.Messaging.Wolverine)
[![Target Framework](https://img.shields.io/badge/target-net10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/panixida-dotnet-core/infrastructure-messaging-wolverine.svg)](LICENSE)

## Features

- `IMediator` implementation based on Wolverine in-process invocation.
- `IEventBus` implementation based on Wolverine EF Core outbox.
- `IScheduler` implementation for durable command and event scheduling through the same outbox.
- Typed recurring command registration from cron configuration sections.
- Default in-process event handling for domain events.
- Explicit Kafka producer and consumer registration per event type.
- Durable inbox and outbox policies for listeners, local queues, and external senders.
- PostgreSQL message storage with EF Core transaction integration.
- One shared Wolverine runtime and message-store schema across multiple module DbContexts.
- Request-scoped routing to keyed module `IUnitOfWork` and EF Core outbox services.
- FluentValidation validator registration from Wolverine discovery assemblies.
- Wolverine application assembly resolution from the entry assembly for pre-generated handler code.
- Runtime compilation support for Wolverine `TypeLoadMode.Auto`.

## Quick Start

### Requirements

- .NET 10 SDK
- PostgreSQL for Wolverine message storage
- Kafka only when external event topics are registered

The package uses Wolverine 6.45.0. Full Native AOT support is not currently provided.

### Installation

Use the latest 4.2.x version:

```xml
<ItemGroup>
  <PackageReference Include="PANiXiDA.Core.Infrastructure.Messaging.Wolverine" Version="4.2.*" />
</ItemGroup>
```

### Minimal Setup

```csharp
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;

builder.Services.AddWolverineMediator<AppDbContext>();

builder.Host.UseWolverineMediator<AppDbContext>(
    builder.Configuration.GetConnectionString("PostgreSqlConnectionString")!,
    typeof(CreateUserHandler).Assembly);
```

`AddWolverineMediator<TDbContext>()` registers PANiXiDA `IMediator`, `IEventBus`, `IScheduler`, and the EF Core outbox dispatcher.

`UseWolverineMediator<TDbContext>()` configures Wolverine, PostgreSQL message storage, EF Core transactions, request middleware, FluentValidation validators from discovery assemblies, durable local queues, durable inbox, and durable outbox.

The package sets Wolverine's application assembly to the process entry assembly. This keeps generated handler code in the publishable application assembly when using Wolverine code generation commands such as `dotnet run -- codegen write`.

The package uses Wolverine `TypeLoadMode.Auto` and includes Wolverine runtime compilation support for handler code that has not been pre-generated.

### Modular Setup

Use the non-generic overload when a host contains multiple modules with independent write DbContexts:

```csharp
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;

builder.Host.UseWolverineMediator(
    builder.Configuration.GetConnectionString("PostgreSqlConnectionString")!,
    modules =>
    {
        modules.AddModule<IdentityWriteDbContext>(
            typeof(CreateUserCommand).Assembly,
            typeof(IdentityIntegrationEventHandler).Assembly);
        modules.AddModule<OrdersWriteDbContext>(
            typeof(CreateOrderCommand).Assembly,
            typeof(OrdersIntegrationEventHandler).Assembly);
    });
```

This overload registers one `IMediator`, one `IEventBus`, one `IScheduler`, and one Wolverine runtime. Both DbContexts are enrolled in the same PostgreSQL message store and therefore use the same durable inbox/outbox tables in the `wolverine` schema.

Each `AddModule<TDbContext>()` also creates a scoped `IOutboxDispatcher` registration keyed by `typeof(TDbContext)`. Outside the mediator pipeline, resolve it from the same DI scope as the module's DbContext:

```csharp
using Microsoft.Extensions.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;

var outbox = scope.ServiceProvider.GetRequiredKeyedService<IOutboxDispatcher>(
    typeof(OrdersWriteDbContext));
```

This selects the module's EF Core outbox directly. Within an explicit transaction, call `outbox.SaveChangesAsync(cancellationToken)` before commit and `outbox.FlushAsync(cancellationToken)` after commit.

Handlers for the same event type are separated into independent local queues and transactions. Durable message identity includes the destination so fan-out handlers have independent inbox records, retries, and failure handling.

The first assembly passed to `AddModule<TDbContext>()` contains the module's requests and is owned by exactly one DbContext. Additional assemblies are used only for handler and validator discovery. Assigning one request assembly to different DbContexts is rejected during configuration.

The module persistence registration must expose keyed `IUnitOfWork` services under the corresponding DbContext types. `PANiXiDA.Core.Infrastructure.Persistence.Ef` does this automatically for write DbContexts. During a mediator request, the package activates the owning module and routes the existing non-generic `IUnitOfWork` and `IEventBus` abstractions to that module's transaction and EF Core outbox.

Before a successful command commits, `PersistOutgoingMessagesBehavior` saves the active module's DbContext, including tracked message envelopes and business changes. It runs after domain event publication and before `CommitTransactionBehavior`; `FlushOutgoingMessagesBehavior` releases messages only after commit. Custom request pipelines must preserve this order. `IUnitOfWork.CommitTransactionAsync()` itself only completes the transaction.

Custom `IOutboxDispatcher` implementations must implement `SaveChangesAsync(CancellationToken cancellationToken)` when upgrading. Persist pending messages before commit, or explicitly return a completed task if they were already persisted in the current transaction. No default implementation is provided by the interface.

They must also implement `SendAsync` and the `PublishAsync` overload accepting Wolverine `DeliveryOptions`. Preserve those options and add messages to the same transactional outbox without committing or flushing it.

Calls to `SaveChangesAsync` and `FlushAsync` require an explicit cancellation token. Pass `CancellationToken.None` when cancellation is not needed.

For ordinary Wolverine messages, including events, module selection is not based on the message assembly. Wolverine detects the transaction from the concrete handler's DbContext dependency. This allows handlers for one shared event contract to commit or roll back independently in different module schemas. A transactional handler must depend on exactly one write DbContext.

`IEventBus` and `IScheduler` share `IOutboxDispatcher`, which uses the keyed module outbox inside the mediator request pipeline and the current Wolverine message context inside native handlers. The inbox record, handler changes, and messages published or scheduled by a native handler therefore share its selected DbContext transaction.

Without an active mediator module, the modular dispatcher's `SaveChangesAsync` throws `InvalidOperationException`: it cannot select a DbContext to save. Native handlers rely on Wolverine's transaction middleware to save changes and flush messages, so `FlushAsync` remains a no-op in that context. Outside the mediator pipeline, use the keyed module dispatcher to save and flush explicitly.

Do not synchronously invoke a command from another module while the first module transaction is active. Separate DbContexts use separate local database transactions, so such a call cannot be atomic. Publish an event through the outbox and let the receiving module handle it independently.

### Scheduling

Inject `IScheduler` from `PANiXiDA.Core.Application.Messaging.Scheduling` into handlers:

```csharp
await scheduler.ScheduleAsync(command, TimeSpan.FromMinutes(15), cancellationToken);
await scheduler.ScheduleAtAsync(occurredEvent, publishAt, cancellationToken);
```

Commands use `SendAsync`; events use `PublishAsync`. Scheduling delays delivery and does not return handler results.
The adapters do not check messages or delivery options for null, reject negative delays, or check cancellation before dispatch. Delay values are passed unchanged to Wolverine. Persistence still passes the cancellation token to EF Core.
Inside mediator requests, scheduled messages use the active module's EF Core outbox and commit or roll back with its business changes.
Native Wolverine handlers use their enlisted message context. The configured PostgreSQL storage and durable queues preserve scheduled messages across restarts.

Outside handlers in modular setups, construct `WolverineScheduler` with the module's keyed `IOutboxDispatcher` resolved using `typeof(AppDbContext)`. Single-context setups can use the registered `IScheduler`. Save and commit through the same `IDbContextOutbox<AppDbContext>`/DbContext transaction; scheduling alone does not commit changes.
The adapter uses typed DI registrations and introduces no runtime reflection or code generation; full Native AOT support remains limited by Wolverine and EF Core.

### Recurring Commands

Register periodic `ICommand<Result>` messages by command type, without a separate options class:

```csharp
using PANiXiDA.Core.Application.Messaging.Mediator.Contracts;
using PANiXiDA.Core.ResultPattern;

public sealed record CleanupExpiredItemsCommand : ICommand<Result>;
```

```json
{
  "Messaging": {
    "Schedules": {
      "CleanupExpiredItemsCommand": {
        "Name": "cleanup-expired-items",
        "CronExpression": "*/15 * * * *"
      }
    }
  }
}
```

```csharp
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;

builder.Services.AddWolverineMediator<AppDbContext>();

builder.Host.UseWolverineMediator<AppDbContext>(
    builder.Configuration.GetConnectionString("PostgreSqlConnectionString")!,
    builder.Configuration,
    configureKafka: null,
    configureRequestBehaviors: null,
    configureSchedules: schedules => schedules.AddRecurringCommand<CleanupExpiredItemsCommand>(),
    discoveryAssemblies: typeof(CleanupExpiredItemsHandler).Assembly);
```

Each occurrence creates a new command and runs its ordinary handler through the transaction/outbox pipeline.
For a command with an occurrence-time constructor, use `AddRecurringCommand<MyCommand>(time => new MyCommand(time))`.
`Enabled` defaults to `true`, and `TimeZoneId` to `UTC`. Invalid enabled settings raise `OptionsValidationException`
during host construction. Keep `Name` stable and unique within the application.

An optional `parentSectionName` selects another parent section. The typed `AddRecurringCommand<TOption, TCommand>(factory)`
overload still binds the section named after `TOption`. Both APIs work with modules and Kafka.
The first enabled schedule requires updating Wolverine's message-store schema. See [Wolverine recurring messages](https://wolverinefx.net/guide/messaging/recurring.html).

## Kafka Topics

Register Kafka routes by domain event type. Options are read from `Messaging:Kafka`,
`Messaging:Producers:<EventType>`, and `Messaging:Consumers:<EventType>`:

```csharp
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;

builder.Services.AddWolverineMediator<AppDbContext>();

builder.Host.UseWolverineMediator<AppDbContext>(
    builder.Configuration.GetConnectionString("PostgreSqlConnectionString")!,
    builder.Configuration,
    options =>
    {
        options.AddKafkaBroker();
        options.AddProducer<UserCreated>();
        options.AddConsumer<UserCreated>();
    },
    typeof(UserCreatedHandler).Assembly);
```

```json
{
  "Messaging": {
    "Kafka": {
      "BootstrapServers": "localhost:9092"
    },
    "Producers": {
      "UserCreated": {
        "TopicName": "users.created"
      }
    },
    "Consumers": {
      "UserCreated": {
        "TopicName": "users.created",
        "ConsumerGroupId": "users-service",
        "AutoOffsetReset": "Earliest"
      }
    }
  }
}
```

Producers use the durable outbox; consumers use the durable inbox. Events without a Kafka producer remain in-process.
For named brokers, set the same `BrokerName` in broker and route settings. Custom configuration paths are supported:

```csharp
options.AddKafkaBroker("External:Kafka");
options.AddProducer<UserCreated>("External:Producers");
options.AddConsumer<UserCreated>("External:Consumers");
```

The existing `AddKafkaBroker<TOption>()`, `AddKafkaProducer<TOption, TEvent>()`, and
`AddKafkaConsumer<TOption, TEvent>()` methods remain available and bind sections named after their option types.

## EF Core Storage

The package enrolls `TDbContext` in Wolverine PostgreSQL message storage. If the application keeps Wolverine envelope tables in EF Core migrations, map them in the DbContext model:

```csharp
using Wolverine.EntityFrameworkCore;

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    modelBuilder.MapWolverineEnvelopeStorage("wolverine");
}
```

With the modular overload, the configured message-store schema is shared by all modules. Let Wolverine managed resources own that schema, or map it in exactly one dedicated messaging migration context. Do not map the shared envelope tables in every business module DbContext.

When upgrading an existing PostgreSQL store from Wolverine 6.40, review the envelope tables' UTC timestamp defaults;
the package update alone does not change deployed defaults ([6.42 notes](https://github.com/JasperFx/wolverine/releases/tag/V6.42.0)).
Recurring schedules now select `CompareByHash` by default and preserve an explicit `CompareByString`. Hash mode requires
`wolverine_deduplication_hashed` alongside `wolverine_recurring_messages`. Existing deduplication claims are not copied
from the old table; `CompareByString` retains that table and its comparison behavior ([6.45 notes](https://github.com/JasperFx/wolverine/releases/tag/V6.45.0)).

### Dead Letter Expiration

Both single-context and modular registrations enable automatic expiration of database-backed dead letters after seven days. Wolverine assigns the expiration when a message enters the dead letter store: it uses the message's `DeliverBy` value when present, otherwise the current time plus seven days. Its background durability agent permanently deletes expired messages; cleanup is periodic, not immediate at the expiration time.

Applications can override the retention period or disable expiration by configuring Wolverine after `UseWolverineMediator`:

```csharp
using Wolverine;

builder.Services.ConfigureWolverine(options =>
{
    options.Durability.DeadLetterQueueExpirationEnabled = true;
    options.Durability.DeadLetterQueueExpiration = TimeSpan.FromDays(14);
});
```

Set `DeadLetterQueueExpirationEnabled` to `false` to disable automatic cleanup. These settings apply to database-backed dead letters, not native broker dead letter queues. Existing rows without an expiration timestamp are not backfilled by this configuration change.

## Behavior

Commands and queries are invoked in-process through Wolverine and PANiXiDA request contracts.

Domain events are published through `IEventBus`. By default, Wolverine dispatches them to local handlers. When a Kafka producer is registered for the event type, the same event is also routed to the configured Kafka topic through durable outbox.

Kafka consumers use durable inbox and map incoming topic messages to the configured event type with `DefaultIncomingMessage<TEvent>()`.

## Request Behaviors

The default request behavior pipeline is:

```text
before:  ValidationBehavior
before:  BeginTransactionBehavior
after:   PublishDomainEventsBehavior
after:   PersistOutgoingMessagesBehavior
after:   CommitTransactionBehavior
after:   FlushOutgoingMessagesBehavior
finally: CleanupTransactionBehavior
```

The modular overload activates module routing before validation, keeps the application `CleanupTransactionBehavior`, and releases module routing after cleanup. The application-facing pipeline continues to depend only on the PANiXiDA `IUnitOfWork` and `IEventBus` abstractions.

Validators are discovered from the same assemblies passed to `UseWolverineMediator<TDbContext>()` for handler discovery.

The bundled source generator prepares request behavior metadata and scoped
FluentValidation registrations. Keep analyzer assets enabled in the host and reference
the assemblies passed to `AddModule` or `UseWolverineMediator`. Requests, behaviors,
validators, and validated types must be accessible to generated code, normally
`public` for referenced assemblies. Internal validators require the generator in
their declaring project. No additional registration calls, attributes, partial
declarations, or code-generation commands are required.

Missing validator registrations fail explicitly at startup without reflection scanning.
Validators within each discovery assembly are registered in deterministic type-name order.
Rebuild and republish the host together with changed discovery assemblies to regenerate
their validator registrations.
Validators emitted by another source generator must be compiled in a referenced project,
such as Application, so the host can discover them. Generating them alongside Wolverine
registrations in the same project produces diagnostic `PANWOLVSG001`.

Custom behaviors can be appended or inserted before or after any behavior in the same stage:

```csharp
using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;

builder.Host.UseWolverineMediator<AppDbContext>(
    builder.Configuration.GetConnectionString("PostgreSqlConnectionString")!,
    behaviors =>
    {
        behaviors.Before.InsertAfter(
            typeof(AuthorizeRequestBehavior<,>),
            typeof(ValidationBehavior<,>));

        behaviors.After.InsertBefore(
            typeof(AuditRequestResultBehavior<,>),
            typeof(CommitTransactionBehavior<,>));

        behaviors.Finally.InsertAfter(
            typeof(ReleaseRequestLockBehavior<,>),
            typeof(CleanupTransactionBehavior<,>));
    },
    typeof(CreateUserHandler).Assembly);
```

The same behavior configuration can be combined with Kafka topology registration:

```csharp
builder.Host.UseWolverineMediator<AppDbContext>(
    builder.Configuration.GetConnectionString("PostgreSqlConnectionString")!,
    builder.Configuration,
    kafka =>
    {
        kafka.AddKafkaBroker<MainKafkaBrokerOption>();
        kafka.AddKafkaProducer<UserCreatedKafkaProducerOption, UserCreated>();
    },
    behaviors =>
    {
        behaviors.After.InsertBefore(
            typeof(AuditRequestResultBehavior<,>),
            typeof(FlushOutgoingMessagesBehavior<,>));
    },
    typeof(UserCreatedHandler).Assembly);
```

## Development

```bash
dotnet restore
dotnet format
dotnet build --configuration Release
dotnet test --configuration Release
```

### Continuous integration

Every pull request and push to `main` runs formatting, tests, and mandatory
SonarQube analysis. Publishing from `main` starts only after the SonarQube
Quality Gate succeeds.

## License

This project is licensed under the Apache-2.0 license.

See the [LICENSE](LICENSE) file for details.
