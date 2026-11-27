# syntax=docker/dockerfile:1
#
# Multi-arch image for the HA add-on (amd64, aarch64).
# The SDK stage runs on the build machine's own architecture and cross-compiles with -a,
# so arm64 builds need no QEMU emulation.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
# The add-on version (release.yml passes config.yaml's version). /api/v1/ping reports it.
ARG VERSION=0.0.0-dev
WORKDIR /src

# Restore with the FULL source present. Restoring with only the .csproj files (the usual
# layer-caching trick) and then publishing with --no-restore silently drops Blazor's
# framework assets, so _framework/blazor.web.js 404s (spec §13, engineering_standards.md §8).
COPY . .
RUN dotnet publish src/NotificationApi.Web/NotificationApi.Web.csproj \
      -c Release -a "$TARGETARCH" --no-self-contained -o /out \
      -p:ContinuousIntegrationBuild=true -p:Version="$VERSION"

# Chiseled = minimal Ubuntu, no shell or package manager.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /out .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    Storage__DataPath=/data
EXPOSE 8080

# The Supervisor mounts /data owned by root, so the app runs as root inside the
# add-on container (ADR 0009). The Supervisor sandboxes the container (AppArmor,
# no host network, no privileges).
USER root

# Used by the Supervisor watchdog. No shell or curl in chiseled images, so the app probes itself.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["dotnet", "NotificationApi.Web.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "NotificationApi.Web.dll"]
