# 0008. Clean Architecture with four projects

- Status: Accepted
- Date: 2026-11-23

## Context

The domain has real rules: nested group expansion without deduplication, cycle detection, per-priority routing, a delivery state machine and a retry schedule. These need thorough unit tests (engineering_standards.md §4.2) without databases, HTTP or ASP.NET in the way. The app is still a single container on one premises, so the structure should be professional and pragmatic, without enterprise ceremony.

## Decision

Four source projects with a strict dependency rule (engineering_standards.md §2):

```
Web ──► Application ──► Domain
 │            ▲
 └──► Infrastructure ─┘
```

- **Domain** (`NotificationApi.Domain`): entities, value objects, domain services. References nothing.
- **Application** (`NotificationApi.Application`): one class per use case, ports (interfaces), DTOs. References Domain and `Microsoft.Extensions.*.Abstractions` only.
- **Infrastructure** (`NotificationApi.Infrastructure`): EF Core, SQLite, Telegram, SMS.to, Supervisor, HA webhook. Implements Application ports.
- **Web** (`NotificationApi.Web`): composition root, Minimal APIs, Blazor SSR UI, auth handlers, hosted services. No business logic.

The rule is enforced by `tests/NotificationApi.ArchitectureTests` (NetArchTest), not by convention. Each source project has a matching test project.

## Alternatives considered

- **A single project with folders.** Less ceremony, but nothing stops EF Core or `HttpClient` leaking into domain logic.

## Consequences

- Domain and Application are testable with plain unit tests and fakes. The coverage gate is higher there (≥ 90%).
- More projects and DI wiring than a single-project app.
- No mediator ([0006](0006-no-mediator-library.md)) and no validation library: use cases are injected directly and validators are hand-written.
