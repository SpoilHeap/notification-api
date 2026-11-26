# Notification API: Engineering Standards & Build Expectations (v1)

Companion to `notification_api_spec.md`. These rules apply to the .NET add-on repo (`notification-api`) and, where noted, to the Python HA integration repo (`notification-api-ha`).

The aim is **professional and pragmatic**: clean boundaries and thorough tests, without enterprise ceremony that a single-container, single-premises app doesn't need.

---

## 1. Platform baseline

| Item | Standard |
|---|---|
| Runtime | **.NET 10 (LTS)**. .NET 8 reaches end of support in Nov 2026. |
| Language | Latest C# for the SDK. `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` |
| SDK pinning | `global.json` pins the SDK feature band (`rollForward: latestFeature`) |
| Package versions | **Central Package Management** (`Directory.Packages.props`). No versions in individual `.csproj` files. |
| Shared build settings | `Directory.Build.props`: nullable, analyzers, `TreatWarningsAsErrors=true`, deterministic builds |
| Lock files | **None.** Versions are pinned by Central Package Management with transitive pinning. SDK-injected packages make lock files depend on the SDK patch ([ADR 0011](adr/0011-no-nuget-lock-files.md)). |

---

## 2. Architecture: Clean Architecture (pragmatic)

### 2.1 Projects and the dependency rule

```
src/
├── NotificationApi.Domain/          # Entities, value objects, domain rules. No dependencies.
├── NotificationApi.Application/     # Use cases, ports (interfaces), DTOs. Depends on Domain only.
├── NotificationApi.Infrastructure/  # EF Core, SQLite, Telegram, SMS.to, Supervisor, HA webhook. Implements Application ports.
└── NotificationApi.Web/             # Composition root: Minimal APIs, Blazor SSR UI, auth handlers, hosted services.
tests/
├── NotificationApi.Domain.Tests/
├── NotificationApi.Application.Tests/
├── NotificationApi.Infrastructure.Tests/
├── NotificationApi.Web.Tests/              # API + UI integration tests
└── NotificationApi.ArchitectureTests/
```

```
Web ──► Application ──► Domain
 │            ▲
 └──► Infrastructure ─┘     (Infrastructure implements Application interfaces)
```

- **Domain** references nothing (no EF, no ASP.NET, no `HttpClient`).
- **Application** references only Domain and `Microsoft.Extensions.*.Abstractions` where needed (logging, options).
- **Infrastructure** is the only project that knows about EF Core, HTTP providers and the Supervisor.
- **Web** wires everything together and contains no business logic. Endpoints and pages call Application use cases.
- The rule is **enforced by architecture tests** (§4.5), not by convention.

### 2.2 Domain

- Rich entities with invariants enforced in constructors and methods. No public setters on aggregate state.
  - `Group.AddMember(...)` rejects cycles.
  - `Contact.SetRouting(priority, channels)` validates channels against the contact's endpoints.
  - `Delivery` is a small state machine: `Queued → Sending → Sent | Retrying → Failed`. Illegal transitions throw.
- Value objects: `Handle` (`contact:alice` / `group:family`), `PhoneNumber` (E.164), `TelegramChatId`, `Priority`, `ChannelType`.
- **Recipient expansion** (nested groups, no dedup) is a pure domain service, so it's fully unit-testable.
- Strongly-typed IDs (`ContactId`, `GroupId`, `NotificationId`, `DeliveryId`).

### 2.3 Application

- One class per use case (`SendNotification`, `CreateContact`, `LinkTelegramEndpoint`, `ProcessDueDeliveries`, `PurgeExpiredLogs`, …), each with a single `HandleAsync`.
- **No mediator library.** Use cases are injected directly. (MediatR is now commercially licensed, and the indirection isn't worth it here.)
- **Ports** (interfaces) defined here and implemented in Infrastructure:
  - `INotificationChannel` (`ChannelType`, `SendAsync(message, endpoint, ct) → ChannelResult`)
  - `IContactRepository`, `IGroupRepository`, `INotificationRepository`, `IUnitOfWork`
  - `IHaEventPublisher` (webhook), `IDiscoveryPublisher` (Supervisor), `ISecretProtector`
  - `TimeProvider` (from the BCL) for all clock access. **Never call `DateTime.UtcNow` directly.**
- Errors: expected failures are returned as a `Result<T>` / error type (validation, unknown handle, zero deliveries). Exceptions are reserved for exceptional conditions. Web maps errors to RFC 9457 `ProblemDetails`.
- Input validation at the Application boundary with **hand-written validators** (one per request type, returning field-level errors, no validation library). Value objects enforce their own format (E.164, handle syntax).

### 2.4 Infrastructure

- EF Core + SQLite. Entity configuration through `IEntityTypeConfiguration<T>`, with no data annotations in Domain.
- **Migrations are checked in** and applied on startup (a single-instance app, so this is safe). A test checks that the model has no pending migration.
- HTTP providers use **typed `HttpClient`s** through `IHttpClientFactory`, with resilience from `Microsoft.Extensions.Http.Resilience` (timeouts only. Retries belong to the delivery algorithm, not the HTTP layer, so attempts aren't counted twice).
- Secrets (bot token, SMS.to key, API token) are encrypted at rest with ASP.NET Data Protection, with keys persisted to `/data/keys`.

### 2.5 Web

- Minimal API endpoints grouped per feature (`MapGroup("/api/v1")`). Request and response contracts are separate records from domain types.
- **OpenAPI** document generated with `Microsoft.AspNetCore.OpenApi`, and the checked-in `openapi.json` is diffed in CI (contract stability for the HA integration).
- Blazor Static SSR UI. Components are thin and call Application use cases. Antiforgery is on.
- Two authentication schemes (`Ingress`, `ApiToken`). Every endpoint has an explicit auth policy, with fallback policy = deny.
- Hosted services (`BackgroundService`) are thin loops that call Application use cases (`ProcessDueDeliveries`, `PollTelegramUpdates`, `PurgeExpiredLogs`).

### 2.6 Configuration

- Options pattern with `ValidateDataAnnotations()` / `ValidateOnStart()`. The app fails fast on invalid config.
- Bootstrap settings (data path, port, trusted proxy IP, log level) come from env vars / add-on options. Runtime settings (retention, retries, provider credentials) live in the DB and are edited in the UI.

---

## 3. Coding standards

- `.editorconfig` at the repo root defines naming, formatting and analyzer severities.
- Analyzers: the built-in .NET analyzers (`AnalysisLevel=latest-recommended`), plus `Microsoft.VisualStudio.Threading.Analyzers` for async correctness.
- `dotnet format --verify-no-changes` runs in CI.
- Async all the way, with `CancellationToken` passed through every async API. No `.Result` / `.Wait()`.
- `sealed` by default for classes not designed for inheritance. `record` for DTOs and value objects.
- No static mutable state. No service locator.
- Public APIs in Application/Domain get XML docs only where intent isn't obvious. Don't write comments that repeat the code.

---

## 4. Testing strategy

### 4.1 Tooling

| Purpose | Library |
|---|---|
| Test framework | **xUnit v3** on **Microsoft.Testing.Platform** (`global.json` `test.runner`). See [ADR 0010](adr/0010-xunit-v3-on-microsoft-testing-platform.md). |
| Assertions | **Shouldly** (avoids FluentAssertions v8 commercial licensing) |
| Test doubles | **NSubstitute**, or hand-rolled fakes (preferred for ports) |
| Time | `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) |
| HTTP fakes | **WireMock.Net** for Telegram/SMS.to/HA webhook contract tests |
| API integration | `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`) + SQLite in-memory |
| UI components | **bUnit** for rendering SSR components |
| UI end-to-end (smoke) | **Playwright for .NET** for a few critical flows |
| Architecture | **NetArchTest.Rules** (or ArchUnitNET) |
| Coverage | `Microsoft.Testing.Extensions.CodeCoverage` (Cobertura) + ReportGenerator |

### 4.2 Unit tests (Domain + Application): the bulk of the suite

Required coverage of behaviour, not just lines:
- Recipient expansion: single contact, flat group, nested groups, **duplicates preserved** (`via` path correct), empty group, contact with no verified endpoint for the priority.
- Cycle detection on group membership (direct, indirect, self).
- Channel selection: routing per priority, `channels` override ∩ verified endpoints, override to an unverified channel.
- Delivery state machine: every legal and illegal transition.
- Retry schedule: 3 retries at 30 s using `FakeTimeProvider`, then `Failed` + an event.
- Idempotency: the same key within 24 h returns the original result. After 24 h it's treated as new.
- Formatting: Telegram HTML escaping (`<`, `&` in user text), SMS plain-text rendering.
- Value objects: E.164 parsing, handle parsing, invalid inputs.
- Retention purge: rows older than `retention_days` removed, newer kept.

### 4.3 Integration tests (Infrastructure + Web)

- **Persistence:** repositories against real SQLite (in-memory, shared connection per test). Migrations apply cleanly. No pending model changes.
- **Channels:** `TelegramChannel` and `SmsToChannel` against WireMock: correct request shape, auth header, success parsing, error/timeout mapping to `ChannelResult`.
- **API:** `WebApplicationFactory` with fakes replacing the channels:
  - `POST /notify` happy path → 202 + deliveries. 400 / 401 / 422 cases.
  - `GET /recipients` exposes no PII (assert on JSON shape).
  - Fallback deny policy: an unauthenticated request to every endpoint → 401 (enumerate endpoints automatically).
- **Ingress auth:** request from `172.30.32.2` with headers → allowed. From any other IP → rejected, **even with valid headers**. Missing headers → rejected.
- **Ingress path base:** pages render with the correct `<base href>` when `X-Ingress-Path` is set.
- **Webhook publisher:** posts the correct event payload and secret to WireMock.

### 4.4 Acceptance tests

- One automated test per acceptance criterion in the spec (`AcceptanceTests` folder or trait `Category=Acceptance`), named after the criterion, e.g. `AC03_Contact_in_two_groups_receives_two_messages`.
- Run end-to-end through the API with fake channels that record what was sent.
- A short Playwright smoke suite: create a contact → add to group → generate the Telegram link → send a test notification from the UI.

### 4.5 Architecture tests

- Domain has no dependency on Application, Infrastructure, Web, EF Core or ASP.NET.
- Application has no dependency on Infrastructure, Web or EF Core.
- Web doesn't reference Infrastructure types except in the composition root (`Program.cs` / `DependencyInjection` extensions).
- All `INotificationChannel` implementations live in Infrastructure and are `sealed`.

### 4.6 Coverage and quality gates

- **Line coverage ≥ 80% overall, ≥ 90% for Domain + Application.** A PR fails if coverage drops below these.
- Coverage is a floor, not the goal. Reviewers check that the behaviours in §4.2 have tests.
- Tests are deterministic: no real network, no `Thread.Sleep`, no wall-clock time. Flaky tests are fixed or quarantined within one working day.

### 4.7 Python integration repo

- `pytest` + `pytest-homeassistant-custom-component`: config flow (discovery + manual), entity creation from `/recipients`, the send action, the webhook → event mapping, and refresh on `recipients.changed`.
- `ruff` (lint + format), `mypy --strict` on the integration package.
- CI runs the **hassfest** and **HACS validation** GitHub Actions.
- Coverage ≥ 90% (normal for HA core integrations).

---

## 5. Observability

- `Microsoft.Extensions.Logging` with **structured** messages (source-generated `LoggerMessage` for hot paths). JSON console output in the container, so it shows in the add-on log tab.
- **No PII or secrets in logs**: phone numbers are masked (`+3538****567`), message bodies are never logged at Information or above, and tokens are never logged.
- Correlation: `notification_id` / `delivery_id` in log scopes.
- `/health` (liveness) and `/health/ready` (DB reachable, channels configured) through ASP.NET Health Checks. The Supervisor watchdog uses liveness.

---

## 6. Security baseline

- Fallback authorisation policy = deny. Every endpoint opts into a scheme explicitly.
- API token: 256-bit random, stored **hashed** (SHA-256) in the DB. The plaintext goes only to discovery. Constant-time comparison.
- Ingress scheme trusts the headers only from the configured proxy IP.
- Webhook calls to HA are signed with the shared secret.
- Antiforgery on all UI forms. Output encoding via Razor defaults. No `MarkupString` with user input.
- Dependencies: **Dependabot** (NuGet, GitHub Actions, Docker base images) + `dotnet list package --vulnerable` in CI.
- Container: non-root user (exception: the add-on image runs as root, see [ADR 0009](adr/0009-container-runs-as-root.md)), chiseled/minimal base image, **Trivy** scan in CI (fails on HIGH/CRITICAL that have a fix available).
- No secrets in the repo. CI uses GitHub `GITHUB_TOKEN` for GHCR.

---

## 7. Source control and workflow

- `main` is protected: changes only through PR, CI must be green, linear history (squash merge).
- **Conventional Commits** (`feat:`, `fix:`, `chore:` …), used to generate the changelog.
- **SemVer.** The add-on `version` in `config.yaml` = image tag = git tag `vX.Y.Z`.
- `CHANGELOG.md` in the add-on folder (HA shows it in the update dialog).
- **ADRs** (`docs/adr/NNNN-title.md`, MADR format) for significant decisions. The current set is in [docs/adr](adr/).

---

## 8. CI/CD pipeline (GitHub Actions)

**On every PR and push to `main`:**
1. Restore → `dotnet format --verify-no-changes` → build (warnings as errors).
2. Unit + architecture tests.
3. Integration + acceptance tests.
4. Coverage report + gate (§4.6), published as a PR comment / artifact.
5. OpenAPI diff against the checked-in `openapi.json` (fails on an unacknowledged change).
6. Vulnerable package check.
7. Docker build for `amd64` + `aarch64` (no push on PRs) + Trivy scan.
   - **Restore with the full source present.** Restoring with only the `.csproj` (the usual layer-caching trick) and then publishing with `--no-restore` silently drops Blazor's framework assets (`_framework/blazor.web.js` → 404). Found in the spike on 2026-11-25.
   - **Image smoke test:** `scripts/smoke-test-image.sh` runs the built image and asserts that `_framework/blazor.web.js` returns 200 with a JavaScript content type (catches the issue above). In-process tests can't catch it, because `WebApplicationFactory` runs from the build output.
8. Add-on lint (`frenck/action-addon-linter`).

**On a tag `vX.Y.Z`:**
1. All of the above.
2. Push multi-arch images to GHCR (`ghcr.io/<owner>/notification-api-{arch}:X.Y.Z`).
3. Check that `config.yaml` `version` matches the tag.
4. Create a GitHub Release with changelog notes.

Playwright smoke tests run on the release pipeline (and nightly), not on every PR, to keep PRs fast.

---

## 9. Local developer experience

- `dotnet run --project src/NotificationApi.Web` works with no extra setup:
  - `Ingress:DevFakeIngress` fakes the Ingress headers. It's allowed only in the Development environment: startup fails otherwise, which `DevFakeIngressGuardTests` covers.
  - The SQLite file and Data Protection keys go in the repo-root `.data/` (gitignored).
  - Channels will default to a **console/fake channel** once channels exist.
- `docker compose -f docker-compose.dev.yml up` runs the production image locally.
- `README.md`: prerequisites, run, test, release steps. `DOCS.md` in the add-on folder holds the user-facing docs shown in HA.

---

## 10. Definition of Done (per feature / PR)

- [ ] Behaviour matches the spec. Any spec change is made in the same PR.
- [ ] Unit tests for domain/application logic. Integration tests for new endpoints/adapters.
- [ ] Acceptance test added or updated if the change affects an acceptance criterion.
- [ ] CI green: format, analyzers (no warnings), tests, coverage gate, Trivy, add-on lint.
- [ ] No PII or secrets in logs.
- [ ] OpenAPI updated if the contract changed (and the HA integration ticket raised if needed).
- [ ] ADR written if the change makes a significant decision.
- [ ] Conventional commit message. `CHANGELOG.md` updated for user-visible changes.

---

## 11. Confirmed

- Acceptance criteria are defined in spec §12 (AC01–AC98). §4.4 maps tests to them.
- Validation: hand-written validators.
- Coverage gates: ≥ 80% overall, ≥ 90% Domain + Application.
