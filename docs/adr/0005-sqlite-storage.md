# 0005. SQLite with EF Core for storage

- Status: Accepted
- Date: 2026-11-23

## Context

The add-on is a single instance on a single premises. Data volumes are small: contacts, groups, settings, and notification logs kept for 30 days by default. Everything persistent must live in `/data` so the Supervisor backs it up and restores it (spec §9.3). HA Green has 4 GB of RAM, and the target is about 100–150 MB for the add-on.

## Decision

Use **EF Core + SQLite**, with the database file in `/data`.

- Entity configuration through `IEntityTypeConfiguration<T>`. No data annotations in Domain.
- Migrations are checked in and applied on startup (safe for a single instance). A test checks there is no pending model change.
- Integration tests use real SQLite in memory, with a shared connection per test.

## Alternatives considered

- **A separate database server or add-on (PostgreSQL, MariaDB).** Another moving part to install, back up and keep in sync, for no benefit at this scale.

## Consequences

- One file in `/data`, included in HA backups. Restore works with no reconfiguration (AC96).
- No concurrent writers from multiple instances, which we don't have.
- SQLite's limited `ALTER TABLE` support can make some migrations rebuild tables. Acceptable at this size.
