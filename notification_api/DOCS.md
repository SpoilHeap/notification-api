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

## Data and backups

Contacts, settings and encrypted credentials are stored in the add-on's `/data`, which is included
in Home Assistant backups. Use encrypted backups.
