# 0002. Deploy as a Home Assistant OS add-on

- Status: Accepted
- Date: 2026-11-23

## Context

The service is called by Home Assistant (HA) on a single premises. It must run on a ThinkCentre (`amd64`) now, and also on a NUC (`amd64`) and an HA Green (`aarch64`, 4 GB RAM). The HA host is already the always-on box. The UI needs authenticated access without building a login system (see [0003](0003-ingress-authentication-no-roles.md)), and the HA integration needs a way to find the service and get a token without manual setup.

## Decision

Ship the service as an **HA OS add-on** (spec §9).

- **One GitHub repo** (`notification-api`) is both the source repo and the add-on repository the user adds in HA. The add-on lives in `notification_api/` (folder name = slug), with `repository.yaml` at the root.
- CI builds **multi-arch images** (`amd64`, `aarch64`) with `docker buildx` and pushes them to **GHCR**. `config.yaml` references them through `image:`. The SDK stage runs on the build platform and cross-compiles, so no QEMU.
- The UI is reached only through **Ingress**. No port is mapped to the LAN.
- Setup uses **Supervisor discovery**: the add-on publishes host, port and API token, and the integration picks them up in `async_step_hassio`.
- `/data` holds SQLite, Data Protection keys and the API token, and is included in HA backups.
- A release bumps `version` in `config.yaml`, and the image tag matches, so HA offers the update.

## Alternatives considered

- **Standalone container or VM.** Needs its own auth, reverse proxy, backup and update story. Ingress and discovery wouldn't be available.
- **Supervisor builds the Dockerfile on the device** (no `image:`). Used in the spike. Install took 5–10 minutes and pulled the ~900 MB SDK image, too slow and too large for HA Green.

## Consequences

- Shared fate with the HA host is accepted: if HA is down, the notifier is down (spec §9.3).
- Lifecycle, backups and updates come from the Supervisor for free.
- Backups contain provider credentials (encrypted with keys also in `/data`), so users should encrypt their backups.
- The Supervisor scans the repo for add-on `config.yaml` files, so nothing else in the repo may look like an add-on (this is why `spikes/` is gitignored).
- Spike 1 (2026-11-25) confirmed discovery works with a custom (HACS) integration.
