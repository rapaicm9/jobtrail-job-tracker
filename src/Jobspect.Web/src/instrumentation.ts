// Runs once per server instance, before the first request is handled.
//
// Beside `app/` for the same reason as proxy.ts: with a `src` directory this is
// where Next looks, and a copy at the repository root would never run.
//
// The OpenTelemetry registration goes here, pointed at the collector the .NET
// hosts already report to. Until then this file exists so that the seam is a
// known place rather than a decision someone makes twice.

export function register() {}
