using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Fixtures;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Commands;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Events;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Queries;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Support;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Views;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Wolverine;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.OutboxDispatcher;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Commands;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Tests.SecondModule.Messaging.Events;

using Testcontainers.Kafka;

using Wolverine;
using Wolverine.Tracking;

using System.Reflection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests;

public sealed class WolverineMediatorIntegrationTests(PostgreSqlContainerFixture fixture)
    : IClassFixture<PostgreSqlContainerFixture>
{
    [Fact(DisplayName = "Mediator configures Wolverine application assembly from entry assembly")]
    public async Task MediatorShouldConfigureWolverineApplicationAssemblyFromEntryAssembly()
    {
        await using var app = await fixture.CreateApplicationAsync();

        var options = app.Host.Services.GetRequiredService<WolverineOptions>();

        options.ApplicationAssembly.ShouldBeSameAs(Assembly.GetEntryAssembly());
    }

    [Fact(DisplayName = "Mediator dispatches command and query to Wolverine handlers")]
    public async Task MediatorShouldDispatchCommandAndQueryToWolverineHandlers()
    {
        await using var app = await fixture.CreateApplicationAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        const string name = "command-query";

        var commandResult = await app.ExecuteWithMediatorAsync(
            (mediator, cancellationToken) => mediator.SendAsync(
                new CreateIntegrationRecordCommand(id, name),
                cancellationToken),
            cancellationToken);

        var queryResult = await app.ExecuteWithMediatorAsync(
            (mediator, cancellationToken) => mediator.QueryAsync(
                new GetIntegrationRecordQuery(id),
                cancellationToken),
            cancellationToken);

        commandResult.IsSuccess.ShouldBeTrue();
        queryResult.IsSuccess.ShouldBeTrue();
        queryResult.Value.ShouldBe(new IntegrationRecordView(id, name));
        ShouldContainInOrder(
            app.Journal.Entries,
            "unitOfWork.begin",
            "handler.command",
            "unitOfWork.commit");
    }

    [Fact(DisplayName = "Modular mediator routes transactions and outbox through the module DbContext")]
    public async Task ModularMediatorShouldRouteTransactionsAndOutboxThroughModuleDbContext()
    {
        await using var app = await fixture.CreateApplicationAsync(
            configuration: new ConfigurationManager(),
            configureKafka: _ => { },
            useModuleRouting: true);
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var options = app.Host.Services.GetRequiredService<WolverineOptions>();

        options.MultipleHandlerBehavior.ShouldBe(MultipleHandlerBehavior.Separated);
        options.Durability.MessageIdentity.ShouldBe(MessageIdentity.IdAndDestination);
        options.ServiceLocationPolicy.ShouldBe(
            JasperFx.CodeGeneration.Model.ServiceLocationPolicy.AlwaysAllowed);

        var result = await app.ExecuteWithMediatorAsync(
            (mediator, cancellationToken) => mediator.SendAsync(
                new CreateIntegrationRecordAndPublishEventCommand(id, "modular"),
                cancellationToken),
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        (await app.CountRecordsAsync(id)).ShouldBe(1);

        await WolverineIntegrationApp.WaitUntilAsync(
            async () => await app.CountHandledEventsAsync(id) == 1,
            TimeSpan.FromSeconds(10));

        ShouldContainInOrder(
            app.Journal.Entries,
            "unitOfWork.begin",
            "handler.command",
            "unitOfWork.commit");
    }

    [Fact(DisplayName = "Modular mediator isolates module transactions and fans out shared events")]
    public async Task ModularMediatorShouldIsolateTransactionsAndFanOutSharedEvents()
    {
        await using var app = await fixture.CreateApplicationAsync(
            useModuleRouting: true);
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        var result = await app.ExecuteWithMediatorAsync(
            (mediator, cancellationToken) => mediator.SendAsync(
                new CreateSecondModuleRecordCommand(id, "second-module"),
                cancellationToken),
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        (await app.CountSecondModuleRecordsAsync(id)).ShouldBe(1);

        await WolverineIntegrationApp.WaitUntilAsync(
            async () =>
                await app.CountHandledEventsAsync(id) == 1 &&
                await app.CountSecondModuleEventsAsync(
                    id,
                    nameof(SharedModuleEvent)) == 1 &&
                await app.CountSecondModuleEventsAsync(
                    id,
                    nameof(SharedModuleFollowUpEvent)) == 1,
            TimeSpan.FromSeconds(20));

        ShouldContainInOrder(
            app.Journal.Entries,
            $"unitOfWork.begin:{nameof(SecondModuleDbContext)}",
            $"unitOfWork.commit:{nameof(SecondModuleDbContext)}");
    }

    [Fact(DisplayName = "Separated event handlers commit and roll back module transactions independently")]
    public async Task SeparatedEventHandlersShouldCommitAndRollbackModuleTransactionsIndependently()
    {
        await using var app = await fixture.CreateApplicationAsync(
            useModuleRouting: true);
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        var result = await app.ExecuteWithMediatorAsync(
            (mediator, cancellationToken) => mediator.SendAsync(
                new CreateSecondModuleRecordCommand(
                    id,
                    "partial-failure",
                    FailSecondModuleEventHandler: true),
                cancellationToken),
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();

        await WolverineIntegrationApp.WaitUntilAsync(
            async () =>
                await app.CountHandledEventsAsync(id) == 1 &&
                await app.CountSecondModuleEventsAsync(
                    id,
                    nameof(SharedModuleFollowUpEvent)) == 1 &&
                await app.CountRowsAsync(
                    "wolverine.wolverine_dead_letters") == 1,
            TimeSpan.FromSeconds(20));

        (await app.CountSecondModuleRecordsAsync(id)).ShouldBe(1);
        (await app.CountSecondModuleEventsAsync(
            id,
            nameof(SharedModuleEvent))).ShouldBe(0);
    }

    [Fact(DisplayName = "Mediator returns validation failure before command handler")]
    public async Task MediatorShouldReturnValidationFailureBeforeCommandHandler()
    {
        await using var app = await fixture.CreateApplicationAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        var result = await app.ExecuteWithMediatorAsync(
            (mediator, cancellationToken) => mediator.SendAsync(
                new CreateIntegrationRecordCommand(id, string.Empty),
                cancellationToken),
            cancellationToken);

        ShouldContainValidationError(result);
        (await app.CountRecordsAsync(id)).ShouldBe(0);
        app.Journal.Entries.ShouldNotContain("unitOfWork.begin");
        app.Journal.Entries.ShouldNotContain("handler.command");
    }

    [Fact(DisplayName = "Mediator returns validation failure before query handler")]
    public async Task MediatorShouldReturnValidationFailureBeforeQueryHandler()
    {
        await using var app = await fixture.CreateApplicationAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        var result = await app.ExecuteWithMediatorAsync(
            (mediator, cancellationToken) => mediator.QueryAsync(
                new GetIntegrationRecordQuery(Guid.Empty),
                cancellationToken),
            cancellationToken);

        ShouldContainValidationError(result);
    }

    [Fact(DisplayName = "Mediator supports custom request behavior configuration overload")]
    public async Task MediatorShouldSupportCustomRequestBehaviorConfigurationOverload()
    {
        await using var app = await fixture.CreateApplicationAsync(
            configureRequestBehaviors: behaviors =>
            {
                behaviors.Before.InsertAfter(
                    typeof(IntegrationBeforeBehavior<,>),
                    typeof(BeginTransactionBehavior<,>));
            });
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        await app.ExecuteWithMediatorAsync(
            (mediator, cancellationToken) => mediator.SendAsync(
                new CreateIntegrationRecordCommand(id, "custom-behavior"),
                cancellationToken),
            cancellationToken);

        ShouldContainInOrder(
            app.Journal.Entries,
            "unitOfWork.begin",
            "behavior.before",
            "handler.command");
    }

    [Fact(DisplayName = "Event published from command handler is handled after successful commit")]
    public async Task EventPublishedFromCommandHandlerShouldBeHandledAfterSuccessfulCommit()
    {
        await using var app = await fixture.CreateApplicationAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        await app
            .Host
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(20))
            .ExecuteAndWaitAsync((Func<IMessageContext, Task>)(async _ =>
            {
                await app.ExecuteWithMediatorAsync(
                    (mediator, cancellationToken) => mediator.SendAsync(
                        new CreateIntegrationRecordAndPublishEventCommand(id, "event-from-command"),
                        cancellationToken),
                    cancellationToken);
            }));

        (await app.CountRecordsAsync(id)).ShouldBe(1);
        await WolverineIntegrationApp.WaitUntilAsync(
            async () => await app.CountHandledEventsAsync(id) == 1,
            TimeSpan.FromSeconds(10));
        ShouldContainInOrder(
            app.Journal.Entries,
            "unitOfWork.begin",
            "handler.command",
            "unitOfWork.commit",
            "handler.event");
    }

    [Theory(DisplayName = "Published event survives restart after commit when outbox flush is interrupted")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishedEventShouldSurviveRestartWhenFlushIsInterrupted(bool useModules)
    {
        // Arrange
        var id = Guid.NewGuid();
        var command = new CreateIntegrationRecordAndPublishEventCommand(id, "recover-after-commit");

        await using (var app = await fixture.CreateApplicationAsync(
            configureRequestBehaviors: behaviors => behaviors.After.InsertBefore(
                typeof(FailBeforeOutboxFlushBehavior<,>),
                typeof(FlushOutgoingMessagesBehavior<,>)),
            useModuleRouting: useModules))
        {
            // Act: interrupt the pipeline after commit, before it can send buffered messages.
            var exception = await Should.ThrowAsync<Exception>(() => app.ExecuteWithMediatorAsync(
                (mediator, token) => mediator.SendAsync(command, token),
                TestContext.Current.CancellationToken));

            // Assert: a separate connection sees both the committed business data and durable message.
            (exception is PlannedCommandException || exception.InnerException is PlannedCommandException)
                .ShouldBeTrue();
            ShouldContainInOrder(app.Journal.Entries, "unitOfWork.commit", "behavior.failBeforeFlush");
            (await app.CountRecordsAsync(id)).ShouldBe(1);
            (await app.CountHandledEventsAsync(id)).ShouldBe(0);
            (await app.CountRowsAsync(WolverineStorageConstants.IncomingEnvelopesTable)).ShouldBe(1);
        }

        // A fresh host must recover the event from PostgreSQL without another publish or flush call.
        await using var restarted = await fixture.CreateApplicationAsync(
            useModuleRouting: useModules, resetDatabase: false);

        await WolverineIntegrationApp.WaitUntilAsync(
            async () => await restarted.CountHandledEventsAsync(id) == 1,
            TimeSpan.FromSeconds(60));

        (await restarted.CountRecordsAsync(id)).ShouldBe(1);
        restarted.Journal.Entries.ShouldContain("handler.event");
        restarted.Journal.Entries.ShouldNotContain("handler.command");
    }

    [Fact(DisplayName = "Failed command result rolls back changes and does not flush outbox")]
    public async Task FailedCommandResultShouldRollbackChangesAndNotFlushOutbox()
    {
        await using var app = await fixture.CreateApplicationAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        var result = await app.ExecuteWithMediatorAsync(
            (mediator, cancellationToken) => mediator.SendAsync(
                new ReturnFailureCommand(id, "failure-result"),
                cancellationToken),
            cancellationToken);

        result.IsFailure.ShouldBeTrue();
        (await app.CountRecordsAsync(id)).ShouldBe(0);
        ShouldContainInOrder(
            app.Journal.Entries,
            "unitOfWork.begin",
            "handler.command",
            "unitOfWork.rollback");
        app.Journal.Entries.ShouldNotContain("unitOfWork.commit");
    }

    [Fact(DisplayName = "Outbox and handler changes roll back in one transaction when command throws")]
    public async Task OutboxAndHandlerChangesShouldRollbackInSingleTransactionWhenCommandThrows()
    {
        await using var app = await fixture.CreateApplicationAsync(
            useModuleRouting: true);
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        async Task act()
        {
            await app.ExecuteWithMediatorAsync(
                (mediator, cancellationToken) => mediator.SendAsync(
                    new CreateIntegrationRecordAndFailAfterPublishCommand(id, "rollback"),
                    cancellationToken),
                cancellationToken);
        }

        var exception = await Should.ThrowAsync<Exception>(act);

        (exception is PlannedCommandException ||
            exception.InnerException is PlannedCommandException)
            .ShouldBeTrue();
        (await app.CountRecordsAsync(id)).ShouldBe(0);
        (await app.CountHandledEventsAsync(id)).ShouldBe(0);
        ShouldContainInOrder(
            app.Journal.Entries,
            "unitOfWork.begin",
            "handler.command",
            "unitOfWork.rollback");
    }

    [Theory(DisplayName = "Kafka broker receives event after outbox flush with typed or conventional registration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KafkaBrokerShouldReceiveEventAfterOutboxFlushAndHandleItOnce(bool useConventions)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var kafka = new KafkaBuilder("apache/kafka-native:4.0.0")
            .WithVendor(KafkaVendor.ApacheSoftwareFoundation)
            .WithKRaft()
            .Build();
        await kafka.StartAsync(cancellationToken);

        var topicName = $"broker-integration-events-{Guid.NewGuid():N}";
        var consumerGroupId = $"broker-integration-tests-{Guid.NewGuid():N}";
        var brokerSection = useConventions ? "Messaging:Kafka" : nameof(IntegrationKafkaBrokerOption);
        var producerSection = useConventions ? "Messaging:Producers:KafkaIntegrationDomainEvent" : nameof(KafkaIntegrationProducerOption);
        var consumerSection = useConventions ? "Messaging:Consumers:KafkaIntegrationDomainEvent" : nameof(KafkaIntegrationConsumerOption);
        var configuration = CreateConfiguration(
            ($"{brokerSection}:BootstrapServers", kafka.GetBootstrapAddress()),
            ($"{producerSection}:TopicName", topicName),
            ($"{consumerSection}:TopicName", topicName),
            ($"{consumerSection}:ConsumerGroupId", consumerGroupId),
            ($"{consumerSection}:AutoOffsetReset", "Earliest"));
        await using var app = await fixture.CreateApplicationAsync(
            configuration,
            kafkaOptions =>
            {
                if (useConventions)
                {
                    kafkaOptions.AddKafkaBroker();
                    kafkaOptions.AddProducer<KafkaIntegrationDomainEvent>();
                    kafkaOptions.AddConsumer<KafkaIntegrationDomainEvent>();
                }
                else
                {
                    kafkaOptions.AddKafkaBroker<IntegrationKafkaBrokerOption>();
                    kafkaOptions.AddKafkaProducer<KafkaIntegrationProducerOption, KafkaIntegrationDomainEvent>();
                    kafkaOptions.AddKafkaConsumer<KafkaIntegrationConsumerOption, KafkaIntegrationDomainEvent>();
                }
            });
        var id = Guid.NewGuid();

        await app
            .Host
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(60))
            .ExecuteAndWaitAsync((Func<IMessageContext, Task>)(async _ =>
            {
                await PublishThroughOutboxAsync(
                    app,
                    new KafkaIntegrationDomainEvent(id, "kafka-event"),
                    cancellationToken);
            }));

        await AssertHandledExactlyOnceAsync(app, id, cancellationToken);
        app.Journal.Entries.ShouldContain("handler.kafkaEvent");
    }

    private static async Task PublishThroughOutboxAsync<TEvent>(
        WolverineIntegrationApp app,
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : DomainEvent
    {
        await using var scope = app.Host.Services.CreateAsyncScope();

        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var outboxDispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();

        await eventBus.PublishAsync(@event, cancellationToken);
        await outboxDispatcher.FlushAsync(cancellationToken);
    }

    private static async Task AssertHandledExactlyOnceAsync(
        WolverineIntegrationApp app,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        await WolverineIntegrationApp.WaitUntilAsync(
            async () => await app.CountHandledEventsAsync(eventId) == 1,
            TimeSpan.FromSeconds(20));

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        (await app.CountHandledEventsAsync(eventId)).ShouldBe(1);
    }

    private static void ShouldContainInOrder(
        IReadOnlyList<string> actual,
        params string[] expected)
    {
        var startIndex = 0;

        for (var i = 0; i < expected.Length; i++)
        {
            var index = IndexOf(
                actual,
                expected[i],
                startIndex);

            index.ShouldNotBe(-1);
            startIndex = index + 1;
        }
    }

    private static int IndexOf(
        IReadOnlyList<string> actual,
        string expected,
        int startIndex)
    {
        for (var i = startIndex; i < actual.Count; i++)
        {
            if (actual[i] == expected)
            {
                return i;
            }
        }

        return -1;
    }

    private static void ShouldContainValidationError(Result result)
    {
        result.IsFailure.ShouldBeTrue();
        result.Errors.Any(error => error.Type == ErrorType.Validation).ShouldBeTrue();
    }

    private static ConfigurationManager CreateConfiguration(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationManager();

        for (var i = 0; i < values.Length; i++)
        {
            configuration[values[i].Key] = values[i].Value;
        }

        return configuration;
    }
}
