# 0001. Record architecture decisions

- Status: Accepted
- Date: 2026-11-25

## Context

The spec ([notification_api_spec.md](../notification_api_spec.md)) and the standards ([engineering_standards.md](../engineering_standards.md)) state *what* we decided. The reasons are spread across tables, notes and spike results. Without a record, later changes will re-open settled questions or undo them by accident.

## Decision

Record significant decisions as ADRs in `docs/adr/NNNN-title.md`, in [MADR](https://adr.github.io/madr/) format, using [0000-template.md](0000-template.md).

- Keep each ADR short: Context, Decision, Alternatives (where relevant), Consequences.
- ADRs are immutable once accepted. To change a decision, write a new ADR and mark the old one *Superseded*.
- The first set (0002 onwards) records the decisions already in the spec (engineering_standards.md §7).
- A PR that makes a significant decision includes its ADR (Definition of Done, engineering_standards.md §10).

## Consequences

- The reasoning lives next to the code and is reviewed in PRs.
- Small overhead per decision. Trivial choices don't need an ADR.
