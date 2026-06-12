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

`UseWolverineMediator<TDbContext>()` configures Wolverine, PostgreSQL message storage, EF Core transactions, request middleware, durable local queues, durable inbox, and durable outbox.

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

## Behavior

Commands and queries are invoked in-process through Wolverine and PANiXiDA request contracts.

Domain events are published through `IEventBus`. By default, Wolverine dispatches them to local handlers. When a Kafka producer is registered for the event type, the same event is also routed to the configured Kafka topic through durable outbox.

Kafka consumers use durable inbox and map incoming topic messages to the configured event type with `DefaultIncomingMessage<TEvent>()`.

## Request Behaviors

The default request behavior pipeline is:

```text
before:  BeginTransactionBehavior
after:   PublishDomainEventsBehavior
after:   CommitTransactionBehavior
after:   FlushOutgoingMessagesBehavior
finally: CleanupTransactionBehavior
```

Custom behaviors can be appended or inserted before or after any behavior in the same stage:

```csharp
using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;

builder.Host.UseWolverineMediator<AppDbContext>(
    builder.Configuration.GetConnectionString("PostgreSqlConnectionString")!,
    behaviors =>
    {
        behaviors.Before.InsertAfter(
            typeof(ValidateRequestBehavior<,>),
            typeof(BeginTransactionBehavior<,>));

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
