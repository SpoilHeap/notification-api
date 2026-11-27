# Notification API

[![CI](https://github.com/SpoilHeap/notification-api/actions/workflows/ci.yml/badge.svg)](https://github.com/SpoilHeap/notification-api/actions/workflows/ci.yml)

A notification service for Home Assistant (HA). HA says who to notify, what to say and how urgent it is. The service decides how to deliver it: Telegram, or SMS via SMS.to. It runs as an HA OS add-on, with a Blazor web UI in the HA sidebar for contacts, groups and settings.

## Repo layout

```
notification-api/
├── src/
│   ├── NotificationApi.Domain/          # entities, value objects, domain rules
│   ├── NotificationApi.Application/     # use cases and ports
│   ├── NotificationApi.Infrastructure/  # EF Core + SQLite, channel providers, Supervisor
│   └── NotificationApi.Web/             # composition root: API, Blazor SSR UI, Ingress
├── tests/
│   ├── NotificationApi.{Domain,Application,Infrastructure,Web}.Tests/
│   └── NotificationApi.ArchitectureTests/
├── docs/                                # spec, engineering standards, brief, ADRs
├── scripts/
│   ├── check-coverage.sh                # coverage gate
│   └── smoke-test-image.sh              # runs the built image and checks /health and blazor.web.js
├── notification_api/                    # the HA add-on (config.yaml, DOCS.md, CHANGELOG.md)
├── repository.yaml                      # HA add-on repository metadata
├── .github/                             # CI workflows, Dependabot
├── Dockerfile                           # multi-arch add-on image
├── docker-compose.dev.yml               # run the production image locally
├── NotificationApi.slnx
├── Directory.Build.props / Directory.Packages.props
├── dotnet-tools.json                    # pinned repo-local tools (ReportGenerator)
└── global.json
```

`spikes/` is gitignored and local-only.

## Prerequisites

- .NET SDK 10.0.4xx (pinned in `global.json`). If you install it with mise, make sure plain `dotnet` on your `PATH` resolves to that SDK: `dotnet --version`.
- Docker with buildx, for the image and `docker-compose.dev.yml`.
- Repo-local tools (ReportGenerator) are pinned in `dotnet-tools.json`. Run `dotnet tool restore` once.

## Run locally

```sh
dotnet run --project src/NotificationApi.Web
```

This uses the Development environment and listens on http://localhost:8080. `appsettings.Development.json` sets:

- `Storage:DataPath` to `../../.data`, so the SQLite database and Data Protection keys go in `.data/` at the repo root (gitignored).
- `Ingress:DevFakeIngress: true`, which injects fake HA identity headers and treats every request as coming from Ingress. Startup fails if this is enabled outside Development.

To run the production image instead:

```sh
docker compose -f docker-compose.dev.yml up
```

## Test

Tests use xUnit v3 on Microsoft.Testing.Platform (opted in through `global.json`), with Shouldly for assertions.

```sh
dotnet test
```

With coverage (Cobertura, via `Microsoft.Testing.Extensions.CodeCoverage`):

```sh
dotnet test --coverage --coverage-output-format cobertura --results-directory TestResults
dotnet tool run reportgenerator -reports:'TestResults/*.cobertura.xml' -targetdir:coverage \
  -reporttypes:'JsonSummary;Html' -filefilters:'-**/obj/**;-*.g.cs;-*.generated.cs'
scripts/check-coverage.sh coverage/Summary.json   # ≥ 80% overall, ≥ 90% Domain + Application
```

Source-generated code is excluded from coverage. CI runs exactly these commands. Open `coverage/index.html` for the report.

In-process tests can't see the Docker publish output, so CI also runs `scripts/smoke-test-image.sh <image>` against the built image. You can run it locally after `docker build`.

## Format

```sh
dotnet format                         # fix
dotnet format --verify-no-changes     # what CI runs
```

Warnings are errors (`Directory.Build.props`), so the build fails on analyzer findings.

## Release

1. Bump `version` in `notification_api/config.yaml` and add a dated entry to `notification_api/CHANGELOG.md` (`## [X.Y.Z] - YYYY-MM-DD`). The release fails if the entry is missing or still says "Unreleased".
2. Merge to `main`.
3. Tag the commit `vX.Y.Z` (same version as `config.yaml`) and push the tag.
4. CI checks that the tag matches `config.yaml`, pushes the multi-arch images (`amd64`, `aarch64`) to GHCR, and creates a GitHub Release.
5. HA sees the new version in the add-on repository and offers the update.

**Image visibility:** the Supervisor pulls images anonymously, so the GHCR packages must be public. The images carry an `org.opencontainers.image.source` label that links them to this public repo, and since v0.1.0 both packages have been publicly pullable with no manual step. If a pull ever fails with "unauthorized" / "manifest unknown", check **Packages → Package settings → Change visibility** for `notification-api-amd64` and `notification-api-aarch64`.

## Install on Home Assistant

1. In HA, go to **Settings → Add-ons → Add-on Store → ⋮ → Repositories** (newer HA versions call these *Apps*).
2. Add `https://github.com/SpoilHeap/notification-api`.
3. Install **Notification API** and start it. Only HA admins see its panel.

The HA integration (Python, installed through HACS) lives in a separate repo, `notification-api-ha` (not created yet). Once both are installed, HA discovers the add-on through Supervisor discovery and sets up the integration with no manual token entry.

## Docs

- [Spec](docs/notification_api_spec.md): behaviour, API, acceptance criteria.
- [Engineering standards](docs/engineering_standards.md): architecture, testing, CI.
- [Brief](docs/brief.md): the original request.
- [Architecture decision records](docs/adr/):
  [0001 Record decisions](docs/adr/0001-record-architecture-decisions.md) ·
  [0002 HA OS add-on](docs/adr/0002-ha-os-add-on-deployment.md) ·
  [0003 Ingress auth, no roles](docs/adr/0003-ingress-authentication-no-roles.md) ·
  [0004 Blazor Static SSR](docs/adr/0004-blazor-static-ssr.md) ·
  [0005 SQLite](docs/adr/0005-sqlite-storage.md) ·
  [0006 No mediator](docs/adr/0006-no-mediator-library.md) ·
  [0007 Telegram and SMS](docs/adr/0007-telegram-and-sms-for-v1.md) ·
  [0008 Clean Architecture](docs/adr/0008-clean-architecture-layers.md) ·
  [0009 Container runs as root](docs/adr/0009-container-runs-as-root.md) ·
  [0010 xUnit v3 on MTP](docs/adr/0010-xunit-v3-on-microsoft-testing-platform.md) ·
  [0011 No NuGet lock files](docs/adr/0011-no-nuget-lock-files.md)
