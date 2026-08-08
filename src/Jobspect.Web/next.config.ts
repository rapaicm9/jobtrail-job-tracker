import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Required by the AppHost, which validates it and fails the build without it:
  // publishing renders this project as a container that runs server.js.
  output: "standalone",

  // Two instances of this service must agree on both of these or a client
  // holding a page from one gets 404s for assets from the other. Neither is set
  // locally, and both fall back to the per-build defaults, so wiring them now
  // costs nothing and removes a class of failure that only appears the day a
  // second instance exists.
  //
  // Returning null asks Next for its own default rather than a fixed string, so
  // an unset GIT_SHA cannot pin every build to the same id.
  generateBuildId: () => process.env.GIT_SHA ?? null,
  deploymentId: process.env.DEPLOYMENT_VERSION,
};

export default nextConfig;
