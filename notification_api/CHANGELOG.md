# Changelog

All notable changes to this add-on are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [SemVer](https://semver.org/).

## [Unreleased]

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
