using System.Text.Json.Serialization;
using Confluent.Kafka;
using EventsService;

var builder = WebApplication.CreateBuilder(args);

// JSON: emit property names as declared and be lenient on input.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// Kafka producer as a singleton. Brokers come from KAFKA_BROKERS (docker-compose: kafka:9092).
var brokers = Environment.GetEnvironmentVariable("KAFKA_BROKERS") ?? "localhost:9092";
var producer = new ProducerBuilder<string, string>(new ProducerConfig
{
    BootstrapServers = brokers,
    // Keep the service responsive even if a broker is briefly unavailable.
    MessageTimeoutMs = 5000,
    AllowAutoCreateTopics = true,
}).Build();
builder.Services.AddSingleton<IProducer<string, string>>(producer);

// The Events class wraps the producer and owns event -> topic routing.
builder.Services.AddSingleton<Events>();

// Background consumer that reads back and logs every event as it is processed.
builder.Services.AddHostedService<EventsConsumer>();

var app = builder.Build();

app.Lifetime.ApplicationStopping.Register(() =>
{
    producer.Flush(TimeSpan.FromSeconds(5));
    producer.Dispose();
});

// Turns a publish outcome into the EventResponse defined in api-specification.yaml,
// or an Error response with status 500 when publishing fails.
async Task<IResult> Handle(Func<Task<PublishResult>> publish)
{
    try
    {
        var r = await publish();
        return Results.Json(new
        {
            status = "success",
            partition = r.Partition,
            offset = r.Offset,
            @event = r.Event,
        }, statusCode: 201);
    }
    catch (ProduceException<string, string> ex)
    {
        app.Logger.LogError(ex, "Failed to publish event");
        return Results.Json(new { error = ex.Error.Reason }, statusCode: 500);
    }
}

app.MapGet("/api/events/health", () => Results.Ok(new { status = true }));

app.MapPost("/api/events/movie", (MovieEvent e, Events events) =>
    Handle(() => events.PublishMovieAsync(e)));

app.MapPost("/api/events/user", (UserEvent e, Events events) =>
    Handle(() => events.PublishUserAsync(e)));

app.MapPost("/api/events/payment", (PaymentEvent e, Events events) =>
    Handle(() => events.PublishPaymentAsync(e)));

var port = Environment.GetEnvironmentVariable("PORT") ?? "8082";
app.Run($"http://0.0.0.0:{port}");
