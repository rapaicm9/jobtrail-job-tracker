import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";

// Sits beside `app/` rather than at the repository root: with a `src` directory
// Next only picks this file up as a sibling of the route tree, and one placed a
// level up is silently never invoked.
//
// What belongs here is work that has to happen per request and before a route
// renders. Authorization does not: this runs on prefetches too, and a check
// here would be a check the page still has to repeat. Session reads and the
// CSP nonce land here once there is a session to read.

const SECURITY_HEADERS: Record<string, string> = {
  // The API is same-origin through this service, so no page has a reason to be
  // framed or to leak a full URL cross-origin. Paths carry application ids.
  "X-Frame-Options": "DENY",
  "X-Content-Type-Options": "nosniff",
  "Referrer-Policy": "strict-origin-when-cross-origin",
  "Cross-Origin-Opener-Policy": "same-origin",
  "X-Permitted-Cross-Domain-Policies": "none",
};

export function proxy(request: NextRequest) {
  const response = NextResponse.next({ request });

  for (const [header, value] of Object.entries(SECURITY_HEADERS)) {
    response.headers.set(header, value);
  }

  return response;
}

export const config = {
  // Everything except the paths that never render a document. Static assets and
  // the image optimizer are served thousands of times per session and gain
  // nothing from a header pass; the health probes answer text/plain and are
  // polled by a container runtime that does not care.
  matcher: ["/((?!_next/static|_next/image|health/|favicon.ico|.*\\.woff2$).*)"],
};
