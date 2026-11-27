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

## Contacts and groups

Home Assistant never sees phone numbers. Automations name a **handle** instead: `contact:alice` for one
person, `group:family` for a group. You set these up in the add-on's web UI.

### Add a contact

1. Open **Contacts**, enter a **handle** (lower-case letters, digits and `_`, e.g. `alice`) and a
   **name**, then click **Create contact**. The handle can't be changed later, because automations use it.
   A contact and a group may have the same handle (`contact:family` and `group:family`).
2. On the contact's page, enter the **SMS number** in international form: `+`, the country code and
   the number, e.g. `+353 86 123 4567` (spaces, dashes and brackets are fine). It's stored without them
   and shown masked from then on.
3. Under **Routing**, tick the channels to use for each priority (`low`, `normal`, `high`, `critical`).
   Only channels the contact has set up can be ticked, and a priority with nothing ticked sends nothing
   to that contact. Removing the SMS number also removes SMS from the routing.

Telegram shows **Not linked — coming soon**: linking a Telegram chat comes in a later version.

### Add a group

1. Open **Groups**, enter a handle (e.g. `family`) and a name, and click **Create group**.
2. On the group's page, pick contacts or other groups under **Add a member**. Groups can be nested, and
   someone who is in two of the groups receives the message twice. A group can't contain itself,
   directly or through other groups: the page says which groups would form the loop.

### Rename or delete

Changing a name keeps the handle, so automations keep working. **Delete** shows the groups the contact
or group is in before you confirm, and removes it from them. Deleting a group doesn't delete its
members. Automations that still send to a deleted handle will fail.

Creating, renaming or deleting a contact or group updates the integration's entities within about
30 seconds, without restarting Home Assistant.

### Audit log

**Audit log** lists every change made in the web UI, newest first, with the Home Assistant user who
made it. Phone numbers are masked there too.

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
