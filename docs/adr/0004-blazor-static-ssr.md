# 0004. Blazor Static SSR for the web UI

- Status: Accepted
- Date: 2026-11-23 (confirmed by spike on 2026-11-25)

## Context

The UI is mostly CRUD forms: contacts, groups, routing, settings. It is served only through HA Ingress, which puts the app under a per-session prefix (`/api/hassio_ingress/<token>/`) inside an iframe. Anything that depends on absolute paths or long-lived connections is at risk:

- Interactive Server mode needs a SignalR circuit (WebSocket) through the Ingress proxy, with reconnect handling when it drops.
- Interactive WebAssembly downloads the runtime under the Ingress prefix and adds a second hosting model.
- A browser refresh (F5) reloads the HA page, so the iframe goes back to the add-on root. Deep links can't be relied on.

## Decision

Use **Blazor Static SSR** (.NET 10) with **no interactive render modes** (no SignalR circuit, no WASM).

- Components render on the server. Forms post with `[SupplyParameterFromForm]`, using Post/Redirect/Get.
- Enhanced navigation and enhanced forms give SPA-like transitions.
- Small dynamic bits (the Telegram link QR code, polling link status) use plain JS or htmx.
- `X-Ingress-Path` → `UsePathBase`, and `<base href>` is set per request.
- Antiforgery stays on.

## Alternatives considered

- **Interactive Server.** Circuit state and a WebSocket through Ingress. More moving parts for forms that don't need them.
- **Interactive WebAssembly.** Larger download, client/server split, and asset paths under the Ingress prefix.
- **A separate SPA framework.** A second toolchain for a small admin UI.

## Consequences

- Plain HTTP request/response. Easy to test with `WebApplicationFactory` and bUnit.
- No rich client-side interactivity without JS. Acceptable for v1.
- Spike 2 (HA OS, 2026-11-25) passed: `<base href>` from `X-Ingress-Path`, static assets, `blazor.web.js`, enhanced navigation and antiforgery form posts all work. Enhanced navigation is signalled by `Accept: text/html; blazor-enhanced-nav=on`.
- The spike found that restoring with only the `.csproj` files and then publishing with `--no-restore` silently drops `_framework/blazor.web.js` (404, which Ingress shows as a MIME error). In-process tests can't catch this, because `WebApplicationFactory` runs from the build output, not the Docker publish output. CI runs [`scripts/smoke-test-image.sh`](../../scripts/smoke-test-image.sh) against the built image instead.
