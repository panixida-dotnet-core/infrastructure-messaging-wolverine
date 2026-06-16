using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.DependencyInjection;

public sealed class HostBuilderExtensionsTests
{
    [Fact(DisplayName = "UseWolverineMediator behavior overload validates message store connection string")]
    public async Task UseWolverineMediatorBehaviorOverloadShouldValidateMessageStoreConnectionString()
    {
        var hostBuilder = Host
            .CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContext<TestDbContext>();
                services.AddWolverineMediator<TestDbContext>();
            });

        hostBuilder.UseWolverineMediator<TestDbContext>(
            " ",
            behaviors =>
            {
                behaviors.Before.InsertAfter(
                    typeof(ClosedCommandBeforeBehavior),
                    typeof(BeginTransactionBehavior<,>));
            },
            typeof(HostBuilderExtensionsTests).Assembly);

        var act = async () =>
        {
            using var host = await hostBuilder.StartAsync(TestContext.Current.CancellationToken);
        };

        var exception = await Should.ThrowAsync<ArgumentException>(act);

        exception.Message.ShouldBe(
            "The Wolverine message store connection string must not be empty. (Parameter 'messageStoreConnectionString')");
    }
}
