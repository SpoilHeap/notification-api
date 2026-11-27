# 0013. Send HA events from a bounded in-process queue

- Status: Accepted
- Date: 2026-11-27

## Context

The add-on POSTs events to the HA integration's webhook (spec §4.4): one attempt, a 5 s timeout, no retry (AC34). The first producer, `recipients.changed` (AC32, AC45), fires when an admin creates, renames or deletes a contact or group in the web UI. Sending it inline made the UI request wait for HA, up to the full timeout when HA is slow or down. The event must also carry the revision that was actually committed, so it can only be sent after the database commit.

Delivery events (`delivery.sent`/`delivery.failed`) will come from the delivery worker later and have the same "don't block the caller" need.

## Decision

Use cases never send HA events themselves. After committing, they hand the event to `IPendingHaEvents` (Application port), which never blocks. Infrastructure implements it as a **bounded `System.Threading.Channels` channel** (256 events, single reader, `DropOldest` with a Warning log for every dropped event). A thin `BackgroundService` in Web (`HaEventSenderService`) drains it in order and sends each event through `PublishHaEvent`, which does the signing, the single attempt and the logging. An unexpected exception for one event is logged and the loop carries on.

The Integration page's **Send test event** stays inline, because the page shows HA's answer.

## Alternatives considered

- **Inline send after commit.** Simple, but a slow or unreachable HA holds every UI save for up to 5 s.
- **Fire-and-forget `Task.Run`.** No ordering, no bound, unobserved exceptions, and a scoped `DbContext` used after the request ends.
- **A persisted outbox table.** Survives restarts and could retry, but events are best-effort by design (AC34) and the integration also polls `GET /recipients` with `If-None-Match` every 30 s (spec §11), so a lost `recipients.changed` only delays a refresh. Not worth a table and a migration now; revisit if delivery events need stronger guarantees.

## Consequences

- UI saves return as soon as the change is committed; the event follows within milliseconds when HA is up.
- Events are sent one at a time in commit order, so `recipients.changed` revisions arrive in increasing order.
- Events waiting in memory are lost on shutdown, and the oldest are dropped if more than 256 are waiting. Both are logged or accepted: polling recovers the recipient list.
- Tests that check events wait (bounded) for the background send instead of asserting right after the request.
