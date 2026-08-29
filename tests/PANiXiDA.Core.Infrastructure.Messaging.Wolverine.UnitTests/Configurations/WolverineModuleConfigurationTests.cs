using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class WolverineModuleConfigurationTests
{
    [Fact(DisplayName = "Build creates module registry with request ownership and handler discovery")]
    public void BuildShouldCreateRegistryForDistinctDbContextsAndAssemblies()
    {
        var handlerAssembly = typeof(string).Assembly;
        var configuration = new WolverineModuleConfiguration()
            .AddModule<TestDbContext>(
                typeof(WolverineModuleConfigurationTests).Assembly,
                handlerAssembly,
                handlerAssembly)
            .AddModule<SecondTestDbContext>(typeof(DbContext).Assembly);

        var registry = configuration.Build();

        registry.Registrations.Count.ShouldBe(2);
        registry.DiscoveryAssemblies.Count.ShouldBe(3);
        registry.DiscoveryAssemblies.ShouldContain(handlerAssembly);
        registry.ResolveDbContextType(typeof(WolverineModuleConfigurationTests))
            .ShouldBe(typeof(TestDbContext));
        registry.ResolveDbContextType(typeof(DbContext))
            .ShouldBe(typeof(SecondTestDbContext));
    }

    [Fact(DisplayName = "Build rejects an assembly assigned to multiple modules")]
    public void BuildShouldRejectAssemblyAssignedToMultipleModules()
    {
        var assembly = typeof(WolverineModuleConfigurationTests).Assembly;
        var configuration = new WolverineModuleConfiguration()
            .AddModule<TestDbContext>(assembly)
            .AddModule<SecondTestDbContext>(assembly);

        var act = configuration.Build;

        var exception = Should.Throw<InvalidOperationException>(act);

        exception.Message.ShouldContain("is assigned to both");
    }

    [Fact(DisplayName = "Build rejects a DbContext registered for multiple modules")]
    public void BuildShouldRejectDbContextRegisteredForMultipleModules()
    {
        var configuration = new WolverineModuleConfiguration()
            .AddModule<TestDbContext>(
                typeof(WolverineModuleConfigurationTests).Assembly)
            .AddModule<TestDbContext>(typeof(DbContext).Assembly);

        var exception = Should.Throw<InvalidOperationException>(
            configuration.Build);

        exception.Message.ShouldContain(
            "is registered for more than one Wolverine module");
    }

    [Fact(DisplayName = "Build rejects an empty module configuration")]
    public void BuildShouldRejectEmptyConfiguration()
    {
        var configuration = new WolverineModuleConfiguration();

        var exception = Should.Throw<InvalidOperationException>(configuration.Build);

        exception.Message.ShouldBe("At least one Wolverine module must be registered.");
    }

    [Fact(DisplayName = "ResolveDbContextType rejects an unregistered request assembly")]
    public void ResolveDbContextTypeShouldRejectUnregisteredRequestAssembly()
    {
        var registry = new WolverineModuleConfiguration()
            .AddModule<TestDbContext>(
                typeof(WolverineModuleConfigurationTests).Assembly)
            .Build();

        var exception = Should.Throw<InvalidOperationException>(() =>
            registry.ResolveDbContextType(typeof(DbContext)));

        exception.Message.ShouldContain(
            "does not belong to a registered Wolverine module");
    }

    [Fact(DisplayName = "AddModule validates request and handler assemblies")]
    public void AddModuleShouldValidateRequestAndHandlerAssemblies()
    {
        var configuration = new WolverineModuleConfiguration();
        var requestAssembly = typeof(WolverineModuleConfigurationTests).Assembly;

        Should.Throw<ArgumentNullException>(() =>
            configuration.AddModule<TestDbContext>(null!));
        Should.Throw<ArgumentNullException>(() =>
            configuration.AddModule<TestDbContext>(
                requestAssembly,
                null!));
        var exception = Should.Throw<ArgumentException>(() =>
            configuration.AddModule<TestDbContext>(
                requestAssembly,
                [null!]));

        exception.ParamName.ShouldBe("handlerAssemblies");
    }
}
