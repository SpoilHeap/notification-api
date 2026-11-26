# 0007. Telegram and SMS (via SMS.to) as the v1 channels

- Status: Accepted
- Date: 2026-11-23

## Context

The brief ([brief.md](../brief.md)) asks for SMS, WhatsApp, Telegram and others. v1 keeps scope small so acceptance testing stays easy (spec §1). We need one rich, free messaging channel and one channel that reaches any phone.

## Decision

v1 ships two channels, both behind `INotificationChannel`:

- **Telegram** (chosen over Signal). Bot API, with updates by long polling. Contacts link through a deep link / QR code `t.me/<bot>?start=<one-time-token>`. Messages are HTML: `<b>title</b>` + newline + message.
- **SMS via SMS.to.** REST API with an API key and sender ID, E.164 numbers. "Sent" is final: there are no delivery receipts in v1. Messages are plain `title: message`.

Two-way messaging is not in v1, but the design leaves room for it (`actions` reserved, `reply.received` / `action.pressed` events reserved).

## Alternatives considered

- **Signal.** Has no official bot API. Using it would mean running an unofficial client, such as signal-cli, with a registered phone number.
- **WhatsApp.** Deferred. Not in v1.

## Consequences

- Long polling needs no inbound port, which suits an add-on with no LAN port.
- The bot replies to `/start` to confirm linking. Link tokens are single-use and expire after 24 h (AC62).
- Provider credentials are entered in the UI and stored encrypted in `/data`.
- More channels can be added later as new `INotificationChannel` implementations in Infrastructure.
