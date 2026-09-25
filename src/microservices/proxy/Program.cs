using ProxyService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("proxy").ConfigurePrimaryHttpMessageHandler(() =>
    new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton(ProxyOptions.FromEnvironment());
builder.Services.AddSingleton<Proxy>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = true }));

// Everything else is delegated to the Proxy for upstream routing.
app.MapFallback((HttpContext ctx, Proxy proxy) => proxy.ForwardAsync(ctx));

var port = Environment.GetEnvironmentVariable("PORT") ?? "8000";
app.Run($"http://0.0.0.0:{port}");
