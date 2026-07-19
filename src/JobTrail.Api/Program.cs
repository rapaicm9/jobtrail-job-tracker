// Composition root. This host wires modules together and owns cross-cutting
// middleware only - no business logic lives here. Each module contributes via
// its own Add<Module>Module() and Map<Module>Endpoints() extension methods.

using Asp.Versioning;
using JobTrail.Api;
using JobTrail.Modules.Identity;

var builder = WebApplication.CreateBuilder(args);

// No "Server: Kestrel" banner; version fingerprints help only an attacker.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

// OpenTelemetry, health checks, service discovery and HTTP resilience, shared
// with the worker so both hosts observe and self-report identically.
builder.AddServiceDefaults();

// Every error leaves this host as RFC 9457 ProblemDetails - including
// unhandled exceptions, which the exception handler middleware below converts
// without ever leaking a stack trace.
builder.Services.AddProblemDetails();

// Edge hardening: real client address behind Caddy, key ring in Redis, and an
// exact-origin CORS allowlist for the Next.js client.
builder.AddApiForwardedHeaders();
builder.AddApiDataProtection();
builder.AddApiCors();

// Accounts, credentials and the token store.
builder.AddIdentityModule();

// The module also owns validation of its own access tokens; the host just
// turns the scheme on and layers authorization over it.
builder.AddIdentityJwtAuthentication();
builder.Services.AddAuthorization();

// URL-segment versioning (/api/v1/...) from day one - deployed mobile clients
// can't be force-upgraded onto a changed contract.
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1);
    options.ReportApiVersions = true;
});

// Per-IP request budgets; policies attach to route groups below.
builder.AddApiRateLimiting();

var app = builder.Build();

// First in the pipeline: everything downstream - rate-limit partitions,
// logging, scheme checks - must see the restored client address and scheme.
app.UseForwardedHeaders();

app.UseExceptionHandler();

app.UseSecurityHeaders();

// CORS ahead of the limiter so preflights are answered, not throttled.
app.UseCors();

// Before authentication on purpose: a throttled request is turned away without
// paying the bearer validation's per-request token-version DB read.
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// /health/ready and /health/live for the proxy and the orchestrator.
app.MapDefaultEndpoints();

var apiVersions = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1))
    .ReportApiVersions()
    .Build();

var api = app
    .MapGroup("/api/v{version:apiVersion}")
    .WithApiVersionSet(apiVersions)
    .RequireRateLimiting(RateLimitingConfiguration.GlobalPolicy);

// The innermost policy wins on these endpoints: the auth surface swaps the
// general per-IP budget for the stricter fixed window.
api.MapIdentityEndpoints()
    .RequireRateLimiting(RateLimitingConfiguration.AuthPolicy);

await app.RunAsync();
