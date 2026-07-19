// Composition root. This host wires modules together and owns cross-cutting
// middleware only - no business logic lives here. Each module contributes via
// its own Add<Module>Module() and Map<Module>Endpoints() extension methods.

using Asp.Versioning;
using JobTrail.Api;
using JobTrail.Modules.Identity;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery and HTTP resilience, shared
// with the worker so both hosts observe and self-report identically.
builder.AddServiceDefaults();

// Every error leaves this host as RFC 9457 ProblemDetails - including
// unhandled exceptions, which the exception handler middleware below converts
// without ever leaking a stack trace.
builder.Services.AddProblemDetails();

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

app.UseExceptionHandler();

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
