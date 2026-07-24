using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Configurations;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.TestDoubles.DependencyInjection;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class WolverineModuleConfigurationTests
{
    [Fact(DisplayName = "Build creates module registry for distinct DbContexts and assemblies")]
    public void BuildShouldCreateRegistryForDistinctDbContextsAndAssemblies()
    {
        var configuration = new WolverineModuleConfiguration()
            .AddModule<TestDbContext>(typeof(WolverineModuleConfigurationTests).Assembly)
            .AddModule<SecondTestDbContext>(typeof(DbContext).Assembly);

        var registry = configuration.Build();

        registry.Registrations.Count.ShouldBe(2);
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

    [Fact(DisplayName = "Build rejects an empty module configuration")]
    public void BuildShouldRejectEmptyConfiguration()
    {
        var configuration = new WolverineModuleConfiguration();

        var exception = Should.Throw<InvalidOperationException>(configuration.Build);

        exception.Message.ShouldBe("At least one Wolverine module must be registered.");
    }
}
