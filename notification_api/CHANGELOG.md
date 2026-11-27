# Changelog

All notable changes to this add-on are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [SemVer](https://semver.org/).

## [Unreleased]

### Added
- **Sending:** `POST /api/v1/notify` accepts a notification for contact and group handles and answers `202` at once with one delivery per contact, path and channel. Nested groups are expanded, duplicates are kept, and each contact's routing (or the request's `channels`) picks the channels, using only set-up endpoints. Unknown handles and invalid requests are refused with field errors (`400`), and a request that would deliver nothing gets `422`. Repeating an `idempotency_key` within 24 hours returns the first answer and sends nothing new.
- **Delivery in the background:** messages are queued in the database and sent by a background worker, each on its own. A failed send is retried 3 times, 30 seconds apart (`Delivery:MaxRetries`, `Delivery:RetryInterval`). Waiting messages survive restarts; a send cut off by a crash is reported as failed, never repeated.
- **SMS through SMS.to**, with the API key and sender ID on the new **Settings** page. The key is stored encrypted and shown masked, and changes are recorded in the audit log.
- `delivery.sent` and `delivery.failed` events to Home Assistant, with the notification and delivery ids, contact, via, channel, tag, source and (for failures) the error.

### Changed
- The database gets a new migration on start (notifications, deliveries and settings).

## [0.5.0] - 2026-11-27

### Changed
- Redesigned web UI in the style of Home Assistant's default theme: a top bar with tabs, cards, tables, pill buttons, coloured notices and status badges, and clearer validation errors. It follows the light or dark setting of the browser or phone, and works offline (no fonts or scripts are downloaded). On narrow screens, such as the Home Assistant mobile app, lists show one entry per block and nothing scrolls sideways.

## [0.4.0] - 2026-11-27

### Added
- **Contacts** page: create a contact with a permanent handle (`contact:alice`) and a name, then set its SMS number and choose which channels each priority uses. SMS numbers are checked and stored in international (E.164) form, and only ever shown masked (`+3538****567`). Telegram shows "Not linked — coming soon" until Telegram linking arrives.
- **Groups** page: create groups of contacts and other groups. Adding a group that would create a loop (a group inside itself, directly or through other groups) is refused with a message that shows the loop.
- Deleting a contact or group first shows which groups it belongs to, then removes it from them. Deleting a group never deletes its members.
- **Audit log** page: every change in the web UI, newest first, with the Home Assistant user who made it. Phone numbers are masked. Revealing the API token and sending a test event are recorded too.
- `GET /api/v1/recipients` now lists every contact and group (handle, name and type only), contacts first. Its revision and ETag change when a contact or group is created, renamed or deleted.
- A `recipients.changed` event is sent to Home Assistant after each of those changes.

### Changed
- Webhook events are now sent in the background, so saving in the web UI never waits for Home Assistant.
- The database gets a new migration on start (contacts, groups and the audit log).

## [0.3.0] - 2026-11-27

### Added
- `PUT`, `GET` and `DELETE /api/v1/webhook`: the integration registers the webhook that events go to. The shared secret is stored encrypted and is never returned or logged.
- Signed events to the webhook (`X-Notification-Api-Timestamp` and `X-Notification-Api-Signature`, HMAC-SHA256). One attempt with a 5 s timeout and no retry; failures are logged.
- **Send test event** on the **Integration** page: sends a `webhook.test` event and shows Home Assistant's answer. The page also shows the registered webhook.
- `GET /api/v1/recipients`, with a `revision` that is also sent as the `ETag`. `If-None-Match` with the current ETag answers `304 Not Modified`. The list is empty until contacts and groups exist.
- `instance_id` in `GET /api/v1/ping`: a UUID created with the database and never changed, so the integration can tell a reinstalled add-on from a new token.

### Changed
- The database gets a new migration on start (webhook registration time and the instance row).

## [0.2.0] - 2026-11-27

### Added
- Supervisor discovery: on every start the add-on announces itself to Home Assistant as `notification_api`, with its host, port and API token, retrying a few times if the Supervisor isn't reachable.
- API token for the Home Assistant integration, generated on first start. It's stored hashed for verification and encrypted for re-publishing; it never appears in the log.
- `GET /api/v1/ping`, which the integration uses to check its token.
- **Integration** page in the web UI: discovery status, plus the host, port and API token for setting up the integration by hand if discovery fails.
- SQLite database in `/data`, created and migrated automatically on start.

### Changed
- Every endpoint now needs explicit authentication. The web UI and its assets answer only through Home Assistant Ingress (403 otherwise), `/api/v1` needs the bearer token (401 otherwise), and `/health` stays open for the container health check.

## [0.1.0] - 2026-11-27

### Added
- Initial scaffold: .NET 10 web app with Ingress support and a health endpoint.
