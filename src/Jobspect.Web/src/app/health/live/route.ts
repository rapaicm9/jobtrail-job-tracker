// A probe must never be cached or prerendered - a response baked at build time
// says the process is alive long after it stopped being so.
export const dynamic = "force-dynamic";

// 200 and a body, matching /health/ready and the .NET hosts. Liveness answers
// the same way for now; readiness gains real checks when there is a session
// store whose absence should take this instance out of rotation.
export function GET() {
  return new Response("Healthy", {
    status: 200,
    headers: { "content-type": "text/plain" },
  });
}
