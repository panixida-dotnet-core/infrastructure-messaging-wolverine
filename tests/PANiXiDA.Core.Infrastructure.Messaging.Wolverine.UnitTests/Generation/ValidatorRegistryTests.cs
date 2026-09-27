using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Generation;

public sealed class ValidatorRegistryTests
{
    [Fact(DisplayName = "Validator registry validates registration arguments")]
    public void RegistryShouldValidateRegistrationArguments()
    {
        var assembly = typeof(ValidatorRegistryTests).Assembly;

        Should.Throw<ArgumentNullException>(() => ValidatorRegistry.RegisterAssemblies(null!, []));
        Should.Throw<ArgumentNullException>(() => ValidatorRegistry.RegisterAssemblies(assembly, null!));
        Should.Throw<ArgumentException>(() => ValidatorRegistry.RegisterAssemblies(assembly, " "));
        Should.Throw<ArgumentNullException>(() => ValidatorRegistry.Register(null!, "Assembly", "Validator", null));
        Should.Throw<ArgumentException>(() => ValidatorRegistry.Register(assembly, "", "Validator", null));
        Should.Throw<ArgumentException>(() => ValidatorRegistry.Register(assembly, "Assembly", "", null));
        Should.Throw<ArgumentNullException>(() => ValidatorRegistry.AddValidators(null!, []));
        Should.Throw<ArgumentNullException>(() => ValidatorRegistry.AddValidators(new Microsoft.Extensions.DependencyInjection.ServiceCollection(), null!));
        Should.Throw<ArgumentNullException>(() => ValidatorRegistry.AddValidators(new Microsoft.Extensions.DependencyInjection.ServiceCollection(), [null!]));
    }
}
