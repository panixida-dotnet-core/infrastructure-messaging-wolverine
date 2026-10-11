using Confluent.Kafka;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
{
    throw new InvalidOperationException("Publish and run this smoke test with Native AOT.");
}

var configuration = new ConfigurationManager
{
    ["Broker:BootstrapServers"] = "localhost:9092",
    ["Producer:TopicName"] = "produced-events",
    ["Consumer:TopicName"] = "consumed-events",
    ["Consumer:AutoOffsetReset"] = "earliest",
    ["Consumer:GroupProtocol"] = "Consumer",
    ["Consumer:Attempts"] = "0x10",
    ["Consumer:Defaults:Name"] = "inherited",
    ["Consumer:Rules:0:Name"] = "first",
    ["Consumer:Rules:0:Attempts"] = "3",
    ["Consumer:Groups:events:0"] = "2",
    ["Consumer:Required:Name"] = "required",
    ["Schedule:CronExpression"] = "0 3 * * *",
    ["Schedule:Enabled"] = "false",
};

var broker = OptionBindingRegistry.GetRequired<KafkaBrokerOption>(configuration, "Broker");
var producer = OptionBindingRegistry.GetRequired<KafkaProducerOption>(configuration, "Producer");
var consumer = OptionBindingRegistry.GetRequired<ConsumerOptions>(configuration, "Consumer");
var schedule = OptionBindingRegistry.GetRequired<ScheduleOptions>(configuration, "Schedule");
if (broker.BootstrapServers != "localhost:9092" || producer.TopicName != "produced-events" ||
    consumer.TopicName != "consumed-events" || consumer.GroupProtocol != GroupProtocol.Consumer ||
    consumer.AutoOffsetReset != AutoOffsetReset.Earliest || consumer.ConsumerGroupId != "default-group" || consumer.Attempts != 16 ||
    schedule.Enabled || schedule.CronExpression != "0 3 * * *" || schedule.Name != "default-schedule" ||
    schedule.TimeZoneId != "UTC" || consumer.Defaults?.Name != "inherited" ||
    consumer.Rules.Count != 1 || consumer.Rules[0].Attempts != 3 ||
    !consumer.Groups["events"].SequenceEqual([1, 2]) || consumer.Required.Name != "required")
{
    throw new InvalidOperationException("Generated options binding did not preserve configured values and defaults.");
}

configuration["Consumer:GroupProtocol"] = "invalid";
try
{
    OptionBindingRegistry.GetRequired<ConsumerOptions>(configuration, "Consumer");
    throw new InvalidOperationException("Invalid enum configuration was accepted.");
}
catch (InvalidOperationException exception) when (exception.Message.Contains("Consumer:GroupProtocol", StringComparison.Ordinal))
{
    Console.WriteLine("Native AOT options binding passed: nested objects, collections, required and generic init setters, defaults and conversion errors.");
}

internal sealed class ConsumerOptions : ConsumerBase<RetrySettings>
{
    public int Attempts { get; init; }
    public List<RetrySettings> Rules { get; init; } = [];
    public Dictionary<string, int[]> Groups { get; } = new() { ["events"] = [1] };
    public required RequiredSettings Required { get; init; }

    public ConsumerOptions()
    {
        ConsumerGroupId = "default-group";
    }
}

internal class ConsumerBase<T> : KafkaConsumerOption
{
    public T? Defaults { get; init; }
}

internal sealed record RetrySettings(string Name, int Attempts = 2);

internal sealed class RequiredSettings
{
    public required string Name { get; init; }
}

internal sealed class ScheduleOptions : RecurringCommandOption
{
    public ScheduleOptions()
    {
        Name = "default-schedule";
    }
}
