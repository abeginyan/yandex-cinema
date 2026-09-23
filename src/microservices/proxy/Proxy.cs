namespace ProxyService;

// Configuration for the gateway, sourced from environment variables.
public sealed class ProxyOptions
{
    public required string MonolithUrl { get; init; }
    public required string MoviesServiceUrl { get; init; }
    public required string EventsServiceUrl { get; init; }
    public bool GradualMigration { get; init; }
    public int MoviesMigrationPercent { get; init; }

    public static ProxyOptions FromEnvironment()
    {
        int.TryParse(Environment.GetEnvironmentVariable("MOVIES_MIGRATION_PERCENT"), out var pct);
        return new ProxyOptions
        {
            MonolithUrl = Url("MONOLITH_URL", "http://localhost:8080"),
            MoviesServiceUrl = Url("MOVIES_SERVICE_URL", "http://localhost:8091"),
            EventsServiceUrl = Url("EVENTS_SERVICE_URL", "http://localhost:8082"),
            GradualMigration = (Environment.GetEnvironmentVariable("GRADUAL_MIGRATION") ?? "false")
                .Equals("true", StringComparison.OrdinalIgnoreCase),
            MoviesMigrationPercent = Math.Clamp(pct, 0, 100),
        };

        static string Url(string name, string fallback) =>
            (Environment.GetEnvironmentVariable(name) ?? fallback).TrimEnd('/');
    }
}

// Routes incoming API requests to the monolith or the extracted microservices,
// applying strangler-fig gradual migration for /api/movies traffic.
public sealed class Proxy
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProxyOptions _options;
    private readonly ILogger<Proxy> _logger;

    public Proxy(IHttpClientFactory httpClientFactory, ProxyOptions options, ILogger<Proxy> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    // Decides which upstream should handle a request path.
    public string SelectTarget(string path)
    {
        // Movies traffic can be gradually shifted to the new microservice.
        if (path.StartsWith("/api/movies", StringComparison.OrdinalIgnoreCase))
        {
            if (_options.GradualMigration && Random.Shared.Next(100) < _options.MoviesMigrationPercent)
            {
                _logger.LogInformation("Routing {Path} -> movies-service", path);
                return _options.MoviesServiceUrl;
            }

            _logger.LogInformation("Routing {Path} -> monolith", path);
            return _options.MonolithUrl;
        }

        // Events traffic goes to the events microservice.
        if (path.StartsWith("/api/events", StringComparison.OrdinalIgnoreCase))
            return _options.EventsServiceUrl;

        // Everything else still lives in the monolith.
        return _options.MonolithUrl;
    }

    // Forwards the current request to the selected upstream and copies the response back.
    public async Task ForwardAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        var target = SelectTarget(req.Path.Value ?? "/");
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
            var client = _httpClientFactory.CreateClient("proxy");
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
            _logger.LogError(ex, "Proxy error forwarding to {Target}", destination);
            ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
            await ctx.Response.WriteAsJsonAsync(new { status = "error", error = ex.Message });
        }
    }
}
