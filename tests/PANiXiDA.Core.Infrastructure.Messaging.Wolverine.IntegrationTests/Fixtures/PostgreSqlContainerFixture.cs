using JasperFx.Resources;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Database;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Messaging.Support;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Transactions;

using Testcontainers.PostgreSql;

using Wolverine.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Fixtures;

public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private bool isStarted;

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    public async Task<WolverineIntegrationApp> CreateApplicationAsync(
        IConfiguration? configuration = null,
        Action<WolverineKafkaConfiguration>? configureKafka = null,
        Action<WolverineRequestBehaviorConfiguration>? configureRequestBehaviors = null)
    {
        container ??= new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("wolverine_integration_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        if (!isStarted)
        {
            await container.StartAsync();
            isStarted = true;
        }

        var connectionString = container.GetConnectionString();
        var journal = new IntegrationTestJournal();

        await ResetDatabaseAsync(connectionString);

        var hostBuilder = Host
            .CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(journal);

                services.AddDbContextWithWolverineIntegration<IntegrationDbContext>(
                    options => options.UseNpgsql(connectionString));

                services.AddScoped<IUnitOfWork, IntegrationUnitOfWork>();
                services.AddScoped<IAggregateTracker, TestAggregateTracker>();
                services.AddWolverineMediator<IntegrationDbContext>();
            });

        if (configureKafka is null && configureRequestBehaviors is null)
        {
            hostBuilder.UseWolverineMediator<IntegrationDbContext>(
                connectionString,
                typeof(PostgreSqlContainerFixture).Assembly);
        }
        else if (configureKafka is null)
        {
            hostBuilder.UseWolverineMediator<IntegrationDbContext>(
                connectionString,
                configureRequestBehaviors,
                typeof(PostgreSqlContainerFixture).Assembly);
        }
        else if (configureRequestBehaviors is null)
        {
            hostBuilder.UseWolverineMediator<IntegrationDbContext>(
                connectionString,
                configuration ?? new ConfigurationManager(),
                configureKafka,
                typeof(PostgreSqlContainerFixture).Assembly);
        }
        else
        {
            hostBuilder.UseWolverineMediator<IntegrationDbContext>(
                connectionString,
                configuration ?? new ConfigurationManager(),
                configureKafka,
                configureRequestBehaviors,
                typeof(PostgreSqlContainerFixture).Assembly);
        }

        hostBuilder.UseResourceSetupOnStartup(StartupAction.SetupOnly);

        var host = await hostBuilder.StartAsync();

        return new WolverineIntegrationApp(
            host,
            journal,
            connectionString);
    }

    private static async Task ResetDatabaseAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var dbContext = new IntegrationDbContext(options);
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }
}
