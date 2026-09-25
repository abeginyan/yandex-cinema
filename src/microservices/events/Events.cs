using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;

namespace EventsService;

/// <summary>
/// Envelope written to Kafka for every domain event. The shape matches the
/// Event schema in api-specification.yaml (id, type, timestamp, payload).
/// </summary>
public record Event(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("payload")] object Payload);

/// <summary>Outcome of publishing an event, carrying its Kafka coordinates.</summary>
public record PublishResult(Event Event, int Partition, long Offset);

/// <summary>
/// Central place that turns User/Payment/Movie API calls into Kafka messages.
/// Owns the producer and knows which topic each event type belongs to.
/// </summary>
public class Events
{
    public const string MovieTopic = "movie-events";
    public const string UserTopic = "user-events";
    public const string PaymentTopic = "payment-events";

    /// <summary>All topics this service produces to and consumes from.</summary>
    public static readonly string[] Topics = { MovieTopic, UserTopic, PaymentTopic };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IProducer<string, string> _producer;
    private readonly ILogger<Events> _logger;

    public Events(IProducer<string, string> producer, ILogger<Events> logger)
    {
        _producer = producer;
        _logger = logger;
    }

    public Task<PublishResult> PublishMovieAsync(MovieEvent e, CancellationToken ct = default)
        => PublishAsync(MovieTopic, "movie", $"movie-{e.MovieId}-{e.Action}", e, ct);

    public Task<PublishResult> PublishUserAsync(UserEvent e, CancellationToken ct = default)
        => PublishAsync(UserTopic, "user", $"user-{e.UserId}-{e.Action}", e, ct);

    public Task<PublishResult> PublishPaymentAsync(PaymentEvent e, CancellationToken ct = default)
        => PublishAsync(PaymentTopic, "payment", $"payment-{e.PaymentId}-{e.Status}", e, ct);

    private async Task<PublishResult> PublishAsync(
        string topic, string type, string id, object payload, CancellationToken ct)
    {
        var evt = new Event(id, type, DateTime.UtcNow.ToString("o"), payload);
        var json = JsonSerializer.Serialize(evt, JsonOptions);

        var delivery = await _producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = id,
            Value = json,
        }, ct);

        _logger.LogInformation(
            "Produced {Type} event {Id} to {Topic} [partition {Partition}, offset {Offset}]",
            type, id, topic, delivery.Partition.Value, delivery.Offset.Value);

        return new PublishResult(evt, delivery.Partition.Value, delivery.Offset.Value);
    }
}

// Event models mirror the schemas in api-specification.yaml and the payloads
// exercised by tests/postman/CinemaAbyss.postman_collection.json.

public record MovieEvent(
    [property: JsonPropertyName("movie_id")] int MovieId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("user_id")] int UserId,
    [property: JsonPropertyName("rating")] double? Rating = null,
    [property: JsonPropertyName("genres")] string[]? Genres = null,
    [property: JsonPropertyName("description")] string? Description = null);

public record UserEvent(
    [property: JsonPropertyName("user_id")] int UserId,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("timestamp")] string? Timestamp,
    [property: JsonPropertyName("email")] string? Email = null);

public record PaymentEvent(
    [property: JsonPropertyName("payment_id")] int PaymentId,
    [property: JsonPropertyName("user_id")] int UserId,
    [property: JsonPropertyName("amount")] double Amount,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("timestamp")] string? Timestamp,
    [property: JsonPropertyName("method_type")] string? MethodType = null);
