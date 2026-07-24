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

### Installation

```xml
<ItemGroup>
  <PackageReference Include="PANiXiDA.Core.Infrastructure.Messaging.Wolverine" Version="..." />
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

`AddWolverineMediator<TDbContext>()` registers PANiXiDA `IMediator`, `IEventBus`, and the EF Core outbox dispatcher.

`UseWolverineMediator<TDbContext>()` configures Wolverine, PostgreSQL message storage, EF Core transactions, request middleware, FluentValidation validators from discovery assemblies, durable local queues, durable inbox, and durable outbox.

The package sets Wolverine's application assembly to the process entry assembly. This keeps generated handler code in the publishable application assembly when using Wolverine code generation commands such as `dotnet run -- codegen write`.

The package uses Wolverine `TypeLoadMode.Auto` and includes Wolverine runtime compilation support for handler code that has not been pre-generated.

### Modular Setup

Use the non-generic overload when a host contains multiple modules with independent write DbContexts:

```csharp
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;

builder.Host.UseWolverineMediator(
    builder.Configuration.GetConnectionString("PostgreSqlConnectionString")!,
    messageStoreSchemaName: "wolverine",
    modules =>
    {
        modules.AddModule<IdentityWriteDbContext>(
            typeof(CreateUserHandler).Assembly);
        modules.AddModule<OrdersWriteDbContext>(
            typeof(CreateOrderHandler).Assembly);
    });
```

This overload registers one `IMediator`, one `IEventBus`, and one Wolverine runtime. Both DbContexts are enrolled in the same PostgreSQL message store and therefore use the same durable inbox/outbox tables in the specified schema.

Handlers for the same event type are separated into independent local queues and transactions. Durable message identity includes the destination so fan-out handlers have independent inbox records, retries, and failure handling.

Each module assembly is owned by exactly one DbContext. Requests are routed by their assembly, so include every assembly that contains the module's requests or handlers in its `AddModule<TDbContext>()` call. Assigning one assembly to different DbContexts is rejected during configuration.

The module persistence registration must expose keyed `IUnitOfWork` services under the corresponding DbContext types. `PANiXiDA.Core.Infrastructure.Persistence.Ef` does this automatically for write DbContexts. During a mediator request, the package activates the owning module and routes the existing non-generic `IUnitOfWork`, `IEventBus`, and outbox behavior to that module's keyed services.

Because the active keyed services are selected at request runtime, this overload explicitly allows Wolverine service location for generated handlers. The single-context generic overload keeps Wolverine's default service-location policy.

Durable listeners and other Wolverine messages outside the `IRequest<Result>` pipeline also receive module activation. Their database transaction and inbox lifecycle remain managed by Wolverine's native EF Core transactional middleware, while `IEventBus` resolves the outbox for the active module.

Do not synchronously invoke a command from another module while the first module transaction is active. Separate DbContexts use separate local database transactions, so such a call cannot be atomic. Publish an event through the outbox and let the receiving module handle it independently.

## Kafka Topics

Kafka is opt-in per event type. If no Kafka producer is registered for an event, publishing stays in-process.

Create typed option models in the consuming infrastructure project:

```csharp
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

public sealed class MainKafkaBrokerOption : KafkaBrokerOption
{
}

public sealed class UserCreatedKafkaProducerOption : KafkaProducerOption
{
}

public sealed class UserCreatedKafkaConsumerOption : KafkaConsumerOption
{
}
```

Register brokers, producers, and consumers in the Wolverine mediator configuration:

```csharp
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;

builder.Services.AddWolverineMediator<AppDbContext>();

builder.Host.UseWolverineMediator<AppDbContext>(
    builder.Configuration.GetConnectionString("PostgreSqlConnectionString")!,
    builder.Configuration,
    options =>
    {
        options.AddKafkaBroker<MainKafkaBrokerOption>();
        options.AddKafkaProducer<UserCreatedKafkaProducerOption, UserCreated>();
        options.AddKafkaConsumer<UserCreatedKafkaConsumerOption, UserCreated>();
    },
    typeof(UserCreatedHandler).Assembly);
```

Configuration sections are resolved by option type name:

```json
{
  "ConnectionStrings": {
    "PostgreSqlConnectionString": "Host=localhost;Port=5432;Database=app;Username=app;Password=app"
  },
  "MainKafkaBrokerOption": {
    "BootstrapServers": "localhost:9092"
  },
  "UserCreatedKafkaProducerOption": {
    "TopicName": "users.created"
  },
  "UserCreatedKafkaConsumerOption": {
    "TopicName": "users.created",
    "ConsumerGroupId": "users-service",
    "AutoOffsetReset": "Earliest"
  }
}
```

For named Kafka brokers, put the broker name into broker and topic options:

```csharp
public sealed class ExternalKafkaBrokerOption : KafkaBrokerOption
{
}

public sealed class ExternalUserCreatedKafkaProducerOption : KafkaProducerOption
{
}
```

```json
{
  "ExternalKafkaBrokerOption": {
    "BrokerName": "external",
    "BootstrapServers": "external-kafka:9092"
  },
  "ExternalUserCreatedKafkaProducerOption": {
    "BrokerName": "external",
    "TopicName": "external.users.created"
  }
}
```

```csharp
options.AddKafkaBroker<ExternalKafkaBrokerOption>();
options.AddKafkaProducer<ExternalUserCreatedKafkaProducerOption, UserCreated>();
```

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

With the modular overload, map the shared Wolverine envelope storage in exactly one infrastructure-owned migration context. Mapping the same shared tables in every module context would duplicate their migration ownership.

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
after:   CommitTransactionBehavior
after:   FlushOutgoingMessagesBehavior
finally: CleanupTransactionBehavior
```

The modular overload activates module routing before validation and uses one final behavior to clean up the active transaction and release the module. The application-facing pipeline continues to depend only on the PANiXiDA `IUnitOfWork` and `IEventBus` abstractions.

Validators are discovered from the same assemblies passed to `UseWolverineMediator<TDbContext>()` for handler discovery.

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

## License

This project is licensed under the Apache-2.0 license.

See the [LICENSE](LICENSE) file for details.
