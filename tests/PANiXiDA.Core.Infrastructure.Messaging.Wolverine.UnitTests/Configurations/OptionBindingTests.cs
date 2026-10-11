using Confluent.Kafka;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.UnitTests.Configurations;

public sealed class OptionBindingTests
{
    [Fact(DisplayName = "Generated binders preserve inherited values, constructor defaults and renamed properties")]
    public void BinderShouldPreserveDerivedOptions()
    {
        var configuration = new ConfigurationManager
        {
            ["Consumer:topicname"] = "events",
            ["Consumer:AutoOffsetReset"] = "earliest",
            ["Consumer:GroupProtocol"] = "consumer",
            ["Consumer:retry-count"] = "5",
            ["Consumer:UnknownSetting"] = "ignored",
        };

        var option = OptionBindingRegistry.GetRequired<DerivedConsumerOption>(configuration, "Consumer");

        option.TopicName.ShouldBe("events");
        option.ConsumerGroupId.ShouldBe("constructor-group");
        option.BrokerName.ShouldBe("constructor-broker");
        option.AutoOffsetReset.ShouldBe(AutoOffsetReset.Earliest);
        option.GroupProtocol.ShouldBe(GroupProtocol.Consumer);
        option.RetryCount.ShouldBe(5);
        option.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Theory(DisplayName = "Generated binders preserve nullable enum and boolean binding behavior")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Classic")]
    [InlineData("consumer")]
    [InlineData("1")]
    public void BinderShouldMatchReflectionForNullableEnums(string? value)
    {
        var configuration = new ConfigurationManager
        {
            ["Consumer:TopicName"] = "events",
            ["Consumer:GroupProtocol"] = value,
        };

        var generated = OptionBindingRegistry.GetRequired<DerivedConsumerOption>(configuration, "Consumer");
        var original = configuration.GetSection("Consumer").Get<DerivedConsumerOption>()!;

        generated.GroupProtocol.ShouldBe(original.GroupProtocol);
        generated.TopicName.ShouldBe(original.TopicName);
    }

    [Theory(DisplayName = "Generated binding reports invalid enum and scalar values with their configuration path")]
    [InlineData("GroupProtocol", "invalid")]
    [InlineData("retry-count", "invalid")]
    [InlineData("retry-count", "99999999999999999999")]
    [InlineData("Timeout", "invalid")]
    public void BinderShouldReportConversionErrors(string key, string value)
    {
        var configuration = new ConfigurationManager { [$"Consumer:{key}"] = value };

        var exception = Should.Throw<InvalidOperationException>(() =>
            OptionBindingRegistry.GetRequired<DerivedConsumerOption>(configuration, "Consumer"));

        exception.Message.ShouldContain($"Consumer:{key}");
        exception.InnerException.ShouldNotBeNull();
    }

    [Fact(DisplayName = "Generated binders preserve option property setters")]
    public void BinderShouldInvokeDerivedPropertySetters()
    {
        var configuration = new ConfigurationManager { ["Schedule:Identifier"] = "changed-name" };

        var option = OptionBindingRegistry.GetRequired<DerivedScheduleOption>(configuration, "Schedule");

        option.Name.ShouldBe("changed-name");
        option.Enabled.ShouldBeTrue();
        option.TimeZoneId.ShouldBe("UTC");
    }

    [Theory(DisplayName = "Generated scalar binding preserves standard converter formats")]
    [InlineData("5", "00:00:12")]
    [InlineData("0x10", "00:01:00")]
    [InlineData("#20", "1.00:00:00")]
    public void BinderShouldPreserveScalarFormats(string retryCount, string timeout)
    {
        var configuration = new ConfigurationManager
        {
            ["Consumer:retry-count"] = retryCount,
            ["Consumer:Timeout"] = timeout,
        };

        var generated = OptionBindingRegistry.GetRequired<DerivedConsumerOption>(configuration, "Consumer");
        var original = configuration.GetSection("Consumer").Get<DerivedConsumerOption>()!;

        generated.RetryCount.ShouldBe(original.RetryCount);
        generated.Timeout.ShouldBe(original.Timeout);
    }

    [Theory(DisplayName = "Generated schedule binders preserve default and configured boolean values")]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData("false")]
    public void BinderShouldMatchReflectionForBooleans(string? enabled)
    {
        var configuration = new ConfigurationManager
        {
            ["Schedule:Name"] = "test",
            ["Schedule:Enabled"] = enabled,
        };

        var generated = OptionBindingRegistry.GetRequired<RecurringCommandOption>(configuration, "Schedule");
        var original = configuration.GetSection("Schedule").Get<RecurringCommandOption>()!;

        generated.Enabled.ShouldBe(original.Enabled);
        generated.TimeZoneId.ShouldBe(original.TimeZoneId);
    }

    [Fact(DisplayName = "Generated binder registration rejects null delegates")]
    public void RegistrationShouldRejectNullBinder()
    {
        Should.Throw<ArgumentNullException>(() => OptionBindingRegistry.Register<KafkaBrokerOption>(null!));
    }

    [Fact(DisplayName = "Generated scalar conversion rejects null arguments")]
    public void ConversionShouldRejectNullArguments()
    {
        var section = new ConfigurationManager().GetSection("Value");

        Should.Throw<ArgumentNullException>(() => OptionBindingRegistry.Parse(null!, static value => value));
        Should.Throw<ArgumentNullException>(() => OptionBindingRegistry.Parse<string>(section, null!));
    }

    [Fact(DisplayName = "Unregistered options fail explicitly without falling back to reflection")]
    public void MissingBinderShouldFailExplicitly()
    {
        var configuration = new ConfigurationManager { ["Unregistered:Name"] = "test" };

        var exception = Should.Throw<InvalidOperationException>(() =>
            OptionBindingRegistry.GetRequired<object>(configuration, "Unregistered"));

        exception.Message.ShouldContain("No generated configuration binder exists for 'System.Object'");
    }

    public sealed class DerivedConsumerOption : KafkaConsumerOption
    {
        public DerivedConsumerOption()
        {
            ConsumerGroupId = "constructor-group";
            BrokerName = "constructor-broker";
            GroupProtocol = Confluent.Kafka.GroupProtocol.Consumer;
        }

        [ConfigurationKeyName("retry-count")]
        public int RetryCount { get; set; }

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    public sealed class DerivedScheduleOption : RecurringCommandOption
    {
        public string Identifier
        {
            get => Name;
            set => Name = value;
        }
    }
}
