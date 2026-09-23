using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;

var builder = WebApplication.CreateBuilder(args);

// JSON: emit snake_case-ish property names as declared and be lenient on input.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// Kafka producer as a singleton. Brokers come from KAFKA_BROKERS (docker-compose: kafka:9092).
var brokers = Environment.GetEnvironmentVariable("KAFKA_BROKERS") ?? "localhost:9092";
var producerConfig = new ProducerConfig
{
    BootstrapServers = brokers,
    // Keep the service responsive even if a broker is briefly unavailable.
    MessageTimeoutMs = 5000,
    AllowAutoCreateTopics = true,
};
var producer = new ProducerBuilder<string, string>(producerConfig).Build();
builder.Services.AddSingleton<IProducer<string, string>>(producer);

var app = builder.Build();

app.Lifetime.ApplicationStopping.Register(() =>
{
    producer.Flush(TimeSpan.FromSeconds(5));
    producer.Dispose();
});

var logger = app.Logger;

// Publishes an event to a Kafka topic and returns the standard success payload.
async Task<IResult> Publish(string topic, object payload)
{
    var json = JsonSerializer.Serialize(payload);
    try
    {
        var result = await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = Guid.NewGuid().ToString(),
            Value = json,
        });
        logger.LogInformation("Published to {Topic} @ {Offset}", topic, result.TopicPartitionOffset);
    }
    catch (ProduceException<string, string> ex)
    {
        logger.LogError(ex, "Failed to publish to {Topic}", topic);
        return Results.Json(new { status = "error", error = ex.Error.Reason }, statusCode: 500);
    }

    return Results.Json(new { status = "success", topic, @event = payload }, statusCode: 201);
}

app.MapGet("/api/events/health", () => Results.Ok(new { status = true }));

app.MapPost("/api/events/movie", async (MovieEvent e) =>
    await Publish("movie-events", e));

app.MapPost("/api/events/user", async (UserEvent e) =>
    await Publish("user-events", e));

app.MapPost("/api/events/payment", async (PaymentEvent e) =>
    await Publish("payment-events", e));

var port = Environment.GetEnvironmentVariable("PORT") ?? "8082";
app.Run($"http://0.0.0.0:{port}");

// Event models mirror the payloads exercised by tests/postman/CinemaAbyss.postman_collection.json.
record MovieEvent(
    [property: JsonPropertyName("movie_id")] int MovieId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("user_id")] int UserId);

record UserEvent(
    [property: JsonPropertyName("user_id")] int UserId,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("timestamp")] string? Timestamp);

record PaymentEvent(
    [property: JsonPropertyName("payment_id")] int PaymentId,
    [property: JsonPropertyName("user_id")] int UserId,
    [property: JsonPropertyName("amount")] double Amount,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("timestamp")] string? Timestamp,
    [property: JsonPropertyName("method_type")] string? MethodType);
