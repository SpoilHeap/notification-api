# 0003. Delegate UI authentication to HA Ingress, with no roles

- Status: Accepted
- Date: 2026-11-23

## Context

The web UI manages contacts, phone numbers, groups and provider credentials. It needs authentication. Building our own login (passwords, account management, a second login screen) adds work and attack surface for a single-premises app whose users already log in to HA. HA Ingress already authenticates the user and proxies requests to the add-on with identity headers (`X-Remote-User-Id`, `X-Remote-User-Name`, `X-Remote-User-Display-Name`).

The API (`/api/v1`) is called by the HA integration, not by a browser, so it needs a separate scheme.

## Decision

**UI:**
- No own login. The add-on trusts the Ingress identity headers.
- Requests are trusted only when they come from the Ingress proxy (`172.30.32.2`, configurable). IPv4-mapped IPv6 addresses (`::ffff:`) are unwrapped before comparing. Ingress headers from any other source are ignored, and UI requests from elsewhere are rejected.
- `X-Ingress-Path` from the trusted proxy is applied as the request PathBase, and `<base href>` is derived per request.
- `panel_admin: true`: only HA administrators see the panel.
- **No roles.** Everyone who can open the panel has full access. The identity headers are used only to record who made each change in the AuditLog.

**API:**
- Bearer token. The add-on generates a 256-bit token on first start, stores it hashed (SHA-256), and publishes the plaintext through **Supervisor discovery**. The user never sees it.
- Kept even on the internal network: it's cheap, and it protects against other add-ons.

Implemented as two `AuthenticationHandler` schemes, `Ingress` and `ApiToken`, with fallback policy = deny. No ASP.NET Identity.

## Alternatives considered

- **Own login with ASP.NET Identity.** A second login screen and account management, for no gain over HA's.
- **Roles (e.g. admin vs. viewer).** No use case in v1. HA already restricts the panel to admins.
- **Manual token entry for the API.** Kept only as a fallback if discovery fails (spec §9.4).

## Consequences

- Security of the UI rests on the source-IP check. It must be tested: requests from other IPs are rejected even with valid-looking headers (AC80).
- Local development needs a fake: `Ingress:DevFakeIngress` injects fake identity headers and treats requests as coming from Ingress. Startup fails if it is enabled outside the Development environment, and a test covers that.
- No user management to build or maintain.
- Spike 3 (2026-11-25) confirmed the headers arrive and the remote IP is `172.30.32.2`.
