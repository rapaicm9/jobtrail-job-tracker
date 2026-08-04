// Composition root. This host wires modules together and owns cross-cutting
// middleware only - no business logic lives here. Each module contributes via
// its own Add<Module>Module() and Map<Module>Endpoints() extension methods.

using System.Text.Json.Serialization;
using Asp.Versioning;
using Jobspect.Api;
using Jobspect.Api.Idempotency;
using Jobspect.Api.OpenApi;
using Jobspect.Infrastructure.Events;
using Jobspect.Modules.Analytics;
using Jobspect.Modules.Applications;
using Jobspect.Modules.Billing;
using Jobspect.Modules.Identity;
using Jobspect.Modules.Notifications;

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

// A number is a number. The web defaults accept a quoted one too, which is a
// leniency no client asked for and which the described contract cannot state
// without widening every integer to "integer or string" - and from there into
// every generated client. Refusing the quoted form keeps one shape per field on
// both sides of the contract. Query and route values are bound before any of
// this and are unaffected.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

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

// The reminder engine's store. This host is where reminders are armed - it is
// where the dispatcher runs and the date-bearing events arrive - while the worker
// composes the same module to deliver them.
//
// After Applications for the reason the module above gives: an erasure here has to
// follow the one that deletes the events still owed on that account's behalf, or
// one of them would arm a reminder for an account that has just been erased.
builder.AddNotificationsModule();

// And what it does with those events. A second call, made by this host only: the
// worker composes the store above and the schedule, but never these - it has no
// dispatcher to fire them and does not compose the Identity contract they read the
// owner's timezone through.
builder.AddNotificationsConsumers();

// And what it serves over HTTP: the feed those reminders are read through - the only
// channel that reaches a person in this release - and the follow-up automation an
// account configures. A third call from this host, because the worker serves no HTTP
// and would carry handlers it could never reach.
builder.AddNotificationsApi();

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
    })
    // The explorer resolves the version *route parameter* to the concrete
    // version, so the described paths read /api/v1/... as a caller types them
    // rather than the /api/v{version}/... the routes are declared with.
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    })
    // Must follow AddApiVersioning: this is the versioning builder's own
    // AddOpenApi, which registers a document per discovered version, not the
    // service-collection one that would register a single unversioned document.
    .AddOpenApi(OpenApiConfiguration.Describe);

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

// The dashboard's figures, aggregated from the read model; same budget.
api.MapAnalyticsEndpoints();

// The reminder feed: what the engine armed, swept and delivered, finally readable.
api.MapNotificationsEndpoints();

// Developer shortcuts (grant Pro without a purchase) exist only in Development -
// mapping them nowhere else is what keeps them out of production entirely. The
// described contract is served under the same rule: it is generated from the live
// endpoint graph, so it would describe those shortcuts to anyone who asked.
if (app.Environment.IsDevelopment())
{
    api.MapBillingDevEndpoints();

    app.MapOpenApi(OpenApiConfiguration.JsonRoute).WithDocumentPerVersion();
    app.MapOpenApi(OpenApiConfiguration.YamlRoute).WithDocumentPerVersion();

    // And something to read it with, until there is a client that does.
    app.MapApiReference();
}

await app.RunAsync();
