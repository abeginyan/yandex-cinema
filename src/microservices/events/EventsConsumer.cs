using Confluent.Kafka;

namespace EventsService;

/// <summary>
/// Background consumer that reads back every message the producer writes and
/// "processes" it by recording it in the log. This closes the round trip
/// through Kafka and gives us a single place to hook real processing later.
/// </summary>
public class EventsConsumer : BackgroundService
{
    private readonly string _brokers;
    private readonly ILogger<EventsConsumer> _logger;

    public EventsConsumer(ILogger<EventsConsumer> logger)
    {
        _brokers = Environment.GetEnvironmentVariable("KAFKA_BROKERS") ?? "localhost:9092";
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Consume on a dedicated thread so the blocking loop never holds up host startup.
        return Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
    }

    private void ConsumeLoop(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _brokers,
            GroupId = "events-service",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(Events.Topics);
        _logger.LogInformation("Consumer subscribed to {Topics}", string.Join(", ", Events.Topics));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);
                    if (result?.Message is null)
                    {
                        continue;
                    }

                    // "Process" the event: for now that means recording it in the log.
                    _logger.LogInformation(
                        "Processed event from {Topic} [partition {Partition}, offset {Offset}] key={Key}: {Value}",
                        result.Topic, result.Partition.Value, result.Offset.Value,
                        result.Message.Key, result.Message.Value);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Error consuming message: {Reason}", ex.Error.Reason);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown was requested — expected, nothing to do.
        }
        finally
        {
            consumer.Close();
        }
    }
}
