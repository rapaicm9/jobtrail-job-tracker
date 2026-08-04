// Composition root for the background host. It runs the reminder engine's
// schedule and nothing else - no HTTP surface, no business logic of its own.
//
// The API host consumes the events that arm reminders, because that is where the
// outbox dispatcher runs; this host is where they fire. One table, one writer, one
// reader.

using Jobspect.Modules.Identity;
using Jobspect.Modules.Notifications;

var builder = Host.CreateApplicationBuilder(args);

// Same OpenTelemetry, health-check, service-discovery and resilience wiring as
// the API. This host has no HTTP surface yet, so the health checks are
// registered but not exposed over HTTP; the /health endpoints arrive when the
// worker gains its health listener.
builder.AddServiceDefaults();

// The reminder store, and the schedule its recurring work runs on. Deliberately
// two calls: the API host composes the first of them and must never compose the
// second, because two schedulers over one job store would each believe itself the
// only one.
builder.AddNotificationsModule();

// One read across a module boundary, and the narrowest one that exists: the
// follow-up scan raises reminders at 11:00 in the owner's own timezone, and that
// timezone is Identity's. The method registers the account store and the profile
// query over it - no credentials, no tokens, and no outbox dispatcher, which is
// the part that would matter: this host registers no event handlers, so a
// dispatcher here would claim owed events and deliver them nowhere.
builder.AddIdentityProfileQuery();

builder.AddNotificationsScheduler();

var host = builder.Build();
host.Run();
