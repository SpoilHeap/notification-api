#!/usr/bin/env bash
# Smoke-test a built image: start it and check the endpoints the add-on depends on.
# Usage: scripts/smoke-test-image.sh <image>
#
# Two containers:
#   1. Production settings, as on HA OS: /health is anonymous, the UI is rejected because the request
#      doesn't come from the Ingress proxy, and /api/v1 needs a bearer token.
#   2. Development with Ingress:DevFakeIngress (like docker-compose.dev.yml), so the UI renders and the
#      Blazor script can be fetched. This catches the publish bug from the spike (spec §13).
set -euo pipefail

IMAGE="${1:?usage: $0 <image>}"
PORT="${SMOKE_PORT:-18080}"
BASE="http://localhost:$PORT"
NAME=""

cleanup() {
  if [[ -n "$NAME" ]]; then
    docker logs "$NAME" >/tmp/"$NAME".log 2>&1 || true
    docker rm -f "$NAME" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

fail() { echo "✗ $*" >&2; echo "--- container log ---" >&2; docker logs "$NAME" >&2 || true; exit 1; }

# start <name> [docker run options...]: runs the image and waits for /health.
start() {
  cleanup
  NAME="$1"
  shift
  docker run -d --name "$NAME" -p "$PORT:8080" "$@" "$IMAGE" >/dev/null
  for _ in $(seq 1 30); do
    curl -fsS "$BASE/health" >/dev/null 2>&1 && return 0
    sleep 1
  done
  fail "/health did not return 2xx"
}

status() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

# 1. Production
start "notification-api-smoke-prod-$$"
echo "✓ /health (production)"

[[ "$(status "$BASE/")" == "403" ]] || fail "/ from outside Ingress did not return 403"
echo "✓ / is rejected outside Ingress"

[[ "$(status "$BASE/api/v1/ping")" == "401" ]] || fail "/api/v1/ping without a token did not return 401"
echo "✓ /api/v1/ping requires a token"

skipped=""
for _ in $(seq 1 10); do
  logs="$(docker logs "$NAME" 2>&1)"
  if [[ "$logs" == *"Supervisor discovery skipped"* ]]; then skipped=1; break; fi
  sleep 1
done
[[ -n "$skipped" ]] || fail "discovery was not reported as skipped"
echo "✓ discovery skipped without SUPERVISOR_TOKEN"

# 2. Development with fake Ingress
start "notification-api-smoke-dev-$$" \
  -e ASPNETCORE_ENVIRONMENT=Development -e Ingress__DevFakeIngress=true -e Storage__DataPath=/data

html="$(curl -fsS "$BASE/")" || fail "/ did not return 2xx"
echo "✓ / renders (fake Ingress)"

script="$(grep -oE '_framework/blazor\.web(\.[a-z0-9]+)?\.js' <<<"$html" | head -1)"
[[ -n "$script" ]] || fail "page has no blazor.web.js script tag"

# The spike regression: a bad restore drops blazor.web.js from the publish output (404).
read -r code ctype < <(curl -s -o /dev/null -w '%{http_code} %{content_type}\n' "$BASE/$script")
[[ "$code" == "200" ]] || fail "$script returned $code"
[[ "$ctype" == text/javascript* ]] || fail "$script served as '$ctype'"
echo "✓ $script ($ctype)"

echo "smoke test passed: $IMAGE"
