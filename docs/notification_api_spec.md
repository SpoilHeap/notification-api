# Notification API: Spec (v1.0)

Status: **final for v1** (2026-11-23). Spikes passed on HA OS (2026-11-25), see §13. Source brief: `brief.md`. Build standards: `engineering_standards.md`.

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
  "notification_id": "ntf_01H...",
  "deliveries": [
    { "delivery_id": "dlv_1", "contact": "alice", "via": "group:family", "channel": "telegram", "status": "queued" },
    { "delivery_id": "dlv_2", "contact": "alice", "via": "contact:alice", "channel": "telegram", "status": "queued" }
  ]
}
```
Errors: `400` unknown handle or invalid payload, `401` bad token, `422` zero deliveries resolved.

### 4.2 Lookups
`GET /recipients`, `GET /notifications/{id}`, `GET /channels`, `GET /health`

### 4.3 Events (API → HA)
The add-on POSTs to the HA webhook that the integration registered (`http://homeassistant:8123/api/webhook/<id>`).
v1: `delivery.sent`, `delivery.failed`, `recipients.changed`.
Reserved for v2: `reply.received`, `action.pressed`.

## 5. Delivery algorithm (v1)

1. Expand the `to` handles depth-first into a list of contacts, keeping duplicates.
2. For each contact, pick channels: (`channels` override, if given, or `routing[priority]`) ∩ verified endpoints.
3. Create one Delivery per (contact, path, channel).
4. Send. On failure, retry 3 times at 30 s intervals (configurable).
5. Final state `sent` or `failed`, with one event to HA.

Formatting: Telegram gets HTML `<b>title</b>\nmessage`, SMS gets plain `title: message`.

## 6. Channels (v1)

**Telegram:** Bot API, with updates by long polling. Contacts link through a deep link / QR code `t.me/<bot>?start=<one-time-token>`.
**SMS.to:** REST API key and sender ID, E.164 numbers, "sent" is final.

## 7. Data model (sketch)

- **Contact**, **Endpoint**, **Group**, **GroupMember**: as before
- **ApiClient**: hashed token, webhook URL + secret, last_used (one row in v1: HA)
- **Notification**, **Delivery**: as before
- **Settings**: retention_days (30), retry count/interval, provider credentials (encrypted)
- **AuditLog**: ha_user_id + display name (from the Ingress headers), action, entity, timestamp

## 8. Authentication and authorisation

### 8.1 Web UI: delegated to HA Ingress
- HA authenticates the user. Ingress proxies the request to the add-on and adds `X-Remote-User-Id`, `X-Remote-User-Name` and `X-Remote-User-Display-Name`.
- The add-on **only accepts UI requests from the Ingress proxy** (`172.30.32.2`) and rejects everything else. That's what makes trusting the headers safe.
- No passwords, no account management and no second login screen.

### 8.2 Authorisation
- **No roles.** `panel_admin: true` means only HA administrators see the panel, and everyone who can reach it has full access.
- The Ingress identity headers are used only to record **who** made each change in the AuditLog.

### 8.3 API (`/api/v1`)
- Still token-protected: it's cheap, and it protects against other add-ons on the internal network.
- The add-on generates the token on first start and publishes it through **Supervisor discovery**, so the user never sees it.
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
4. The user clicks **Submit**. The integration stores the config, registers its webhook with the API, and creates the entities.

⚠️ Discovery for a **custom** (HACS) integration should work through `async_step_hassio`, but verify this in an early spike. The fallback is a manual config flow, with the token shown in the add-on UI.

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

- `config_flow.py`: `async_step_hassio` (discovery), plus a manual fallback.
- Entities: one `notify.<handle>` per recipient. Action: `notification_api.send`.
- Webhook receiver → HA events `notification_api_delivery_sent` / `_failed`. Entities refresh on `recipients.changed`.

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

### 12.4 Lookups

| ID | Given / When / Then | Test |
|---|---|---|
| AC40 | `GET /recipients` returns every contact and group as `{handle, name, type}` and **no phone numbers, chat IDs or routing**. | A |
| AC41 | `GET /notifications/{id}` returns the current status of every delivery. An unknown id returns 404. | A |
| AC42 | `GET /channels` reports each channel as `configured` / `not_configured`. | A |

### 12.5 Contacts and groups (web UI)

| ID | Given / When / Then | Test |
|---|---|---|
| AC50 | An admin can create, edit and delete a contact with a display name and a unique **handle** (`[a-z0-9_]+`). | E |
| AC51 | A contact's **handle can't be changed** after creation, because HA automations depend on it. The display name can be changed. | A + E |
| AC52 | SMS numbers are validated and stored in E.164. Invalid numbers are rejected with a field error. | A |
| AC53 | Per-priority routing can be set for each contact, from the channels that contact has endpoints for. | E |
| AC54 | An admin can create a group and add contacts **and other groups** as members. | E |
| AC55 | Adding a member that would create a cycle (self, direct or indirect) is rejected with a clear message. | A |
| AC56 | Deleting a contact or group that is a member of other groups asks for confirmation (listing those groups) and then removes the memberships. | E |
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
