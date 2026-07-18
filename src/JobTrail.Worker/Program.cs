using JobTrail.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Same OpenTelemetry, health-check, service-discovery and resilience wiring as
// the API. This host has no HTTP surface yet, so the health checks are
// registered but not exposed over HTTP; the /health endpoints arrive when the
// worker gains its health listener alongside Hangfire and the stream consumers.
builder.AddServiceDefaults();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
