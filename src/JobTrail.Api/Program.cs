// Composition root. This host wires modules together and owns cross-cutting
// middleware only - no business logic lives here. Each module contributes via
// its own Add<Module>Module() and Map<Module>Endpoints() extension methods.

using JobTrail.Modules.Identity;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery and HTTP resilience, shared
// with the worker so both hosts observe and self-report identically.
builder.AddServiceDefaults();

// Accounts, credentials and the token store.
builder.AddIdentityModule();

var app = builder.Build();

// /health/ready and /health/live for the proxy and the orchestrator.
app.MapDefaultEndpoints();

await app.RunAsync();
