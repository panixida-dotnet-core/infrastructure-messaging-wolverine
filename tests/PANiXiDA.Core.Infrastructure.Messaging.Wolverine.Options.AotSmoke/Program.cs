using Confluent.Kafka;

using Microsoft.Extensions.Configuration;

using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;
using PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Options;

var configuration = new ConfigurationManager
{
    ["Broker:BootstrapServers"] = "localhost:9092",
    ["Producer:TopicName"] = "produced-events",
    ["Consumer:TopicName"] = "consumed-events",
    ["Consumer:AutoOffsetReset"] = "earliest",
    ["Consumer:GroupProtocol"] = "Consumer",
    ["Consumer:Attempts"] = "0x10",
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
    schedule.TimeZoneId != "UTC")
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
    Console.WriteLine("Native AOT options binding passed: built-in and derived options, defaults, nullable enums and conversion errors.");
}

internal sealed class ConsumerOptions : KafkaConsumerOption
{
    public int Attempts { get; set; }

    public ConsumerOptions()
    {
        ConsumerGroupId = "default-group";
    }
}

internal sealed class ScheduleOptions : RecurringCommandOption
{
    public ScheduleOptions()
    {
        Name = "default-schedule";
    }
}
