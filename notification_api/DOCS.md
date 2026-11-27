# Notification API

Sends notifications from Home Assistant to people and groups over **Telegram** and **SMS** (SMS.to).
Automations name a recipient (`contact:alice`, `group:family`) and a priority. The add-on decides
which channels to use and holds every phone number, chat ID and provider key, so Home Assistant never sees them.

## Installation

1. **Settings → Add-ons → Add-on Store → ⋮ → Repositories**, add
   `https://github.com/SpoilHeap/notification-api`.
2. Install **Notification API** and start it.
3. On the add-on's **Info** tab, switch on **Show in sidebar**, then open **Notifications** from the
   sidebar. It's visible to administrators only. You can also use **Open Web UI** on the same tab.

## Configuration

Everything is configured in the add-on's web UI: contacts, groups, channel credentials and retention.
The add-on has no options in this tab.

## Home Assistant integration

The companion integration (`notification-api-ha`, installed through HACS) is discovered automatically
once the add-on is running. It provides `notify.*` entities and the `notification_api.send` action.

If Home Assistant doesn't offer the integration under **Settings → Devices & services → Discovered**,
open the add-on's **Integration** page. It shows whether discovery worked and, for manual setup, the
host, port and API token (click **Reveal API token**). Treat the token like a password.

When the integration is set up, it registers a webhook so the add-on can send it delivery and
recipient events. The **Integration** page shows the registered webhook, and **Send test event**
sends a signed `webhook.test` event to it and shows Home Assistant's answer (the HTTP status, or the
error). Events are best-effort: if Home Assistant can't be reached, the event is logged and dropped.

Events need Home Assistant to serve **plain HTTP** on its own port. The webhook is called at
`http://homeassistant:<port>` on the internal Supervisor network. If Home Assistant itself has
`ssl_certificate` set, the certificate won't match that hostname and events fail. That setup isn't
supported yet. Nabu Casa or a reverse proxy in front of Home Assistant is fine.

## Data and backups

Contacts, settings and encrypted credentials are stored in the add-on's `/data`, which is included
in Home Assistant backups. Use encrypted backups.
