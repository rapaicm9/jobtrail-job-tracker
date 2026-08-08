// Serves the standalone build, which is the tree the container runs.
//
// `next start` refuses to serve it and falls back to the ordinary build output,
// so a suite pointed at `next start` would be testing something that never
// ships. Next writes server.js without the static assets beside it — they are
// copied by the Dockerfile in a normal deployment, and by this script locally.

import { cpSync, existsSync } from "node:fs";
import { spawn } from "node:child_process";

const root = ".next/standalone";

if (!existsSync(`${root}/server.js`)) {
  console.error("No standalone build found. Run `pnpm build` first.");
  process.exit(1);
}

cpSync(".next/static", `${root}/.next/static`, { recursive: true });
if (existsSync("public")) cpSync("public", `${root}/public`, { recursive: true });

spawn(process.execPath, [`${root}/server.js`], {
  stdio: "inherit",
  env: process.env,
}).on("exit", (code) => process.exit(code ?? 0));
