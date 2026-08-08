// A probe must never be cached or prerendered - a response baked at build time
// says the process is alive long after it stopped being so.
export const dynamic = "force-dynamic";

// 200 with a body rather than a bare 204, to match what the .NET hosts answer on
// the same paths. Aspire's health check treats only 200 as healthy unless told
// otherwise, and the proxy that will front all three of these gets configured
// once - so the odd one out would be the one that costs an afternoon.
export function GET() {
  return new Response("Healthy", {
    status: 200,
    headers: { "content-type": "text/plain" },
  });
}
