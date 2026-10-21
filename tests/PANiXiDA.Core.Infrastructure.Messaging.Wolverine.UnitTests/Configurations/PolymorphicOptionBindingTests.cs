using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class PolymorphicOptionBindingTests
{
    [Theory(DisplayName = "Generated binding rejects runtime subtypes instead of silently binding only base properties")]
    [InlineData("Value")]
    [InlineData("Map:existing")]
    public void BinderShouldRejectRuntimeSubtypes(string member)
    {
        var configuration = new ConfigurationManager { [$"Options:{member}:Name"] = "configured" };

        var error = Should.Throw<NotSupportedException>(() =>
            OptionBindingRegistry.GetRequired<PolymorphicOptions>(configuration, "Options"));

        error.Message.ShouldContain("Options:" + member);
        error.Message.ShouldContain(typeof(DerivedSettings).FullName!);
        error.Message.ShouldContain(typeof(Settings).FullName!);
        error.Message.ShouldContain("Declare the concrete configuration model type.");
    }

    [Fact(DisplayName = "Generated binding supports concrete models while retaining option inheritance and existing instances")]
    public void BinderShouldBindConcreteModels()
    {
        var configuration = new ConfigurationManager
        {
            ["Options:TopicName"] = "events",
            ["Options:Value:Name"] = "configured",
            ["Options:Value:Extra"] = "extra",
            ["Options:Map:existing:Name"] = "mapped",
        };

        var original = configuration.GetSection("Options").Get<ConcreteOptions>()!;
        var generated = OptionBindingRegistry.GetRequired<ConcreteOptions>(configuration, "Options");

        generated.TopicName.ShouldBe(original.TopicName);
        generated.Value.ShouldBeSameAs(generated.Initial);
        generated.Value.Name.ShouldBe(original.Value.Name);
        generated.Value.Extra.ShouldBe(original.Value.Extra);
        generated.Map["existing"].Name.ShouldBe(original.Map["existing"].Name);
    }

    public sealed class PolymorphicOptions : KafkaConsumerOption
    {
        public Settings Value { get; } = new DerivedSettings();
        public Dictionary<string, Settings> Map { get; } = new() { ["existing"] = new DerivedSettings() };
    }

    public sealed class ConcreteOptions : KafkaConsumerOption
    {
        internal DerivedSettings Initial { get; } = new();
        public DerivedSettings Value => Initial;
        public Dictionary<string, DerivedSettings> Map { get; } = new() { ["existing"] = new DerivedSettings() };
    }

    public class Settings
    {
        public string Name { get; init; } = "initial";
        public new string GetType() => Name;
    }

    public sealed class DerivedSettings : Settings
    {
        public string Extra { get; init; } = "initial";
    }
}
