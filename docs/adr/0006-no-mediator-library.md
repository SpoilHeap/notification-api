# 0006. No mediator library

- Status: Accepted
- Date: 2026-11-23

## Context

The Application layer has one class per use case (`SendNotification`, `CreateContact`, `ProcessDueDeliveries`, …), each with a single `HandleAsync`. A mediator library (e.g. MediatR) is a common way to dispatch to them. MediatR is now commercially licensed, and the indirection adds little in an app this size.

## Decision

**No mediator library.** Use cases are registered in DI and injected directly into endpoints, pages and hosted services (engineering_standards.md §2.3).

## Alternatives considered

- **MediatR.** Commercial licence, and request/handler indirection that hides call paths.
- **A hand-rolled mediator.** Same indirection, plus code to maintain.

## Consequences

- Call sites name the use case they use, so "go to definition" works.
- Cross-cutting concerns (logging, validation) are done explicitly or with DI decorators, not pipeline behaviours.
