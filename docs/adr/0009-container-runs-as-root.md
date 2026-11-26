# 0009. The add-on container runs as root

- Status: Accepted, revisit
- Date: 2026-11-25

## Context

engineering_standards.md §6 requires a non-root user and a chiseled/minimal base image. The spec (§9.1) prefers the chiseled `aspnet:10.0` image, which defaults to a non-root user.

The HA Supervisor mounts `/data` into the add-on container owned by root. The app must write the SQLite database, Data Protection keys and the API token there. The chiseled image has no shell or package manager, so we can't `chown` `/data` at startup and then drop privileges.

## Decision

Use `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, and set `USER root` in the [Dockerfile](../../Dockerfile).

This is an accepted, scoped exception to the non-root rule. It applies only to the add-on image. It is acceptable because the Supervisor sandboxes add-ons: AppArmor profile, no host network and no extra privileges. The minimal image (no shell, no package manager) and the Trivy scan still apply.

## Alternatives considered

- **Debian-based `aspnet:10.0` with an entrypoint script** that `chown`s `/data` and then drops privileges with `gosu` or `setpriv`. Works, but adds a shell, more packages and a larger attack surface. It trades one risk for another.
- **A Supervisor option to mount `/data` with a different owner,** if one exists. Not found yet. This would be the cleanest fix.
- **Keep the default non-root user.** Fails: the app can't write to `/data`.

## Consequences

- The process runs as root inside the container. A container escape would start from root, so we rely on the Supervisor's sandbox.
- engineering_standards.md §6 notes this exception.
- Revisit when a Supervisor option for `/data` ownership is found, or if the sandbox assumptions change.
