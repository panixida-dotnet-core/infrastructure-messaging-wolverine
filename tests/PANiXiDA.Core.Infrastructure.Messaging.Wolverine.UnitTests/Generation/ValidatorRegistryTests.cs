using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Generation;

public sealed class ValidatorRegistryTests
{
    [Fact(DisplayName = "Validator registry validates registration arguments")]
    public void RegistryShouldValidateRegistrationArguments()
    {
        Should.Throw<ArgumentNullException>(() => ValidatorRegistry.RegisterAssemblies(null!));
        Should.Throw<ArgumentException>(() => ValidatorRegistry.RegisterAssemblies(" "));
        Should.Throw<ArgumentException>(() => ValidatorRegistry.Register("", "Validator", null));
        Should.Throw<ArgumentException>(() => ValidatorRegistry.Register("Assembly", "", null));
        Should.Throw<ArgumentNullException>(() => ValidatorRegistry.AddValidators(null!, []));
        Should.Throw<ArgumentNullException>(() => ValidatorRegistry.AddValidators(new Microsoft.Extensions.DependencyInjection.ServiceCollection(), null!));
        Should.Throw<ArgumentNullException>(() => ValidatorRegistry.AddValidators(new Microsoft.Extensions.DependencyInjection.ServiceCollection(), [null!]));
    }
}
