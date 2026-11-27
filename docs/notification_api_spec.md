# Notification API: Spec (v1.4)

Status: **final for v1** (2026-11-23). Spikes passed on HA OS (2026-11-25), see §13. v1.1 (2026-11-27) adds the integration contract found while building `notification-api-ha`: §4.2–§4.4, AC35–AC37 and AC43–AC45. v1.2 (2026-11-27) pins the exact wire details of §4.2–§4.4 (secret encoding, signature bytes, envelope format, ETag matching, the `webhook.test` event and a signature test vector), so both repos implement the same bytes. v1.3 (2026-11-27) fixes the contacts and groups decisions found while building them: what changes the recipients revision (§4.2), how `recipients.changed` is sent (§4.4), endpoint verification (§6), the data model and audit log (§7), and deletion and routing rules (§12.5). v1.4 (2026-11-27) settles the `/notify` request rules (§4.1) and the retry and SMS.to settings for delivery (§5, §6), and, while building delivery, pins the id formats and response fields (§4.1), the `delivery.*` event data (§4.4), the delivery worker and restart rule (§5, [ADR 0014](adr/0014-persisted-delivery-queue.md)), the SMS.to wire format (§6) and the Notification, Delivery and Settings data (§7). Source brief: `brief.md`. Build standards: `engineering_standards.md`.

## 1. Guiding principle

**Home Assistant (HA) decides *who* to notify, *what* to say and *how urgent* it is. The Notification API decides *how* to deliver it.**

HA never sees phone numbers, chat IDs or provider credentials. It only uses stable recipient handles (`contact:alice`, `group:family`).

**v1 keeps every algorithm deliberately simple**, to make acceptance testing easy.

## 2. Decisions so far

| Topic | Decision |
|---|---|
| Channel routing | Owned by the API, using per-contact preferences for each priority. HA may override (rare). |
| HA integration | Custom HA integration (Python), in its own GitHub repo (`notification-api-ha`), installed through HACS |
| Two-way messaging | Not in v1. Designed for it. |
| Message content | Free text from HA |
| v1 channels | Telegram + SMS via SMS.to |
| Hosting | **HA OS add-on**, images on **GHCR**. **One GitHub repo for the app + add-on** (see §9.1). |
| Target hardware | ThinkCentre now (`amd64`). Must also run on a NUC (`amd64`) and HA Green (`aarch64`, 4 GB RAM). |
| Tenancy | One premises |
| **Web UI auth** | **No own login. HA Ingress authenticates the user; the add-on trusts the Ingress identity headers.** |
| **API auth** | **A token generated automatically and handed to the integration through Supervisor discovery** |
| Web UI access | **Ingress only** (HA sidebar), with **`panel_admin: true`**: only HA admins can see it. No port exposed to the LAN. |
| **Roles** | **None.** Every user who can open the panel has full access. |
| Setup | **Supervisor discovery** (the HA standard for add-on + integration pairs) |
| Events → HA | Webhook registered by the integration |
| Transport | Plain HTTP on the internal Supervisor network |
| Groups | Nested allowed, no deduplication |
| Quiet hours / rate limits / fallback / tag logic | None in v1 |
| Attachments | Not in v1 |
| SMS delivery status | "Sent" is final |
| Log retention | Configurable in the UI, default 30 days |
| Shared fate with the HA host | Accepted |
| Stack | C# / **.NET 10 (LTS)**, ASP.NET Core, **Blazor Static SSR** web UI, Clean Architecture (see `engineering_standards.md`) |

## 3. Responsibilities

| Concern | HA | Notification API |
|---|---|---|
| Trigger / when to notify | ✅ | |
| Message title and body | ✅ | Formats per channel |
| Recipients | Names the handles | Resolves handles, expanding nested groups |
| Urgency | Sends `priority` | Maps priority → channels for each contact |
| Channel choice | Optional override only | ✅ default owner |
| Phone numbers, chat IDs, provider credentials | ❌ never | ✅ |
| Retries | ❌ | ✅ |
| Reacting to failures | ✅ automations on events | ✅ sends events |
| **User authentication** | ✅ HA login + Ingress | Trusts the Ingress headers |
| **Authorisation** | ✅ Only HA admins see the panel (`panel_admin: true`) | None: everyone who gets in has full access |
| API token | Integration stores it (received through discovery) | Generates and rotates it |
| Lifecycle, backups, updates | ✅ Supervisor | |

## 4. Public API (used by the HA integration)

Base: `http://<addon-hostname>:8080/api/v1`, authenticated with `Authorization: Bearer <api-token>`.
JSON field names are snake_case throughout (requests, responses and webhook events). Errors are RFC 9457 `ProblemDetails` (`application/problem+json`). Validation errors (400) add an `errors` object keyed by the request's JSON field name, e.g. `{"errors": {"secret": ["…"]}}`.

### 4.1 Send
```http
POST /notify
{
  "to": ["group:family", "contact:alice"],
  "title": "Front door",
  "message": "Door opened while alarm armed",
  "priority": "high",            // low | normal | high | critical
  "channels": ["telegram"],      // optional override
  "tag": "front_door_alarm",     // optional, logged only
  "actions": [],                 // reserved for v2; rejected if non-empty
  "source": "automation.front_door",
  "idempotency_key": "6f1c..."   // optional
}
```
```http
202 Accepted
{
  "notification_id": "ntf_019ac63f8e407c3a9d1b2e4f5a6b7c8d",
  "deliveries": [
    { "delivery_id": "dlv_019ac63f8e417d0c8a2b3c4d5e6f7a8b", "contact": "alice", "via": "group:family", "channel": "telegram", "status": "queued" },
    { "delivery_id": "dlv_019ac63f8e417d0c8a2b3c4d5e6f7a8c", "contact": "alice", "via": "contact:alice", "channel": "telegram", "status": "queued" }
  ]
}
```
Errors: `400` unknown handle or invalid payload, `401` bad token, `422` zero deliveries resolved.

**Request rules** (v1.4, settled before building `/notify`):
- **Required:** `to` (non-empty array of handles), `message` (1–4000 characters), `priority`. Everything else is optional.
- **Optional fields:** `title` (≤ 200 characters), `channels`, `tag`, `source`, `idempotency_key` (each of the last three ≤ 200 characters). `null` means the same as omitted. `channels: []` means no override (the same as omitted). `source` is optional.
- **Case:** `priority` (`low | normal | high | critical`) and channel names (`telegram | sms`) are exact lower case; `High` is a 400.
- **`actions`:** reserved for v2. Omitted, `null` or `[]` is accepted; anything else is a 400 under `errors.actions`.
- **Unknown handles (AC09):** 400 `ProblemDetails` with one message per unknown handle under `errors.to`, e.g. `{"errors": {"to": ["Unknown handle: contact:bob"]}}`. A malformed handle gets a message there too. Nothing is queued.
- **Zero deliveries (AC08):** 422 `ProblemDetails` with `title: "No deliveries"` and a `detail` saying why (e.g. "None of the 1 resolved contact(s) has a verified endpoint for the selected channels."), with no `errors` object. Nothing is queued.
- **202 body:** every delivery's `status` is `queued`. The later lifecycle (`queued → sending → sent`, or `→ retrying → … → failed`) is visible through events (§4.4) and `GET /notifications/{id}`. `deliveries` follows the order of §5 (handles in `to` order, depth-first, then the contact's channel order).
- **Ids:** `notification_id` is `ntf_` and `delivery_id` is `dlv_`, each followed by 32 lower-case hex digits (a time-ordered UUID v7 without hyphens, like event ids in §4.4), e.g. `ntf_019ac63f8e407c3a9d1b2e4f5a6b7c8d`. Treat them as opaque strings.
- **Delivery fields:** `contact` is the contact's handle **name** without the `contact:` prefix (`alice`); `via` is the full handle from `to` that reached it (`group:family`, or `contact:alice` when the contact was named directly); `channel` is `telegram` or `sms`. The 202 has no `Location` header.
- **Malformed JSON** (not JSON, or a field of the wrong JSON type such as `"to": "contact:alice"`) is a 400 `ProblemDetails` without field errors.
- **Idempotency (AC12):** the key alone decides. Within 24 h of the accepted request, the same key returns the **original 202 response** (same status and body, statuses still `queued`) whatever the new payload is, and nothing new is sent. Requests that are rejected (400, 422) never record a key. After 24 h the key counts as new.

### 4.2 Lookups
- `GET /ping`: token check for the integration's setup flow and connectivity polling.
  ```json
  { "ok": true, "version": "0.2.0", "instance_id": "8d3f0c3e-…" }
  ```
  `instance_id` is a random (version 4) UUID in lower-case hyphenated form, generated once, when the database is created, and never changed. The integration uses it to tell "same add-on, new token" (keep entities) from "add-on reinstalled with a fresh database" (recipients may be different).
- `GET /recipients`: every contact and group, with **no PII**.
  ```json
  { "revision": "17", "recipients": [ { "handle": "group:family", "name": "Family", "type": "group" } ] }
  ```
  - `recipients` is sorted by type (contacts first), then by handle. Each entry has exactly `handle`, `name` (the display name) and `type` (`contact` or `group`).
  - `revision` is an opaque string that changes whenever any contact or group is **created, renamed (display name changed) or deleted**. Nothing else changes it: endpoints, routing and group membership aren't part of this response, so changing them leaves the revision (and the ETag) as it was. A new database starts at `"1"`. Treat it as opaque: compare for equality only.
  - Every 200 and 304 response carries it as a strong `ETag`: the revision in double quotes, e.g. `ETag: "17"`.
  - With `If-None-Match` set to the current ETag (`"17"`), the add-on answers **304 Not Modified** with no body. Any other value, or no header, gets 200. Matching follows RFC 9110 (a list of tags, `*`, and `W/"17"` all match). Polling is then cheap.
  - A new database has no contacts or groups, so `recipients` is `[]`.
- `GET /notifications/{id}`, `GET /channels`

`GET /health` is at the root (`http://<addon-hostname>:8080/health`), not under `/api/v1`. It's anonymous, for the Docker HEALTHCHECK.

### 4.3 Webhook registration (integration → API)
The integration builds its own webhook URL (it knows HA's port and SSL setup), generates the shared secret, and registers both:
```http
PUT /webhook
{ "url": "http://homeassistant:8123/api/webhook/<webhook_id>", "secret": "<base64, ≥ 32 random bytes>" }
→ 204 No Content
```
- One registration per API client: a new `PUT` replaces the old one and sets `registered_at`. `DELETE /webhook` removes it and answers 204 even when nothing was registered (idempotent). The integration calls it when its config entry is removed.
- `secret` is **standard base64** (RFC 4648 §4, with `=` padding, no whitespace) of 32 to 512 random bytes. The integration should generate 32 bytes, e.g. `base64.b64encode(secrets.token_bytes(32))`. The **HMAC key is the decoded bytes**, not the base64 text.
- Validation: `url` must be an absolute `http`/`https` URL of at most 2048 characters, and `secret` must be valid standard base64 decoding to 32–512 bytes. Otherwise **400** with field errors under `errors.url` / `errors.secret`, and the previous registration is kept unchanged (AC36).
- The secret is stored encrypted (like the API token, ADR 0012) and is never returned by any endpoint or logged. The add-on logs only the URL's origin (scheme, host, port), because the path holds HA's webhook id.
- `GET /webhook` returns `{ "url": "…", "registered_at": "2026-11-27T16:15:20.1234567Z" }` (ISO 8601, UTC; no secret), or 404 `ProblemDetails` when nothing is registered.

### 4.4 Events (API → HA)
The add-on POSTs events to the registered webhook URL. With no registration, events are dropped (logged at Debug).

**Envelope:** a `POST` with `Content-Type: application/json` (UTF-8, no BOM) and this body:
```json
{"id":"evt_019ac63f8e407c3a9d1b2e4f5a6b7c8d","type":"delivery.sent","occurred_at":"2026-11-27T16:15:20Z","data":{…}}
```
- `id`: `evt_` followed by 32 lower-case hex digits (a time-ordered UUID v7 without hyphens). Unique per event. Receivers must accept any `evt_`-prefixed string (a 26-character ULID is also allowed) and treat it as opaque.
- `type`: one of the types below.
- `occurred_at`: ISO 8601 in UTC with a `Z` suffix, to the second (`2026-11-27T16:15:20Z`).
- `data`: an object, specific to the type, with snake_case field names.

Types:

| Type | `data` | Producer |
|---|---|---|
| `webhook.test` | `{}` | **Send test event** on the add-on's Integration page. Lets an admin check the webhook before contacts exist. The integration verifies it like any event, and may log or ignore it. |
| `recipients.changed` | `{"revision": "18"}`: the revision the next `GET /recipients` returns (AC45) | Contact or group created, renamed or deleted (one event per change, sent after it's committed; see below) |
| `delivery.sent` | AC30 fields, see below | Delivery reached `sent` |
| `delivery.failed` | AC31 fields, plus `error`, see below | Delivery reached `failed` |

Reserved for v2: `reply.received`, `action.pressed`. Receivers should ignore unknown types.

**Delivery event data** (v1.4). Exactly these fields, in this order, sent once when a delivery reaches its final state (never for a retry):
```json
{"notification_id":"ntf_019ac63f8e407c3a9d1b2e4f5a6b7c8d","delivery_id":"dlv_019ac63f8e417d0c8a2b3c4d5e6f7a8b","contact":"alice","via":"group:family","channel":"telegram","tag":"front_door_alarm","source":"automation.front_door"}
```
- `notification_id`, `delivery_id`, `contact`, `via` and `channel` are the values of the 202 response (§4.1).
- `tag` and `source` are the request's values, or JSON `null` when the request had none (both keys are always present).
- `delivery.failed` adds `"error"` last: a human-readable string, the last attempt's error (at most 500 characters, phone numbers masked), e.g. `"SMS.to answered HTTP 401: Invalid API Key or Token"` or `"SMS.to is not configured"`.

**Signature (AC33, AC37):** every call carries
- `X-Notification-Api-Timestamp: <unix seconds>`: integer seconds since the Unix epoch, as a plain decimal string (`1795796120`).
- `X-Notification-Api-Signature: sha256=<hex>`: the **lower-case hex** HMAC-SHA256 where
  - the key is the **decoded bytes** of the registered base64 `secret`, and
  - the message is the ASCII bytes of the timestamp header value followed by `.`, then the **exact raw body bytes** of the request.

The add-on serialises the body once and sends exactly the bytes it signed. The receiver recomputes the HMAC over the raw body bytes (before any JSON parsing) and compares in constant time. It rejects the call if the signature doesn't match, or if the timestamp is more than **300 s** from its clock (replay protection). It deduplicates on `id`.

**Test vector.** Both repos test against this:

| Input | Value |
|---|---|
| `secret` (base64) | `AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=` (the 32 bytes `0x00, 0x01, …, 0x1f`) |
| Timestamp | `1795796120` (2026-11-27T16:15:20Z) |
| Body (exact bytes: 114 bytes of UTF-8, no trailing newline) | `{"id":"evt_019ac63f8e407c3a9d1b2e4f5a6b7c8d","type":"webhook.test","occurred_at":"2026-11-27T16:15:20Z","data":{}}` |
| Signed message | `1795796120.` followed by the body |
| `X-Notification-Api-Signature` | `sha256=c2fcb3fbbe01d66932b4d9a193e368ef70bf42739a894e6dd48b98d5d50911ff` |

In Python: `hmac.new(base64.b64decode(secret), b"1795796120." + body, hashlib.sha256).hexdigest()`.

**Sending:** events are queued in memory once the change that caused them is committed, and a background sender POSTs them one at a time, in the order they were queued ([ADR 0013](adr/0013-ha-events-sent-from-an-in-process-queue.md)). The request that made the change never waits for HA. The queue holds at most 256 events: when it's full the oldest waiting event is dropped and logged at Warning, and waiting events are lost when the add-on stops. **Send test event** (`webhook.test`) is the exception: it's sent inline, because the Integration page shows HA's answer.

**Delivery:** best-effort, one attempt with a 5 s timeout, no retry (AC34). A 2xx answer counts as delivered. Any other status (redirects aren't followed), a timeout or a connection error is a failure, logged at Warning with the event type, id and HTTP status, never the secret or the body. With no registration the event is dropped and logged at Debug. The integration therefore doesn't rely on `recipients.changed` alone: it also polls `GET /recipients` with `If-None-Match` (§11).

## 5. Delivery algorithm (v1)

1. Expand the `to` handles depth-first into a list of contacts, keeping duplicates.
2. For each contact, pick channels: (`channels` override, if given, or `routing[priority]`) ∩ verified endpoints.
3. Create one Delivery per (contact, path, channel).
4. Send. On failure, retry 3 times at 30 s intervals (configurable as app options `Delivery:MaxRetries` and `Delivery:RetryInterval`; no UI in v1).
5. Final state `sent` or `failed`, with one event to HA.

Formatting: Telegram gets HTML `<b>title</b>\nmessage`, SMS gets plain `title: message`.

Details (v1.4, [ADR 0014](adr/0014-persisted-delivery-queue.md)):
- **Formatting:** for Telegram, the title and message are HTML-escaped (`<`, `>`, `&`, quotes), so `<script>` arrives as text (AC24). With no title, Telegram gets just the escaped message and SMS just the message (AC25).
- **Queue:** steps 1–3 run in the `/notify` request and save the notification and all its deliveries in one commit (status `queued`); step 4 runs in a background worker. The 202 never waits for a provider (AC13).
- **Attempts:** the worker claims due deliveries (`queued`, or `retrying` whose next attempt is due), marks them `sending` and commits before sending. Each delivery is attempted in its own task, so a slow or failing delivery doesn't hold up any other (AC26). `attempts` counts started attempts: an always-failing channel gives `attempts = 4` (AC20). `Delivery:MaxRetries` is 0–20 and `Delivery:RetryInterval` must be positive.
- **No channel:** if the add-on has no implementation for a delivery's channel (Telegram, until linking ships), the delivery fails at once (`attempts = 1`, no retry) with "The Telegram channel isn't available in this version of the add-on."
- **Restart (AC23):** `queued` and `retrying` deliveries are kept in the database and processed after startup; a retry whose time passed while the add-on was down is due at once. A delivery still `sending` at startup was cut off mid-send, so it may or may not have arrived: it is marked `failed` (with a `delivery.failed` event) and **never sent again**. A graceful stop waits for sends in flight, so only a crash or a forced stop leaves one.

## 6. Channels (v1)

**Telegram:** Bot API, with updates by long polling. Contacts link through a deep link / QR code `t.me/<bot>?start=<one-time-token>`. A Telegram endpoint can **only** be created by that linking flow (AC60–AC63): an admin can't type a chat id. Until a contact is linked, the UI shows Telegram as *Not linked — coming soon* (until the linking feature ships) and routing can't select it.
**SMS.to:** REST API key and sender ID, set on the web UI's Settings page (the key is stored encrypted and shown masked, AC70), E.164 numbers, "sent" is final. Wire format (v1.4, from the official API reference at [developers.sms.to](https://developers.sms.to/), "Send single message"): `POST https://api.sms.to/sms/send` with `Authorization: Bearer <api key>` and JSON `{"to": "+353…", "message": "…", "sender_id": "…"}` (`sender_id` left out when none is set, so the account's default applies). Success is a 2xx with `{"success": true, "message_id": "…", "message": "Message is queued for sending! …"}`: the delivery is `sent` and `message_id` is stored (AC22). Errors are `{"success": false, "message": "…"}` with a 4xx status; the attempt fails with `SMS.to answered HTTP <status>: <message>`. A timeout (10 s, `SmsTo:RequestTimeout`) or connection error is a failed attempt, retried by §5. With no API key saved the attempt fails with "SMS.to is not configured". The key and sender ID are read on every send, so a change on the Settings page applies without a restart. The sender ID is optional: up to 11 letters, digits or spaces, or a number of up to 15 digits (optionally `+`). An SMS endpoint is **verified on entry**: an admin types the number, so `verified_at` is set when it's saved. Numbers are normalised by removing spaces, dashes and parentheses, and must then be `+` followed by 8–15 digits, the first not `0` (AC52).

## 7. Data model (sketch)

- **Contact**: an immutable `handle` (`contact:<name>`, name `[a-z0-9_]{1,64}`), a display `name` (required, trimmed, one line, ≤ 100 characters), its endpoints, and `routing`: for each priority, an ordered list of distinct channels. Routing may only name channels the contact has an endpoint for; removing an endpoint also removes its channel from every priority.
- **Endpoint**: `channel`, `address` (E.164 for SMS, chat id for Telegram), `verified_at`. At most one per channel per contact. Only verified endpoints receive deliveries (§5 step 2).
- **Group**: an immutable `handle` (`group:<name>`), a display `name`, and members.
- **GroupMember**: exactly one contact or one group. A member appears at most once in the same group (a contact can still be in many groups). Adding a member that would create a cycle (the group itself, or a group that already contains it at any depth) is rejected (AC55).
- Handles are unique per type, stored as text in their own tables: `contact:x` and `group:x` may coexist (AC57). Deleting a contact or group removes its memberships in other groups (AC56); deleting a group never deletes its members.
- **ApiClient**: hashed token + encrypted token (ADR 0012), webhook URL + encrypted webhook secret + registered_at, last_used (one row in v1: HA)
- **Instance**: a single row: `instance_id` (UUID v4, created on first start with the database) and the recipients `revision` counter (starts at 1)
- **Notification** (v1.4): id (§4.1), `title`, `message`, `priority`, the `channels` override (empty when none), `tag`, `source`, `idempotency_key`, `created_at`. Immutable.
- **Delivery** (v1.4): id, its notification, `position` (its place in the 202 list), `contact` and `via` handles (as text, so a delivery survives the contact's deletion), `channel`, the endpoint `address` copied at queue time, `status` (`queued`, `sending`, `sent`, `retrying`, `failed`), `attempts`, `last_error`, `provider_message_id` (the SMS.to `message_id`), `next_attempt_at`, `created_at`, `last_attempt_at`, `completed_at`. Deleting a notification deletes its deliveries. `GET /notifications/{id}` (AC41) reads these rows.
- **Settings**: retention_days (30), provider credentials (encrypted). Retry count and interval are app options in v1 (§5), not settings. v1.4 stores one row with the SMS.to API key (Data Protection-encrypted, ADR 0012, plus its last 4 characters for the masked display when the key has at least 12) and the sender ID; saving or removing them on the Settings page is audited (entity type `Settings`, key `sms_to`), never with the key.
- **AuditLog**: one entry per create, update or delete made in the web UI (contacts, groups, endpoints, routing, membership), plus **Reveal API token** and **Send test event** (AC82). Fields: timestamp, `ha_user_id` and display name (from the Ingress headers), action (`create`, `update`, `delete`, `reveal`, `send`), entity type, entity key (the handle, or another stable key) and a short summary. Phone numbers are always masked (`+3538****567`, engineering_standards.md §5), never stored in full. The UI shows it read-only, newest first, 50 per page. The retention purge (AC73) will delete old entries.

## 8. Authentication and authorisation

### 8.1 Web UI: delegated to HA Ingress
- HA authenticates the user. Ingress proxies the request to the add-on and adds `X-Remote-User-Id`, `X-Remote-User-Name` and `X-Remote-User-Display-Name`.
- The add-on **only accepts UI requests from the Ingress proxy** (`172.30.32.2`) that carry `X-Remote-User-Id`, and rejects everything else with `403`. That's what makes trusting the headers safe.
- No passwords, no account management and no second login screen.

### 8.2 Authorisation
- **No roles.** `panel_admin: true` means only HA administrators see the panel, and everyone who can reach it has full access.
- The Ingress identity headers are used only to record **who** made each change in the AuditLog.

### 8.3 API (`/api/v1`)
- Still token-protected: it's cheap, and it protects against other add-ons on the internal network.
- The add-on generates the token on first start and publishes it through **Supervisor discovery** on every start, so the user normally never sees it.
- It's stored hashed (for verification) and encrypted with Data Protection (so every start re-publishes the same token, and the manual fallback in §9.4 can show it). See [ADR 0012](adr/0012-api-token-hashed-and-encrypted.md).
- A missing or wrong token gets `401` with `WWW-Authenticate: Bearer` and a `ProblemDetails` body.
- The API port isn't mapped to the LAN.

## 9. Deployment on HA OS

### 9.1 Packaging
- **One GitHub repo `notification-api`**, which is both the source repo and the add-on repository the user adds in HA:
  ```
  notification-api/
  ├── repository.yaml              # add-on repository metadata
  ├── notification_api/            # the add-on (folder name = slug): config.yaml, DOCS.md, CHANGELOG.md, icon.png, logo.png
  ├── src/                         # Domain / Application / Infrastructure / Web (see engineering_standards.md §2)
  ├── tests/                       # one test project per src project, plus ArchitectureTests
  ├── docs/                        # this spec, engineering_standards.md, brief.md, adr/
  ├── scripts/                     # coverage gate, image smoke test
  ├── Dockerfile
  ├── docker-compose.dev.yml       # run the production image locally
  ├── NotificationApi.slnx
  └── .github/                     # workflows (build, test, multi-arch push to GHCR, release), Dependabot
  ```
- A release bumps `version` in `config.yaml` and pushes images tagged with that version, so HA offers the update.
- CI builds **multi-arch images (`amd64`, `aarch64`)** with `docker buildx` and pushes them to **GHCR**. `config.yaml` references them through `image:`.
  - .NET cross-compiles natively (SDK stage on `--platform=$BUILDPLATFORM`, `dotnet publish -a $TARGETARCH`), so slow QEMU builds aren't needed.
- Base image: `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (minimal, no shell). It runs as root because the Supervisor mounts `/data` owned by root ([ADR 0009](adr/0009-container-runs-as-root.md)).
- Memory: about 100–150 MB. Fine on HA Green's 4 GB.

### 9.2 Key `config.yaml` settings
```yaml
name: Notification API
slug: notification_api
arch: [amd64, aarch64]
image: ghcr.io/spoilheap/notification-api-{arch}
ingress: true
ingress_port: 8080
panel_icon: mdi:message-alert
# panel_admin is left at its default (true): only HA admins see the panel.
discovery: [notification_api]
hassio_api: true           # needed to publish discovery
init: false
```
The real file is [`notification_api/config.yaml`](../notification_api/config.yaml). Health checks use the image's Docker `HEALTHCHECK` (`--healthcheck` mode), not the obsolete `watchdog` key.

### 9.3 Runtime
| Area | Behaviour |
|---|---|
| Persistence | `/data` holds SQLite, Data Protection keys and the API token |
| Backups | `/data` is included in HA backups, so encrypt your backups |
| Networking | HA Core → `http://<addon-hostname>:8080`. Add-on → `http://homeassistant:8123`. No LAN port. |
| Lifecycle | The Supervisor handles start, restart (watchdog on `/health`) and updates (version bump) |
| Shared fate | Accepted: if the HA host is down, the notifier is down |

### 9.4 Setup flow (Supervisor discovery)
1. The user adds the GitHub repo as an add-on repository and installs the add-on.
2. On first start, the add-on generates the API token and calls `POST http://supervisor/discovery` with `{service: "notification_api", config: {host, port, token}}`.
3. HA shows *"New device discovered: Notification API"*, and the integration's `async_step_hassio` takes the config.
4. The user clicks **Submit**. The integration checks the token with `GET /ping` (and stores `instance_id`), registers its webhook with `PUT /webhook` (§4.3), loads `GET /recipients`, and creates the entities.

Discovery for a **custom** (HACS) integration works through `async_step_hassio` (spike 1, §13). The fallback is a manual config flow: the add-on's **Integration** page shows the discovery status (uuid, last error, or skipped), and the host, port and API token behind a **Reveal** button.

### 9.5 Development
- Run locally with `docker compose` (or plain `dotnet run`). In Development, `Ingress:DevFakeIngress` fakes the Ingress headers.
- The app reads the data path, port and trusted proxy from config, with add-on defaults.

## 10. .NET architecture

Layering, testing and CI rules are in `engineering_standards.md`. Summary:


- **ASP.NET Core** Minimal APIs for `/api/v1`.
- **Web UI: Blazor Static SSR** (.NET 10). Components render on the server, forms post back with `[SupplyParameterFromForm]`, and *enhanced navigation/forms* give SPA-like transitions. **No interactive render modes** (no SignalR circuit, no WASM).
  - Small dynamic bits (e.g. the Telegram link QR code, or polling link status) use plain JS or htmx.
  - Ingress: `UsePathBase` from `X-Ingress-Path`, and set `<base href>` for each request.
- **EF Core + SQLite** in `/data`.
- **Auth:** two custom `AuthenticationHandler` schemes: `Ingress` (UI: source IP must be 172.30.32.2, reads the identity headers) and `ApiToken` (API). No ASP.NET Identity.
  - Antiforgery stays enabled for SSR forms (it's on by default).
- **Background services:** delivery worker, Telegram poller, retention cleanup.
- **`INotificationChannel`:** `TelegramChannel`, `SmsToChannel`.

## 11. HA integration (Python, separate GitHub repo, HACS)

- `config_flow.py`: `async_step_hassio` (discovery), plus a manual fallback and reauth. One config entry per HA install (fixed unique_id), updated in place by later discovery.
- Entities: one `notify.<handle>` per recipient, keyed by the immutable handle (AC51). Action: `notification_api.send`.
- Webhook receiver: verifies the signature and timestamp (§4.4), deduplicates on event `id`, and fires HA events `notification_api_delivery_sent` / `_failed`.
- Recipient refresh: immediately on `recipients.changed`, **and** by polling `GET /recipients` with `If-None-Match` every 30 s, because webhook events are best-effort (AC34, AC93).
- `instance_id` change (add-on reinstalled with a fresh database): reload the entry and rebuild entities from `/recipients`.
- On entry removal: `DELETE /webhook`.

## 12. Acceptance criteria (v1)

Each criterion is a single testable behaviour. The **Test** column gives the automated level that proves it (see `engineering_standards.md` §4):
**A** = automated acceptance test through the API with recording fake channels, **E** = Playwright UI end-to-end, **I** = integration test, **M** = manual check on a real HA OS install (release checklist).

Test names follow the pattern `ACnn_<criterion>`, e.g. `AC03_Contact_in_two_groups_receives_two_messages`.

Default fixtures used below: contact **Alice** (Telegram verified, SMS verified; routing `normal → [telegram]`, `high → [telegram, sms]`), contact **Bob** (Telegram verified only; routing `normal → [telegram]`, `high → [telegram]`), contact **Carol** (no verified endpoints).

### 12.1 Sending: recipient resolution and channel selection

| ID | Given / When / Then | Test |
|---|---|---|
| AC01 | **Given** Alice, **when** HA sends `to: [contact:alice]`, `priority: normal`, **then** the API returns 202 with exactly one delivery (alice, telegram) and Alice receives one Telegram message. | A |
| AC02 | **Given** Alice, **when** HA sends to `contact:alice` with `priority: high`, **then** two deliveries are created (telegram, sms) and both channels send. | A |
| AC03 | **Given** Alice is a member of `group:family` and `group:security`, **when** HA sends `to: [group:family, group:security]`, **then** Alice receives **two** messages per routed channel, with `via` = `group:family` and `group:security` respectively. | A |
| AC04 | **Given** Alice is in `group:family`, **when** HA sends `to: [group:family, contact:alice]`, **then** Alice receives two messages (no deduplication). | A |
| AC05 | **Given** `group:family` contains `group:kids` and Alice, and `group:kids` also contains Alice, **when** HA sends to `group:family`, **then** Alice receives two messages, both with `via: group:family`. | A |
| AC06 | **Given** a `channels: [sms]` override, **when** HA sends to Alice and Bob, **then** Alice gets SMS only and Bob gets nothing (no verified SMS), and the response lists only Alice's delivery. | A |
| AC07 | **When** HA sends to a contact whose routing names a channel with no **verified** endpoint, **then** that channel is skipped for that contact. | A |
| AC08 | **When** every resolved contact yields zero deliveries (e.g. only Carol, or an empty group), **then** the API returns **422** and nothing is queued. | A |
| AC09 | **When** any handle in `to` doesn't exist, **then** the API returns **400** naming the unknown handle(s) and **nothing is queued for any recipient** (the request is all-or-nothing). | A |
| AC10 | **When** the payload is invalid (missing `message`, empty `to`, unknown `priority`, unknown channel in `channels`, non-empty `actions`), **then** the API returns **400** `ProblemDetails` with field errors. | A |
| AC11 | **When** the bearer token is missing or wrong, **then** every `/api/v1` endpoint except `/health` returns **401**. | I |
| AC12 | **Given** a request with `idempotency_key: K` was accepted, **when** the same key is sent again within 24 h, **then** the API returns the original response and sends nothing new. **After** 24 h the key is treated as new. | A |
| AC13 | `POST /notify` returns 202 within **500 ms** (p95, local), whatever the provider latency (sending is asynchronous). | I |

### 12.2 Delivery and formatting

| ID | Given / When / Then | Test |
|---|---|---|
| AC20 | **Given** a channel that fails every attempt, **when** a delivery is processed, **then** it is attempted **1 + 3 retries at 30 s intervals** and ends `failed`, with `attempts = 4` and the last error stored. | A (fake time) |
| AC21 | **Given** a channel that fails once then succeeds, **then** the delivery ends `sent` with `attempts = 2`. | A |
| AC22 | **When** SMS.to accepts a message, **then** the delivery is `sent` (final) and the SMS.to message ID is stored. | I |
| AC23 | **Given** queued or retrying deliveries, **when** the add-on restarts, **then** those deliveries are processed after startup and none are lost or sent twice. | I |
| AC24 | Telegram messages render as `<b>title</b>` + newline + message. HTML special characters in title and message are escaped (`<script>` arrives as literal text). | A |
| AC25 | SMS messages render as `title: message` in plain text. With no title, just `message`. | A |
| AC26 | Failure of one delivery doesn't delay or affect other deliveries of the same notification. | A |

### 12.3 Events to HA

| ID | Given / When / Then | Test |
|---|---|---|
| AC30 | **When** a delivery reaches `sent`, **then** one `delivery.sent` event is POSTed to the registered HA webhook with `notification_id`, `delivery_id`, `contact`, `via`, `channel`, `tag`, `source`. | A |
| AC31 | **When** a delivery reaches `failed`, **then** one `delivery.failed` event is POSTed with the same fields plus `error`. | A |
| AC32 | **When** a contact or group is created, renamed or deleted, **then** a `recipients.changed` event is POSTed. | A |
| AC33 | Every webhook call carries the shared secret signature, and the HA integration rejects calls without a valid one. | I (both repos) |
| AC34 | **When** the HA webhook is unreachable, **then** delivery outcomes are unaffected and the failure is logged. (Events are best-effort, with no retry in v1.) | A |
| AC35 | **When** the integration calls `PUT /webhook` with a valid URL and secret, **then** the API returns 204, `GET /webhook` shows the URL (never the secret), and later events go to that URL. A second `PUT` replaces the first. `DELETE /webhook` stops events. | A |
| AC36 | `PUT /webhook` with a relative or non-http(s) URL, or a secret shorter than 32 bytes, returns **400** `ProblemDetails`, and the previous registration is kept. | A |
| AC37 | Every event carries `X-Notification-Api-Timestamp` and `X-Notification-Api-Signature: sha256=<HMAC-SHA256(secret, "<timestamp>.<raw body>")>`. The integration rejects a bad signature, a timestamp more than 300 s off, or a repeated event `id`. | I (both repos) |

### 12.4 Lookups

| ID | Given / When / Then | Test |
|---|---|---|
| AC40 | `GET /recipients` returns `{revision, recipients: [{handle, name, type}]}` for every contact and group, and **no phone numbers, chat IDs or routing**. | A |
| AC41 | `GET /notifications/{id}` returns the current status of every delivery. An unknown id returns 404. | A |
| AC42 | `GET /channels` reports each channel as `configured` / `not_configured`. | A |
| AC43 | `GET /recipients` returns its `revision` as a strong `ETag`. With `If-None-Match` set to the current revision it returns **304** with no body. After any contact or group is created, renamed or deleted, the revision changes and the same request returns 200. | A |
| AC44 | `GET /ping` returns `{ok, version, instance_id}`. `instance_id` stays the same across restarts and updates, and changes only when the database is recreated. | A |
| AC45 | `recipients.changed` events carry the new `revision`, matching the next `GET /recipients`. | A |

### 12.5 Contacts and groups (web UI)

| ID | Given / When / Then | Test |
|---|---|---|
| AC50 | An admin can create, edit and delete a contact with a display name and a unique **handle** (`[a-z0-9_]+`). | E |
| AC51 | A contact's **handle can't be changed** after creation, because HA automations depend on it. The display name can be changed. | A + E |
| AC52 | SMS numbers are validated and stored in E.164. Invalid numbers are rejected with a field error. | A |
| AC53 | Per-priority routing can be set for each contact, from the channels that contact has endpoints for. Removing an endpoint removes its channel from the routing. | E |
| AC54 | An admin can create a group and add contacts **and other groups** as members. | E |
| AC55 | Adding a member that would create a cycle (self, direct or indirect) is rejected with a clear message. | A |
| AC56 | Deleting a contact or group that is a member of other groups asks for confirmation (listing those groups) and then removes the memberships. Deleting a group doesn't delete its members. | E |
| AC57 | Group and contact handles share one namespace per type (`contact:x` and `group:x` may coexist, two `group:x` may not). | A |

### 12.6 Telegram linking

| ID | Given / When / Then | Test |
|---|---|---|
| AC60 | An admin can generate a Telegram link for a contact. The UI shows the `t.me/<bot>?start=<token>` link and a QR code. | E |
| AC61 | **When** the recipient opens the link and presses Start, **then** within 10 s the contact's Telegram endpoint is stored as **verified** and the bot replies with a confirmation. | I (fake Telegram) + M |
| AC62 | A link token is **single-use** and **expires after 24 h**. An expired or reused token gets a polite bot reply and changes nothing. | A |
| AC63 | Re-linking a contact replaces their previous Telegram chat ID. | A |

### 12.7 Settings, retention and secrets

| ID | Given / When / Then | Test |
|---|---|---|
| AC70 | An admin can enter the Telegram bot token and the SMS.to API key + sender ID. After saving, the UI shows them **masked** and they are stored encrypted in `/data`. | E + I |
| AC71 | The UI has a **Send test** action per channel and contact that reports success or the provider error. | E |
| AC72 | Retention defaults to **30 days** and can be changed in the UI (min 1, max 365). | E |
| AC73 | The daily purge deletes notifications, deliveries and audit entries older than the retention period and keeps newer ones. | A (fake time) |
| AC74 | Logs never contain full phone numbers, message bodies (at Information or above), tokens or API keys. | I |

### 12.8 Security and access

| ID | Given / When / Then | Test |
|---|---|---|
| AC80 | UI requests from the Ingress proxy IP with identity headers are served. Requests from any other IP are rejected **even with valid-looking headers**. | I |
| AC81 | Only HA **administrators** see the Notification API panel in the HA sidebar. | M |
| AC82 | Every create/update/delete in the UI writes an audit entry with the HA user's id and display name. | A |
| AC83 | All UI forms reject posts without a valid antiforgery token. | I |
| AC84 | The API port isn't reachable from the LAN (no port mapping by default). | M |

### 12.9 Add-on and HA integration (end-to-end on HA OS)

| ID | Given / When / Then | Test |
|---|---|---|
| AC90 | The add-on installs and starts from the GitHub add-on repository on **amd64** (ThinkCentre) and **aarch64** (HA Green). | M |
| AC91 | On first start, HA shows *"Notification API discovered"*. Confirming it sets up the integration with no manual token entry. | M |
| AC92 | After setup, HA has one `notify.<handle>` entity per contact and group, plus the `notification_api.send` action. | M + pytest |
| AC93 | Creating a group in the web UI makes its `notify.` entity appear in HA **without a restart** (within 30 s). | M + pytest |
| AC94 | An HA automation calling `notification_api.send` delivers a Telegram message end-to-end. | M |
| AC95 | A failed delivery fires the `notification_api_delivery_failed` event in HA, and an automation can trigger on it. | M + pytest |
| AC96 | Restoring an HA backup restores contacts, groups, settings and credentials, and sending works with no reconfiguration. | M |
| AC97 | Updating the add-on to a new version keeps all data. | M |
| AC98 | Idle memory use stays **< 200 MB** on HA Green. | M |

## 13. Confirmed decisions and spikes

No open design questions. These decisions were confirmed while drafting the criteria:
- AC09: an unknown handle rejects the **whole** request.
- AC51: handles can't be changed after creation.
- AC62: Telegram link tokens are single-use and expire after 24 h.
- AC34: webhook events to HA are best-effort, with no retry.
- AC20: 3 retries = 4 attempts in total.
- AC71: the "Send test" action is in scope for v1.
- §4.2, AC43 (v1.3): only creating, renaming or deleting a contact or group changes the recipients revision. Endpoint, routing and membership changes don't, because `/recipients` doesn't expose them.
- §4.4, AC32 (v1.3): `recipients.changed` is sent after the commit by a background sender, never inline in the UI request ([ADR 0013](adr/0013-ha-events-sent-from-an-in-process-queue.md)).
- §6 (v1.3): SMS endpoints are verified on entry. Telegram endpoints exist only through the linking flow.
- AC56 (v1.3): deleting asks for confirmation listing the containing groups, then removes those memberships. Deleting a group keeps its members.

Spike results (HA OS, amd64 ThinkCentre, 2026-11-25; code in `spikes/`):

| # | Risk | Result |
|---|---|---|
| 1 | Supervisor discovery with a custom (HACS) integration | ✅ **Pass.** The add-on got a discovery `uuid`, HA listed the integration under *Discovered*, and setup completed with the token from discovery. |
| 2 | Blazor Static SSR under Ingress | ✅ **Pass.** The `<base href>` from `X-Ingress-Path`, static assets, `blazor.web.js`, enhanced navigation and antiforgery form posts all work. |
| 3 | Ingress identity headers | ✅ **Pass.** The `X-Remote-User-Id/-Name/-Display-Name` headers arrive, and the remote IP is `172.30.32.2`. |

Lessons from the spikes, to apply in the real build:
- **Docker:** restore with the full source present. Restoring with only the csproj + `publish --no-restore` silently drops `_framework/blazor.web.js` (404, which Ingress surfaces as a MIME error). See `engineering_standards.md` §8.
- **Enhanced navigation** is signalled by `Accept: text/html; blazor-enhanced-nav=on`, not by a separate header.
- **Ingress + F5:** a browser refresh reloads the HA page, so the iframe goes back to the add-on root. Don't depend on deep links. Forms must use Post/Redirect/Get.
- **Kestrel dual-stack:** unwrap IPv4-mapped addresses (`::ffff:`) before comparing to `172.30.32.2`.
- **Re-triggering discovery:** restarting HA Core with the add-on running re-processes discovery. Restarting only the add-on may not, because Supervisor ignores duplicates.
- **Deploying to HA OS for development:** SSH as root to the Advanced SSH add-on (key auth, over Tailscale), using `spikes/deploy.sh`.
