# 0003 — Authentication and token model

- **Status:** Accepted
- **Date:** 2026-07-16
- **Amended:** 2026-08-05 — where the signing keypair comes from, and which host receives it. See *Revision history*.

## Context

The app holds a user's private job-search history behind a personal account, consumed by two first-party clients (a Next.js web app and a Flutter mobile app). Auth is the hardest thing to retrofit, so it ships early. I want the smallest surface that is still correct, with a clear path to passkeys, and without standing up a separate identity server before there's any reason to.

## Decision

**ASP.NET Core Identity for the user store and password hashing; self-issued tokens for API access.**

- **Access token (JWT):** short-lived (5–15 min), signed **asymmetrically** (RS256/ES256) so the worker and any future service validate with the public key only. Claims limited to `sub` (opaque UUIDv7), a token version, and minimal scopes. **No PII in the payload.**
- **Refresh token:** opaque 256-bit value stored **hashed**, one row per device (user id, device label, expiry, revoked flag, family id). Rotation on every use; reuse of a retired token revokes the whole family.
- **Revocation:** per-device logout (delete row); global logout by bumping the user's token-version claim.
- **Client storage:** mobile keeps tokens in the OS keystore; web uses a BFF-style handoff where the Next.js server holds the tokens and the browser gets an HttpOnly, Secure, SameSite cookie — no tokens in `localStorage`.
- **Passkeys (WebAuthn/FIDO2)** are a fast-follow, added as an additional factor with recovery codes issued at registration. Native .NET 10 Identity support is used; a community FIDO2 library is layered in only if attestation policy is later required.
- **Key provisioning:** the ES256 keypair is an input to a deployment, never a build artefact and never committed. The orchestrator owns it — both halves are declared as secret parameters and injected into the API host as `Identity__Jwt__PrivateKeyPem` and `Identity__Jwt__PublicKeyPem`. Locally those parameters resolve from the developer's own user-secrets store; published, they render as environment references whose values the deployment host supplies. **One pair per environment**, and the API is the only host that receives it — it is the only one that issues or validates a token, while the worker composes nothing but Identity's timezone read. Rotating the pair invalidates every access token in circulation but no refresh token, so clients recover on their next rotation without being logged out.

## Consequences

- I own every OAuth-shaped correctness detail (rotation, reuse detection, revocation) — mitigated by keeping to the documented model above and testing it directly.
- Minimal moving parts; no extra stateful service to run.
- The public-key validation model means the worker never needs the signing key.

## Alternatives considered

- **Identity + OpenIddict** (become a standards-compliant OAuth2/OIDC server) — deferred; justified only when social login or third-party API clients appear.
- **Keycloak / a cloud IdP** — deferred/rejected for v1: another stateful service to run, or recurring cost and user PII leaving my control, neither warranted yet.
- **Keeping the keypair in the API's own user-secrets store** — the obvious reading of "user-secrets in development". Rejected because it solves half the problem: it says nothing about how the key reaches a deployed container, so the published topology would still have needed a second, unrelated mechanism. Declaring the pair where the topology is described covers the local loop and the deployment with the same declaration.

## Revision history

- **2026-08-05 — the keypair's provisioning path recorded.** The model above always treated the keys as configuration, but nothing said what supplied them, and in practice nothing did: the test suite generated a pair of its own, so a locally run API started cleanly and then failed on the first registration, and the published topology passed no key variable at all. Declaring both halves as secret orchestrator parameters answers both with one mechanism and keeps the private key in exactly one place per environment. Nothing in the token model itself changed.
