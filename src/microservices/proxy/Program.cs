var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient("proxy").ConfigurePrimaryHttpMessageHandler(() =>
    new SocketsHttpHandler { AllowAutoRedirect = false });

var app = builder.Build();
var logger = app.Logger;

// Upstream targets (docker-compose service URLs).
var monolithUrl = (Environment.GetEnvironmentVariable("MONOLITH_URL") ?? "http://localhost:8080").TrimEnd('/');
var moviesUrl = (Environment.GetEnvironmentVariable("MOVIES_SERVICE_URL") ?? "http://localhost:8081").TrimEnd('/');
var eventsUrl = (Environment.GetEnvironmentVariable("EVENTS_SERVICE_URL") ?? "http://localhost:8082").TrimEnd('/');

// Strangler-fig migration knobs.
var gradualMigration = (Environment.GetEnvironmentVariable("GRADUAL_MIGRATION") ?? "false")
    .Equals("true", StringComparison.OrdinalIgnoreCase);
int.TryParse(Environment.GetEnvironmentVariable("MOVIES_MIGRATION_PERCENT"), out var moviesPercent);
moviesPercent = Math.Clamp(moviesPercent, 0, 100);

var httpClientFactory = app.Services.GetRequiredService<IHttpClientFactory>();

app.MapGet("/health", () => Results.Ok(new { status = true }));

// Everything else is proxied to the appropriate upstream.
app.MapFallback(async (HttpContext ctx) =>
{
    var path = ctx.Request.Path.Value ?? "/";
    string target = SelectTarget(path);
    await Forward(ctx, target);
});

// Decides which upstream handles a request path.
string SelectTarget(string path)
{
    // Movies traffic can be gradually shifted to the new microservice.
    if (path.StartsWith("/api/movies", StringComparison.OrdinalIgnoreCase))
    {
        if (gradualMigration && Random.Shared.Next(100) < moviesPercent)
        {
            logger.LogInformation("Routing {Path} -> movies-service", path);
            return moviesUrl;
        }
        logger.LogInformation("Routing {Path} -> monolith", path);
        return monolithUrl;
    }

    // Events traffic goes to the events microservice.
    if (path.StartsWith("/api/events", StringComparison.OrdinalIgnoreCase))
        return eventsUrl;

    // Everything else still lives in the monolith.
    return monolithUrl;
}

// Streams the incoming request to the target and copies the response back.
async Task Forward(HttpContext ctx, string target)
{
    var client = httpClientFactory.CreateClient("proxy");
    var req = ctx.Request;
    var destination = $"{target}{req.Path}{req.QueryString}";

    using var upstream = new HttpRequestMessage(new HttpMethod(req.Method), destination);

    if (req.ContentLength is > 0 || req.Headers.ContainsKey("Transfer-Encoding"))
    {
        upstream.Content = new StreamContent(req.Body);
        if (req.ContentType is not null)
            upstream.Content.Headers.TryAddWithoutValidation("Content-Type", req.ContentType);
    }

    foreach (var header in req.Headers)
    {
        if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) ||
            header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            continue;
        upstream.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
    }

    try
    {
        using var response = await client.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
        ctx.Response.StatusCode = (int)response.StatusCode;

        foreach (var header in response.Headers)
            ctx.Response.Headers[header.Key] = header.Value.ToArray();
        foreach (var header in response.Content.Headers)
            ctx.Response.Headers[header.Key] = header.Value.ToArray();
        ctx.Response.Headers.Remove("transfer-encoding");

        await response.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Proxy error forwarding to {Target}", destination);
        ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
        await ctx.Response.WriteAsJsonAsync(new { status = "error", error = ex.Message });
    }
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "8000";
app.Run($"http://0.0.0.0:{port}");
