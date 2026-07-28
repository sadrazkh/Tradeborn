using System.Text.Json;
using System.Text.Json.Serialization;
using Tradeborn.Web.Prototype;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// TimeProvider is injected rather than calling DateTimeOffset.UtcNow directly.
// See docs/architecture/REALTIME_AND_TIME_MODEL.md §7 — the server clock is the only clock,
// and tests substitute FakeTimeProvider.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health/live");

// ---------------------------------------------------------------------------------------
// Phase 0 prototype API.
//
// Even in the prototype the world layout comes from the server. This is not ceremony: it
// establishes the server-authoritative boundary (docs/architecture/SECURITY_MODEL.md §3)
// before any economy exists, so the client is never written against local truth.
// Replaced by the real Cities module in Phase 1.
// ---------------------------------------------------------------------------------------
app.MapGet("/api/prototype/city", (TimeProvider clock) =>
    TypedResults.Ok(PrototypeCity.Create(clock.GetUtcNow())));

app.MapFallbackToFile("index.html");

app.Run();
