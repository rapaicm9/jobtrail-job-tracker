// Composition root. This host wires modules together and owns cross-cutting
// middleware only - no business logic lives here. Each module contributes via
// its own Add<Module>Module() and Map<Module>Endpoints() extension methods.

using Asp.Versioning;
using JobTrail.Api;
using JobTrail.Api.Idempotency;
using JobTrail.Infrastructure.Events;
using JobTrail.Modules.Analytics;
using JobTrail.Modules.Applications;
using JobTrail.Modules.Billing;
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

// Aspire-wired Redis for the AppHost's "cache" resource - health check,
// telemetry and connection string included. Registered here rather than inside
// one feature because two of them resolve it: the Data Protection key ring and
// the idempotency replay cache.
builder.AddRedisClient(connectionName: "cache");

// Edge hardening: real client address behind Caddy, key ring in Redis, and an
// exact-origin CORS allowlist for the Next.js client.
builder.AddApiForwardedHeaders();
builder.AddApiDataProtection();
builder.AddApiCors();

// A retried mutation must happen once: POSTs carrying an Idempotency-Key are
// answered from the replay cache rather than executed again.
builder.AddApiIdempotency();

// Async glue between modules. Registered before the modules themselves so a
// module's composition method can add its handlers onto a live bus.
builder.Services.AddInProcessEventBus();

// Accounts, credentials and the token store.
builder.AddIdentityModule();

// Per-user entitlements (Free/Pro). Endpoints and policies arrive in later slices.
builder.AddBillingModule();

// The core aggregate: applications and the pipeline. Persistence only for now;
// the aggregate's behaviour and endpoints arrive in later slices.
builder.AddApplicationsModule();

// The dashboard's read model, built from the events the module above publishes.
// Persistence only for now; the projections and endpoints arrive in later slices.
//
// After Applications on purpose, and it is load-bearing rather than tidy: handlers
// for one event run in registration order, and this module's reaction to an
// erasure has to come after the reaction that deletes the events still owed on
// that user's behalf. The other way round, an owed event delivered in between
// would rebuild a row for an account that had just been erased.
builder.AddAnalyticsModule();

// The module also owns validation of its own access tokens; the host just
// turns the scheme on and layers authorization over it.
builder.AddIdentityJwtAuthentication();
builder.Services.AddAuthorization();

// Billing contributes the Feature:* entitlement policies onto that authorization;
// gated endpoints in any module resolve them by name, never referencing Billing.
builder.Services.AddBillingFeaturePolicies();

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

// After authorization on purpose: a key is scoped to the caller it belongs to,
// and a request that was never allowed through must not be able to claim one.
app.UseIdempotencyKeys();

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

// Authenticated account self-service; inherits the group's general per-IP budget.
api.MapAccountEndpoints();

// Authenticated billing: plan status and the mocked Pro purchase; same budget.
api.MapBillingEndpoints();

// Authenticated application data: the company type-ahead picker for now; same budget.
api.MapApplicationsEndpoints();

// Developer shortcuts (grant Pro without a purchase) exist only in Development -
// mapping them nowhere else is what keeps them out of production entirely.
if (app.Environment.IsDevelopment())
{
    api.MapBillingDevEndpoints();
}

await app.RunAsync();
