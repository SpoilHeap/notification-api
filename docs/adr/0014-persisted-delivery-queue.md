# 0014. A persisted delivery queue with an at-most-once restart rule

- Status: Accepted
- Date: 2026-11-27

## Context

`POST /notify` must answer 202 within 500 ms whatever the providers do (AC13), so sending happens after the request. A delivery is retried 3 times at 30 s intervals (spec §5, AC20), deliveries of one notification must not hold each other up (AC26), and after a restart "queued or retrying deliveries are processed and none are lost or sent twice" (AC23). The add-on is a single instance with SQLite in `/data` ([ADR 0005](0005-sqlite-storage.md)), so the queue can live in the same database and survive restarts and backups. HA events are already sent from an in-process queue ([ADR 0013](0013-ha-events-sent-from-an-in-process-queue.md)); deliveries need stronger guarantees than best-effort events.

## Decision

**The database is the queue.** `POST /notify` (`SendNotification`) validates, expands the handles, picks the channels and saves one `Notifications` row and all its `Deliveries` rows (status `queued`, `next_attempt_at = now`) in one commit. Then it signals the worker through `DeliveryWakeUp` and returns. Nothing is sent in the request.

**Claim, then send.** A hosted `DeliveryWorkerService` loops:

1. `ProcessDueDeliveries` loads up to 50 due deliveries (`queued`, or `retrying` with `next_attempt_at <= now`), moves each to `sending` (attempts + 1) and **commits before anything is sent**.
2. For each claimed delivery the worker starts `AttemptDelivery` in its own DI scope and task, without waiting. It sends through the matching `INotificationChannel` and records the outcome: `sent` (with the provider's message id), `retrying` (`next_attempt_at = now + Delivery:RetryInterval`) while attempts ≤ `Delivery:MaxRetries`, otherwise `failed`. A final state queues one `delivery.sent` / `delivery.failed` event after the commit.
3. It sleeps until the earliest `next_attempt_at`, or until `DeliveryWakeUp` is signalled (a notification was queued, or a retry was scheduled), with a 1-minute polling safety net. All waits use `TimeProvider`, so retries are tested with fake time.

Deliveries are therefore independent: each attempt is its own task with its own `DbContext`, and a retry is a row with a later `next_attempt_at`, not a sleeping task. A slow provider (up to its HTTP timeout) or a failing one never blocks other deliveries, other notifications or the claim loop. If a delivery's channel has no implementation (Telegram until linking ships), it fails at once with a clear error, since retrying can't help.

**Restart rule (AC23).** On start the worker runs `RecoverInterruptedDeliveries` before claiming anything:

- `queued` and `retrying` rows need nothing. They are in the database and are picked up as usual (a retry whose time passed while the add-on was down is due at once).
- A row still `sending` was cut off mid-attempt by a crash or a forced stop. Whether the provider accepted it is unknown. It is marked **`failed`** with the error "The add-on stopped while this delivery was being sent, so it may or may not have arrived. It was not sent again.", and a `delivery.failed` event is queued. It is **not** sent again.

An attempt that crashes **while the add-on keeps running** (for example, saving its outcome fails after the send) gets the same rule straight away rather than at the next start: the worker catches the exception and runs `AbandonDelivery` in a fresh scope, which marks the delivery `failed` ("An unexpected error interrupted this delivery…") with a `delivery.failed` event, and doesn't send it again. Only if that also fails (the database is unavailable) does the row stay `sending` until the next start.

Graceful shutdown keeps that case rare. The worker stops claiming, then waits for attempts in flight to finish (each is bounded by its channel's HTTP timeout). Only if the host stops waiting are they cancelled, and only those are left `sending`.

## Alternatives considered

- **Re-send `sending` rows on start (at-least-once).** Nothing would be silently lost, but a message the provider had already accepted would arrive twice, which breaks "none sent twice", costs money on SMS and repeats alarms. The failed event already tells HA, whose automation can decide to re-send.
- **Re-queue `sending` rows without counting the attempt.** The same duplicate risk as above, and a provider that crashes the process would loop forever.
- **An in-memory `Channel<T>` queue, like ADR 0013.** Fast and simple, but queued and retrying deliveries would be lost on every restart or update (AC23).
- **One sequential loop that awaits each send.** Simplest, but one slow provider delays every other delivery (AC26), and a retry that sleeps in the loop delays everything behind it.
- **A job library (Hangfire, Quartz).** More moving parts, its own tables and its own retry semantics, for one queue in a single-instance app.

## Consequences

- Queued and retrying deliveries survive restarts, updates and backup restores. Each delivery is sent at most once per attempt, and a crash mid-send costs that one delivery (reported as failed), never a duplicate.
- `Deliveries` holds the status, attempts, last error, provider message id and timestamps, so `GET /notifications/{id}` (AC41) is a read of rows that already exist. The retention purge (AC73) deletes old notifications, and their deliveries go with them (cascade).
- Times the queue filters or sorts on are stored as Unix milliseconds, because SQLite can't compare `DateTimeOffset` text.
- Several attempts may write to SQLite at once. Writes are tiny and Microsoft.Data.Sqlite retries on a busy database, which is fine at a home's volume.
- An SMS.to timeout is treated as a failed attempt and retried, so if SMS.to accepted the message but answered too late, the retry can duplicate it. A longer timeout makes this unlikely; SMS.to's delivery callbacks are not used in v1.
- Two concurrent requests with the same new `idempotency_key` could both be accepted (the key is checked, not locked). HA sends one request per action call, so this is accepted for v1.
